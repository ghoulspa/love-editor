using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoveEditor;

sealed class LuaLanguage : IDisposable
{
    const int MaxLaunches = 3;

    static readonly string ServerDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lua-language-server");
    static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoveEditor", "lua-language-server");

    static readonly Dictionary<string, object> Settings = new()
    {
        ["runtime"] = new Dictionary<string, object>
        {
            ["version"] = "LuaJIT",
            ["special"] = new Dictionary<string, object> { ["love.filesystem.load"] = "loadfile" },
        },
        ["workspace"] = new Dictionary<string, object>
        {
            ["library"] = new[] { LoveApi.LibraryDirectory },
            ["checkThirdParty"] = false,
        },
        ["format"] = new Dictionary<string, object>
        {
            ["defaultConfig"] = new Dictionary<string, object> { ["indent_style"] = "space", ["indent_size"] = "2" },
        },
        ["completion"] = new Dictionary<string, object> { ["callSnippet"] = "Disable" },
        ["telemetry"] = new Dictionary<string, object> { ["enable"] = false },
        ["window"] = new Dictionary<string, object> { ["progressBar"] = false, ["statusBar"] = false },
    };

    readonly string root;
    readonly System.Windows.Forms.WindowsFormsSynchronizationContext ui;
    readonly List<LuaAssist> documents = new();
    readonly FileSystemWatcher watcher;
    readonly Dictionary<string, int> fileChanges = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Forms.Timer fileTimer = new() { Interval = 300 };
    LspConnection? connection;
    int launches;
    bool disposed;

    public static LuaLanguage? Current { get; private set; }

    public static void Open(string root)
    {
        Close();
        if (Environment.OSVersion.Version < new Version(6, 2)) return;
        Current = new LuaLanguage(root);
    }

    public static void Close()
    {
        Current?.Dispose();
        Current = null;
    }

    LuaLanguage(string root)
    {
        this.root = root;
        ui = SynchronizationContext.Current as System.Windows.Forms.WindowsFormsSynchronizationContext ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
        watcher = new FileSystemWatcher(root, "*.lua") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        watcher.Created += (s, e) => QueueFileChange(e.FullPath, 1);
        watcher.Changed += (s, e) => QueueFileChange(e.FullPath, 2);
        watcher.Deleted += (s, e) => QueueFileChange(e.FullPath, 3);
        watcher.Renamed += (s, e) =>
        {
            QueueFileChange(e.OldFullPath, 3);
            QueueFileChange(e.FullPath, 1);
        };
        fileTimer.Tick += (s, e) => SendFileChanges();
        try { watcher.EnableRaisingEvents = true; }
        catch (Exception) { }
        Launch();
    }

    public bool IsReady => connection != null;

    public static string ToUri(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 2 || full[1] != ':') return new Uri(full).AbsoluteUri;
        var builder = new StringBuilder("file:///").Append(char.ToLowerInvariant(full[0])).Append("%3A");
        foreach (var part in full.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            builder.Append('/').Append(Uri.EscapeDataString(part));
        return builder.ToString();
    }

    public static string? FromUri(string? uri)
    {
        if (uri == null || !uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Uri.UnescapeDataString(uri.Substring(7));
        if (path.Length >= 3 && path[0] == '/' && path[2] == ':') path = path.Substring(1);
        try { return Path.GetFullPath(path.Replace('/', '\\')); }
        catch (Exception) { return null; }
    }

    public static bool IsLibrary(string? uri)
    {
        var path = FromUri(uri);
        return path != null && path.StartsWith(LoveApi.LibraryDirectory + "\\", StringComparison.OrdinalIgnoreCase);
    }

    void Launch()
    {
        var executable = Path.Combine(ServerDirectory, "bin", "lua-language-server.exe");
        if (disposed || launches >= MaxLaunches || !File.Exists(executable)) return;
        launches++;
        var arguments = $"--locale=en-us --logpath=\"{Path.Combine(DataDirectory, "log")}\" --metapath=\"{Path.Combine(DataDirectory, "meta")}\"";
        var parameters = InitializeParameters();
        Task.Run(async () =>
        {
            LspConnection started;
            try { started = LspConnection.Start(executable, arguments, root); }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return;
            }
            started.RequestHandler = HandleServerRequest;
            started.Notified += (method, value) => ui.Post(_ => OnNotification(method, value), null);
            started.Exited += () => ui.Post(_ => OnExited(started), null);
            var result = await started.Request("initialize", parameters).ConfigureAwait(false);
            if (result == null)
            {
                started.Dispose();
                return;
            }
            started.Notify("initialized", new Dictionary<string, object>());
            LoveApi.Warm();
            ui.Post(_ => OnInitialized(started), null);
        });
    }

