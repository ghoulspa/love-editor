using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScintillaNET;

namespace LoveEditor;

partial class LuaAssist
{
    public void FormatDocument() => Format(false);

    public void FormatSelection() => Format(true);

    void Format(bool selection)
    {
        if (!opened || language == null) return;
        Flush();
        int requested = version;
        var options = new { tabSize = IndentSize, insertSpaces = true };
        if (selection && editor.SelectionStart != editor.SelectionEnd)
        {
            int first = editor.LineFromPosition(editor.SelectionStart);
            int last = editor.LineFromPosition(editor.SelectionEnd);
            if (last > first && editor.SelectionEnd == editor.Lines[last].Position) last--;
            var range = new { start = new { line = first, character = 0 }, end = new { line = last, character = editor.Lines[last].Text.TrimEnd('\r', '\n').Length } };
            language.Request("textDocument/rangeFormatting", new { textDocument = new { uri }, range, options }, result => ApplyFormatting(result, requested));
        }
        else language.Request("textDocument/formatting", new { textDocument = new { uri }, options }, result => ApplyFormatting(result, requested));
    }

    void ApplyFormatting(object? result, int requested)
    {
        if (editor.IsDisposed || changed || version != requested) return;
        var edits = Json.Items(result).ToList();
        if (edits.Count == 0) return;
        var oldText = editor.Text;
        var eol = editor.EolMode == Eol.CrLf ? "\r\n" : editor.EolMode == Eol.Cr ? "\r" : "\n";
        var newText = LinePattern.Replace(ApplyEdits(oldText, edits), eol);
        if (newText != oldText) ReplaceKeepingPlace(oldText, newText);
    }

    static string ApplyEdits(string text, List<object> edits)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) starts.Add(i + 1);
        int Offset(object? position)
        {
            int line = Json.Int(position, "line");
            if (line >= starts.Count) return text.Length;
            int lineEnd = line + 1 < starts.Count ? starts[line + 1] : text.Length;
            return Math.Min(starts[line] + Json.Int(position, "character"), lineEnd);
        }
        var builder = new StringBuilder(text);
        foreach (var edit in edits
            .Select(edit => (Start: Offset(Json.Get(edit, "range", "start")), End: Offset(Json.Get(edit, "range", "end")), Text: Json.Str(edit, "newText") ?? ""))
            .OrderByDescending(edit => edit.Start))
        {
            builder.Remove(edit.Start, Math.Max(0, edit.End - edit.Start));
            builder.Insert(edit.Start, edit.Text);
        }
        return builder.ToString();
    }

    void ReplaceKeepingPlace(string oldText, string newText)
    {
        var oldLines = LinePattern.Split(oldText);
        var newLines = LinePattern.Split(newText);
        int sameTop = 0;
        while (sameTop < oldLines.Length && sameTop < newLines.Length && oldLines[sameTop] == newLines[sameTop]) sameTop++;
        int sameBottom = 0;
        while (sameBottom < oldLines.Length - sameTop && sameBottom < newLines.Length - sameTop && oldLines[oldLines.Length - 1 - sameBottom] == newLines[newLines.Length - 1 - sameBottom]) sameBottom++;

        int caret = editor.CurrentPosition;
        int caretLine = editor.LineFromPosition(caret);
        int caretColumn = caret - editor.Lines[caretLine].Position;
        int firstVisible = editor.FirstVisibleLine;

        int head = 0;
        while (head < oldText.Length && head < newText.Length && oldText[head] == newText[head]) head++;
        int tail = 0;
        while (tail < oldText.Length - head && tail < newText.Length - head && oldText[oldText.Length - 1 - tail] == newText[newText.Length - 1 - tail]) tail++;
        editor.BeginUndoAction();
        try
        {
            editor.SetTargetRange(head, oldText.Length - tail);
            editor.ReplaceTarget(newText.Substring(head, newText.Length - head - tail));
        }
        finally { editor.EndUndoAction(); }

        int newLine;
        int column = caretColumn;
        if (caretLine < sameTop) newLine = caretLine;
        else if (caretLine >= oldLines.Length - sameBottom) newLine = caretLine + newLines.Length - oldLines.Length;
        else
        {
            newLine = oldLines.Length == newLines.Length ? caretLine : Math.Min(caretLine, newLines.Length - sameBottom - 1);
            newLine = Math.Max(0, Math.Min(newLine, newLines.Length - 1));
            int oldIndent = oldLines[caretLine].Length - oldLines[caretLine].TrimStart().Length;
            int newIndent = newLines[newLine].Length - newLines[newLine].TrimStart().Length;
            column = Math.Max(newIndent, newIndent + caretColumn - oldIndent);
        }
        newLine = Math.Max(0, Math.Min(newLine, editor.Lines.Count - 1));
        var target = editor.Lines[newLine];
        int position = target.Position + Math.Min(column, target.Text.TrimEnd('\r', '\n').Length);
        editor.SetSelection(position, position);
        editor.FirstVisibleLine = firstVisible;
    }
}
