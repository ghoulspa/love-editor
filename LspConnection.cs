using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LoveEditor;

sealed class LspConnection : IDisposable
{
    readonly Process process;
    readonly JobObject job = new();
    readonly Stream input;
    readonly Stream output;
    readonly JavaScriptSerializer json = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
    readonly BlockingCollection<byte[]> outgoing = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<object?>> pending = new();
    int nextId;
    int disposed;

    public event Action<string, object?>? Notified;
    public event Action? Exited;
    public Func<string, object?, object?>? RequestHandler { get; set; }

    LspConnection(Process process)
    {
        this.process = process;
        job.Assign(process.Handle);
        input = process.StandardInput.BaseStream;
        output = process.StandardOutput.BaseStream;
        new Thread(ReadLoop) { IsBackground = true, Name = "lsp-read" }.Start();
        new Thread(WriteLoop) { IsBackground = true, Name = "lsp-write" }.Start();
    }

    public static LspConnection Start(string executable, string arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory,
        };
        return new LspConnection(Process.Start(info) ?? throw new InvalidOperationException("The language server didn't start."));
    }

    public Task<object?> Request(string method, object? parameters)
    {
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (disposed != 0)
        {
            completion.SetResult(null);
            return completion.Task;
        }
        pending[id] = completion;
        Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
        return completion.Task;
    }

    public void Notify(string method, object? parameters) =>
        Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    void Send(object message)
    {
        if (disposed != 0) return;
        byte[] body;
        lock (json) body = Encoding.UTF8.GetBytes(json.Serialize(message));
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        var packet = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        Buffer.BlockCopy(body, 0, packet, header.Length, body.Length);
        try { outgoing.Add(packet); }
        catch (InvalidOperationException) { }
    }

    void WriteLoop()
    {
        try
        {
            foreach (var packet in outgoing.GetConsumingEnumerable())
            {
                input.Write(packet, 0, packet.Length);
                input.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    void ReadLoop()
    {
        try
        {
            while (true)
            {
                int length = ReadHeader();
                if (length < 0) break;
                var body = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int count = output.Read(body, read, length - read);
                    if (count <= 0) return;
                    read += count;
                }
                object? message;
                try { message = json.DeserializeObject(Encoding.UTF8.GetString(body)); }
                catch (ArgumentException) { continue; }
                if (message is Dictionary<string, object> map) Dispatch(map);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            foreach (var key in pending.Keys)
                if (pending.TryRemove(key, out var waiting)) waiting.TrySetResult(null);
            Exited?.Invoke();
        }
    }

    int ReadHeader()
    {
        var line = new StringBuilder();
        int length = -1;
        while (true)
        {
            int b = output.ReadByte();
            if (b < 0) return -1;
            if (b == '\r') continue;
            if (b != '\n')
            {
                line.Append((char)b);
                continue;
            }
            if (line.Length == 0)
            {
                if (length >= 0) return length;
                continue;
            }
            var text = line.ToString();
            line.Clear();
            if (text.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.Substring(15).Trim(), out var value)) length = value;
        }
    }

    void Dispatch(Dictionary<string, object> message)
    {
        message.TryGetValue("method", out var methodValue);
        message.TryGetValue("params", out var parameters);
        bool hasId = message.TryGetValue("id", out var id);
        if (methodValue is string method)
        {
            if (!hasId)
            {
                Notified?.Invoke(method, parameters);
                return;
            }
            object? result = null;
            try { result = RequestHandler?.Invoke(method, parameters); }
            catch (Exception) { }
            Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            return;
        }
        if (id is int number && pending.TryRemove(number, out var completion))
            completion.TrySetResult(message.TryGetValue("result", out var result) ? result : null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        outgoing.CompleteAdding();
        job.Terminate();
        job.Dispose();
        process.Dispose();
    }
}
