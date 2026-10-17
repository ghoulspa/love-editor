using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using ScintillaNET;

namespace LoveEditor;

partial class LuaAssist
{
    static readonly Regex DefinitionLead = new(@"^\s*function\s+love\.$", RegexOptions.Compiled);
    static readonly Regex DefinedCallback = new(@"^\s*(?:function\s+love\.(\w+)\s*\(|love\.(\w+)\s*=\s*function\b)", RegexOptions.Compiled);
    static readonly string[] CallbackOrder = { "load", "update", "draw", "keypressed", "keyreleased", "textinput", "mousepressed", "mousereleased", "mousemoved", "wheelmoved", "resize", "focus", "quit" };
    static readonly string[] AdvancedCallbacks = { "run", "errorhandler" };

    void OnCharAdded(object? sender, CharAddedEventArgs e)
    {
        if (!opened || editor.Selections.Count > 1) return;
        char typed = (char)e.Char;
        int caret = editor.CurrentPosition;
        if (tip == Tip.Hover) CancelTip();
        if (IsWordChar(typed))
        {
            if (sessionStart >= 0) PostRefilter();
            else
            {
                int start = WordStart(caret);
                if (!char.IsDigit(CharAt(start)) && InCode(caret - 1)) StartCompletion(start, null, false);
            }
        }
        else
        {
            if (sessionStart >= 0) EndSession(true);
            if ((typed == '.' || typed == ':') && CanCompleteMember(caret - 1)) StartCompletion(caret, typed.ToString(), false);
        }
        if (typed == '(' || typed == ',') RequestSignature();
        else if (tip == Tip.Signature) PostSignature();
    }

    bool CanCompleteMember(int dot)
    {
        char before = CharAt(dot - 1);
        if (before == ')' || before == ']') return InCode(dot);
        if (!IsWordChar(before)) return false;
        return !char.IsDigit(CharAt(WordStart(dot))) && InCode(dot);
    }

    public void TriggerCompletion()
    {
        if (!opened) return;
        EndSession(true);
        StartCompletion(WordStart(editor.CurrentPosition), null, true);
    }

    void StartCompletion(int start, string? trigger, bool manual)
    {
        if (language == null) return;
        sessionStart = start;
        sessionReceiver = Receiver(start);
        int lineStart = editor.Lines[editor.LineFromPosition(start)].Position;
        sessionDefinesCallback = sessionReceiver == "love" && DefinitionLead.IsMatch(editor.GetTextRange(lineStart, start - lineStart));
        sessionTriggered = trigger != null || manual;
        sessionItems = null;
        shown = new List<Entry>();
        int serial = ++completionSerial;
        Flush();
        object context = trigger == null ? new { triggerKind = 1 } : new { triggerKind = 2, triggerCharacter = trigger };
        language.Request("textDocument/completion", new { textDocument = new { uri }, position = Position(editor.CurrentPosition), context }, result =>
        {
            if (serial != completionSerial || editor.IsDisposed) return;
            sessionItems = ParseItems(result);
            Refilter();
        });
    }

    string Receiver(int start)
    {
        char before = CharAt(start - 1);
        if (before != '.' && before != ':') return "";
        int begin = start - 1;
        while (begin > 0 && (IsWordChar(CharAt(begin - 1)) || CharAt(begin - 1) == '.')) begin--;
        return editor.GetTextRange(begin, start - 1 - begin);
    }

