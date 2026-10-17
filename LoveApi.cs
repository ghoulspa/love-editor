using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LoveEditor;

static class LoveApi
{
    public static readonly string LibraryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lua-language-server", "love2d");

    static readonly Regex CallbackPattern = new(@"^function love\.(\w+)\(([^)]*)\)", RegexOptions.Compiled);
    static readonly Regex UtilityPattern = new(@"^(get|set|has|is)[A-Z]", RegexOptions.Compiled);
    static readonly Lazy<HashSet<string>> modules = new(LoadModules);
    static readonly Lazy<Dictionary<string, string>> callbacks = new(LoadCallbacks);

    public static bool IsModule(string name) => modules.Value.Contains(name);

    public static IReadOnlyDictionary<string, string> Callbacks => callbacks.Value;

    public static void Warm()
    {
        _ = modules.Value;
        _ = callbacks.Value;
    }

    static HashSet<string> LoadModules()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(LibraryDirectory, "*.lua", SearchOption.AllDirectories))
            {
                string? declared = null;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.StartsWith("---@class ", StringComparison.Ordinal))
                    {
                        declared = line.Substring(10).Split(':')[0].Trim();
                        continue;
                    }
                    if (declared != null && line.StartsWith(declared + " = {}", StringComparison.Ordinal)) found.Add(declared);
                    if (!line.StartsWith("---", StringComparison.Ordinal)) declared = null;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return found;
    }

    static Dictionary<string, string> LoadCallbacks()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(LibraryDirectory, "love.lua")))
            {
                var match = CallbackPattern.Match(line);
                if (match.Success && !UtilityPattern.IsMatch(match.Groups[1].Value)) found[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return found;
    }
}
