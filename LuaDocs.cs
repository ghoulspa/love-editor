using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LoveEditor;

static class LuaDocs
{
    public const int Width = 80;
    const int MaxTags = 10;

    static readonly Regex LinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);
    static readonly Regex LinkPattern = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex TagPattern = new(@"^@\*(\w+)\*\s*", RegexOptions.Compiled);
    static readonly Regex EmphasisPattern = new(@"\*{1,2}([^*\s][^*]*?)\*{1,2}", RegexOptions.Compiled);
    static readonly Regex CommaPattern = new(@"(?<=, )", RegexOptions.Compiled);

    public sealed class Doc
    {
        public readonly List<string> Code = new();
        public readonly List<string> Paragraphs = new();
        public readonly List<string> Tags = new();

        public bool IsEmpty => Code.Count == 0 && Paragraphs.Count == 0 && Tags.Count == 0;
    }

    public static Doc Read(object? contents) => Parse(Markdown(contents));

    public static (string Text, int CodeLength) Hover(object? contents)
    {
        var parsed = Parse(Markdown(contents));
        var lines = new List<string>();
        int none = -1;
        foreach (var code in parsed.Code) lines.Add(WrapCode(code, ref none, ref none));
        int codeLength = string.Join("\n", lines).Length;
        if (parsed.Paragraphs.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.AddRange(Wrap(parsed.Paragraphs[0]));
        }
        if (parsed.Tags.Count > 0)
        {
            lines.Add("");
            foreach (var tag in parsed.Tags.Take(MaxTags)) lines.AddRange(Wrap(tag));
            if (parsed.Tags.Count > MaxTags) lines.Add("…");
        }
        return (string.Join("\n", lines), codeLength);
    }

    public static string Summary(object? documentation)
    {
        var parsed = Parse(Markdown(documentation));
        return parsed.Paragraphs.Count == 0 ? "" : string.Join("\n", Wrap(parsed.Paragraphs[0]));
    }

    public static IEnumerable<string> Wrap(string line)
    {
        if (line.Length <= Width)
        {
            yield return line;
            yield break;
        }
        var current = new StringBuilder();
        foreach (var word in line.Split(' '))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > Width)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    public static string WrapCode(string code, ref int start, ref int end)
    {
        if (code.Length <= Width) return code;
        var builder = new StringBuilder();
        var breaks = new List<int>();
        int origin = 0;
        int lineLength = 0;
        foreach (var piece in CommaPattern.Split(code))
        {
            if (lineLength > 0 && lineLength + piece.Length > Width)
            {
                builder.Length--;
                builder.Append("\n    ");
                breaks.Add(origin);
                lineLength = 4;
            }
            builder.Append(piece);
            lineLength += piece.Length;
            origin += piece.Length;
        }
        int Map(int offset) => offset < 0 ? offset : offset + 4 * breaks.Count(b => b <= offset);
        start = Map(start);
        end = Map(end);
        return builder.ToString();
    }

    static string Markdown(object? contents) => contents switch
    {
        null => "",
        string text => text,
        Dictionary<string, object> map when map.ContainsKey("language") => "```lua\n" + Json.Str(map, "value") + "\n```",
        Dictionary<string, object> map => Json.Str(map, "value") ?? "",
        _ => string.Join("\n\n", Json.Items(contents).Select(Markdown)),
    };

    static Doc Parse(string markdown)
    {
        var parsed = new Doc();
        var paragraph = new List<string>();
        bool inCode = false;
        bool leadingCode = true;
        void EndParagraph()
        {
            if (paragraph.Count > 0) parsed.Paragraphs.Add(string.Join(" ", paragraph));
            paragraph.Clear();
        }
        foreach (var raw in LinePattern.Split(markdown))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                if (inCode) EndParagraph();
                else if (parsed.Code.Count > 0) leadingCode = false;
                continue;
            }
            if (inCode)
            {
                if (leadingCode && parsed.Paragraphs.Count == 0 && parsed.Tags.Count == 0) parsed.Code.Add(line);
                continue;
            }
            var text = line.Trim();
            if (text.StartsWith("[Open in Browser]", StringComparison.Ordinal)) continue;
            leadingCode = false;
            if (text.Length == 0 || text == "---")
            {
                EndParagraph();
                continue;
            }
            var tag = TagPattern.Match(text);
            if (tag.Success)
            {
                EndParagraph();
                var name = tag.Groups[1].Value;
                parsed.Tags.Add((name == "param" ? "" : name + " ") + Clean(text.Substring(tag.Length)));
                continue;
            }
            paragraph.Add(Clean(text));
        }
        EndParagraph();
        return parsed;
    }

    static string Clean(string text) => EmphasisPattern.Replace(LinkPattern.Replace(text, "$1"), "$1").Replace("`", "");
}