    List<Entry> ParseItems(object? result)
    {
        var items = result is Dictionary<string, object> ? Json.Get(result, "items") : result;
        var callbacks = LoveApi.Callbacks;
        bool inConf = string.Equals(System.IO.Path.GetFileName(FilePath), "conf.lua", StringComparison.OrdinalIgnoreCase);
        var defined = sessionDefinesCallback ? DefinedCallbacks() : new HashSet<string>();
        var entries = new List<(string Sort, Entry Entry)>();
        foreach (var item in Json.Items(items))
        {
            var label = Json.Str(item, "label");
            if (string.IsNullOrEmpty(label)) continue;
            var edit = Json.Get(item, "textEdit");
            var insert = Json.Str(edit, "newText") ?? Json.Str(item, "insertText") ?? label!;
            bool isSnippet = Json.Int(item, "insertTextFormat", 1) == 2;
            var display = isSnippet ? label! : insert;
            if (display.Length == 0 || display.IndexOf(ListSeparator) >= 0 || display.IndexOf(ImageSeparator) >= 0) continue;
            var sort = Json.Str(item, "sortText") ?? label!;
            string? template = null;
            bool isCallback = sessionReceiver == "love" && !isSnippet && callbacks.ContainsKey(insert);
            if (sessionDefinesCallback)
            {
                if (!isCallback || (insert == "conf") != inConf || defined.Contains(insert)) continue;
                int rank = Array.IndexOf(CallbackOrder, insert);
                sort = (rank >= 0 ? rank.ToString("00") : Array.IndexOf(AdvancedCallbacks, insert) >= 0 ? "98" : "50") + insert;
                template = $"{insert}({callbacks[insert]})\n\t$0\nend";
            }
            else if (isCallback) sort = "~" + sort;
            int kind = Json.Int(item, "kind");
            int image = CompletionIcons.For(kind);
            if ((kind == 5 || kind == 6) && LoveApi.IsModule(sessionReceiver.Length == 0 ? insert : sessionReceiver + "." + insert)) image = CompletionIcons.Module;
            entries.Add((sort, new Entry
            {
                Item = item,
                Display = display,
                Filter = Json.Str(item, "filterText") ?? label!,
                Insert = insert,
                IsSnippet = isSnippet,
                Template = template,
                Image = image,
                Range = Json.Get(edit, "range") ?? Json.Get(edit, "insert"),
                Extra = Json.Items(item, "additionalTextEdits").ToList(),
            }));
        }
        return entries.OrderBy(e => e.Sort, StringComparer.Ordinal).Select(e => e.Entry).ToList();
    }