    object InitializeParameters()
    {
        var rootUri = ToUri(root);
        return new
        {
            processId = Process.GetCurrentProcess().Id,
            clientInfo = new { name = "Love Editor" },
            locale = "en-us",
            rootUri,
            rootPath = root,
            workspaceFolders = new[] { new { uri = rootUri, name = Path.GetFileName(root) } },
            capabilities = new
            {
                general = new { positionEncodings = new[] { "utf-16" } },
                workspace = new { configuration = true, workspaceFolders = true, didChangeWatchedFiles = new { dynamicRegistration = false } },
                window = new { workDoneProgress = true },
                textDocument = new
                {
                    synchronization = new { didSave = true },
                    completion = new
                    {
                        completionItem = new { snippetSupport = true, documentationFormat = new[] { "markdown", "plaintext" } },
                        contextSupport = true,
                    },
                    hover = new { contentFormat = new[] { "markdown", "plaintext" } },
                    signatureHelp = new
                    {
                        signatureInformation = new
                        {
                            documentationFormat = new[] { "markdown", "plaintext" },
                            parameterInformation = new { labelOffsetSupport = true },
                            activeParameterSupport = true,
                        },
                    },
                    publishDiagnostics = new { relatedInformation = true, versionSupport = true, tagSupport = new { valueSet = new[] { 1, 2 } } },
                    formatting = new { },
                    rangeFormatting = new { },
                },
            },
        };
    }

    static object? HandleServerRequest(string method, object? parameters)
    {
        if (method != "workspace/configuration") return null;
        return Json.Items(parameters, "items").Select(item => Configuration(Json.Str(item, "section"))).ToArray();
    }

    static object? Configuration(string? section) => section switch
    {
        "Lua" => Settings,
        "files.associations" => new Dictionary<string, object>(),
        "files.exclude" => new Dictionary<string, object>(),
        "editor.semanticHighlighting.enabled" => false,
        "editor.acceptSuggestionOnEnter" => "on",
        _ => null,
    };

    void OnInitialized(LspConnection started)
    {
        if (disposed)
        {
            started.Dispose();
            return;
        }
        connection = started;
        foreach (var document in documents.ToList()) document.ServerReady();
    }

    void OnExited(LspConnection exited)
    {
        exited.Dispose();
        if (disposed) return;
        if (connection == exited)
        {
            connection = null;
            foreach (var document in documents.ToList()) document.ServerLost();
        }
        Launch();
    }

    void OnNotification(string method, object? parameters)
    {
        if (disposed || method != "textDocument/publishDiagnostics") return;
        var path = FromUri(Json.Str(parameters, "uri"));
        if (path == null) return;
        documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase))?.SetDiagnostics(parameters);
    }

    public void Register(LuaAssist document)
    {
        documents.Add(document);
        if (connection != null) document.ServerReady();
    }

    public void Unregister(LuaAssist document) => documents.Remove(document);

    public void Notify(string method, object? parameters) => connection?.Notify(method, parameters);

    public void Request(string method, object? parameters, Action<object?> callback)
    {
        if (connection == null)
        {
            callback(null);
            return;
        }
        connection.Request(method, parameters).ContinueWith(task => ui.Post(_ =>
        {
            if (!disposed) callback(task.Status == TaskStatus.RanToCompletion ? task.Result : null);
        }, null), TaskScheduler.Default);
    }

    void QueueFileChange(string path, int type) => ui.Post(_ =>
    {
        if (disposed) return;
        fileChanges[path] = type;
        fileTimer.Stop();
        fileTimer.Start();
    }, null);

    void SendFileChanges()
    {
        fileTimer.Stop();
        if (fileChanges.Count == 0) return;
        var changes = fileChanges.Select(pair => new { uri = ToUri(pair.Key), type = pair.Value }).ToArray();
        fileChanges.Clear();
        Notify("workspace/didChangeWatchedFiles", new { changes });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        watcher.Dispose();
        fileTimer.Dispose();
        connection?.Dispose();
        connection = null;
    }
}