    HashSet<string> DefinedCallbacks()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        int current = editor.LineFromPosition(editor.CurrentPosition);
        for (int i = 0; i < editor.Lines.Count; i++)
        {
            if (i == current) continue;
            var match = DefinedCallback.Match(editor.Lines[i].Text);
            if (match.Success) defined.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        }
        return defined;
    }

    void PostRefilter()
    {
        if (refilterPosted || !editor.IsHandleCreated) return;
        refilterPosted = true;
        editor.BeginInvoke(new Action(() =>
        {
            refilterPosted = false;
            Refilter();
        }));
    }

    void Refilter()
    {
        if (sessionStart < 0 || sessionItems == null || editor.IsDisposed) return;
        int caret = editor.CurrentPosition;
        if (caret < sessionStart || editor.Selections.Count > 1)
        {
            EndSession(true);
            return;
        }
        var prefix = editor.GetTextRange(sessionStart, caret - sessionStart);
        if (!prefix.All(IsWordChar) || (prefix.Length == 0 && !sessionTriggered))
        {
            EndSession(true);
            return;
        }
        var seen = new HashSet<string>();
        var matches = sessionItems
            .Where(item => item.Filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Filter.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .Where(item => seen.Add(item.Display))
            .ToList();
        if (matches.Count == 0 || (matches.Count == 1 && !matches[0].IsSnippet && matches[0].Template == null && matches[0].Insert == prefix))
        {
            HideList();
            return;
        }
        if (imagesDpi != editor.DeviceDpi)
        {
            imagesDpi = editor.DeviceDpi;
            CompletionIcons.Register(editor, imagesDpi);
        }
        shown = matches;
        if (tip == Tip.Signature) resumeSignature = true;
        quiet = true;
        try { editor.AutoCShow(prefix.Length, string.Join(ListSeparator.ToString(), matches.Select(item => item.Display + ImageSeparator + item.Image))); }
        finally { quiet = false; }
        UpdateDocs();
    }

    void UpdateDocs()
    {
        var entry = SelectedEntry();
        if (entry == null || language == null)
        {
            HideDocs();
            return;
        }
        if (entry.Doc == null && entry.IsSnippet) entry.Doc = SnippetDoc(entry.Insert);
        if (entry.Doc != null)
        {
            ShowDocs(entry.Doc);
            return;
        }
        int serial = ++docsSerial;
        language.Request("completionItem/resolve", entry.Item, result =>
        {
            entry.Doc = LuaDocs.Read(Json.Get(result, "documentation"));
            if (serial == docsSerial && SelectedEntry() == entry) ShowDocs(entry.Doc);
        });
    }

    void ShowDocs(LuaDocs.Doc doc)
    {
        var list = editor.AutoCActive && !doc.IsEmpty ? CompletionDocs.FindList() : null;
        if (list == null)
        {
            HideDocs();
            return;
        }
        docs ??= new CompletionDocs();
        docs.ShowBeside(editor, list.Value, doc);
    }

    void HideDocs()
    {
        docsSerial++;
        if (docs != null && docs.Visible) docs.Hide();
    }

    static LuaDocs.Doc SnippetDoc(string body)
    {
        var text = new StringBuilder();
        ParseSnippet(body, 0, text, "\n", new List<Placeholder>(), false);
        var doc = new LuaDocs.Doc();
        doc.Code.AddRange(text.ToString().Split('\n'));
        return doc;
    }

    void HideList()
    {
        HideDocs();
        if (!editor.AutoCActive) return;
        quiet = true;
        try { editor.AutoCCancel(); }
        finally { quiet = false; }
    }

    void EndSession(bool hide)
    {
        sessionStart = -1;
        sessionItems = null;
        shown = new List<Entry>();
        completionSerial++;
        if (hide) HideList();
        HideDocs();
        ResumeSignature();
    }

    Entry? SelectedEntry()
    {
        int index = editor.AutoCActive ? editor.AutoCCurrent : -1;
        return index >= 0 && index < shown.Count ? shown[index] : null;
    }

    void OnAutoCSelection(object? sender, AutoCSelectionEventArgs e)
    {
        var entry = SelectedEntry();
        if (sessionStart < 0 || entry == null) return;
        int start = sessionStart;
        editor.BeginInvoke(new Action(() => Accept(entry, start)));
        HideList();
        EndSession(false);
    }

    void Accept(Entry entry, int start)
    {
        if (editor.IsDisposed) return;
        int caret = editor.CurrentPosition;
        int from = entry.Range != null ? PositionOf(Json.Get(entry.Range, "start")) : start;
        if (from > caret) from = start;
        editor.BeginUndoAction();
        try
        {
            editor.SetTargetRange(from, caret);
            var line = editor.Lines[editor.LineFromPosition(caret)];
            bool lineEnds = editor.GetTextRange(caret, line.Position + line.Text.TrimEnd('\r', '\n').Length - caret).Trim().Length == 0;
            if (entry.Template != null && lineEnds) InsertSnippet(from, entry.Template);
            else if (entry.IsSnippet) InsertSnippet(from, entry.Insert);
            else
            {
                editor.ReplaceTarget(entry.Insert);
                editor.GotoPosition(from + entry.Insert.Length);
            }
            var extra = entry.Extra
                .Select(edit => (Start: PositionOf(Json.Get(edit, "range", "start")), End: PositionOf(Json.Get(edit, "range", "end")), Text: Json.Str(edit, "newText") ?? ""))
                .Where(edit => edit.End <= from)
                .OrderByDescending(edit => edit.Start)
                .ToList();
            foreach (var edit in extra)
            {
                editor.SetTargetRange(edit.Start, edit.End);
                editor.ReplaceTarget(edit.Text);
            }
        }
        finally { editor.EndUndoAction(); }
    }

    void InsertSnippet(int from, string body)
    {
        int line = editor.LineFromPosition(from);
        var newline = (editor.EolMode == Eol.CrLf ? "\r\n" : editor.EolMode == Eol.Cr ? "\r" : "\n") + new string(' ', editor.Lines[line].Indentation);
        var text = new StringBuilder();
        var stops = new List<Placeholder>();
        ParseSnippet(body, 0, text, newline, stops, false);
        editor.ReplaceTarget(text.ToString());
        foreach (var stop in stops) stop.Start += from;
        var ordered = stops.Where(s => s.Number > 0).GroupBy(s => s.Number).OrderBy(g => g.Key).Select(g => g.First()).ToList();
        var final = stops.FirstOrDefault(s => s.Number == 0) ?? new Placeholder { Number = 0, Start = from + text.Length };
        ordered.Add(final);
        if (ordered.Count == 1)
        {
            editor.GotoPosition(final.Start);
            return;
        }
        EndSnippet();
        placeholders.AddRange(ordered);
        snippet = new Placeholder { Number = -1, Start = from, Length = text.Length };
        placeholderIndex = -1;
        MovePlaceholder(1);
    }

    static int ParseSnippet(string body, int index, StringBuilder text, string newline, List<Placeholder> stops, bool nested)
    {
        while (index < body.Length)
        {
            char c = body[index];
            if (c == '\\' && index + 1 < body.Length)
            {
                text.Append(body[index + 1]);
                index += 2;
                continue;
            }
            if (nested && c == '}') return index + 1;
            if (c == '$' && index + 1 < body.Length)
            {
                int digits = index + 1;
                bool braced = body[digits] == '{';
                if (braced) digits++;
                int end = digits;
                while (end < body.Length && char.IsDigit(body[end])) end++;
                if (end > digits)
                {
                    int number = int.Parse(body.Substring(digits, end - digits));
                    int begin = text.Length;
                    if (!braced) index = end;
                    else if (end < body.Length && body[end] == ':') index = ParseSnippet(body, end + 1, text, newline, stops, true);
                    else if (end < body.Length && body[end] == '|')
                    {
                        int close = body.IndexOf("|}", end + 1, StringComparison.Ordinal);
                        var choices = body.Substring(end + 1, (close < 0 ? body.Length : close) - end - 1);
                        text.Append(choices.Split(',')[0]);
                        index = close < 0 ? body.Length : close + 2;
                    }
                    else
                    {
                        int close = body.IndexOf('}', end);
                        index = close < 0 ? body.Length : close + 1;
                    }
                    stops.Add(new Placeholder { Number = number, Start = begin, Length = text.Length - begin });
                    continue;
                }
            }
            if (c == '\r') { index++; continue; }
            if (c == '\n') text.Append(newline);
            else if (c == '\t') text.Append(' ', IndentSize);
            else text.Append(c);
            index++;
        }
        return index;
    }

    void MovePlaceholder(int step)
    {
        if (snippet == null) return;
        int next = Math.Max(0, placeholderIndex + step);
        if (next >= placeholders.Count) next = placeholders.Count - 1;
        placeholderIndex = next;
        var target = placeholders[next];
        editor.SetSelection(target.End, target.Start);
        editor.ScrollCaret();
        if (target.Number == 0) EndSnippet();
        else PostPlaceholders();
    }

    void EndSnippet()
    {
        if (snippet == null) return;
        editor.IndicatorCurrent = PlaceholderIndicator;
        editor.IndicatorClearRange(0, editor.TextLength);
        snippet = null;
        placeholders.Clear();
        placeholderIndex = -1;
    }

    void ShiftPlaceholders(int position, int length)
    {
        var current = placeholderIndex >= 0 && placeholderIndex < placeholders.Count ? placeholders[placeholderIndex] : null;
        foreach (var stop in placeholders.Append(snippet!))
        {
            bool grows = stop == current || stop == snippet;
            if (position < stop.Start || (position == stop.Start && !grows)) stop.Start += length;
            else if (position <= stop.End && (grows || position < stop.End)) stop.Length += length;
        }
        PostPlaceholders();
    }

    void ShrinkPlaceholders(int position, int length)
    {
        int end = position + length;
        foreach (var stop in placeholders.Append(snippet!))
        {
            int start = stop.Start <= position ? stop.Start : stop.Start >= end ? stop.Start - length : position;
            int stopEnd = stop.End <= position ? stop.End : stop.End >= end ? stop.End - length : position;
            stop.Start = start;
            stop.Length = Math.Max(0, stopEnd - start);
        }
        PostPlaceholders();
    }

    void PostPlaceholders()
    {
        if (placeholdersPosted || !editor.IsHandleCreated) return;
        placeholdersPosted = true;
        editor.BeginInvoke(new Action(() =>
        {
            placeholdersPosted = false;
            if (snippet == null || editor.IsDisposed) return;
            editor.IndicatorCurrent = PlaceholderIndicator;
            editor.IndicatorClearRange(0, editor.TextLength);
            foreach (var stop in placeholders.Where(s => s.Number > 0 && s.Length > 0)) editor.IndicatorFillRange(stop.Start, stop.Length);
        }));
    }
}
