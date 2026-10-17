using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScintillaNET;

namespace LoveEditor;

static class EditorClipboard
{
    const string LineFormat = "MSDEVLineSelect";
    const string VisualStudioLineFormat = "VisualStudioEditorOperationsLineCutCopyClipboardTag";
    const string ColumnFormat = "MSDEVColumnSelect";
    const int SciSelectionIsRectangle = 2372;

    static readonly Regex LinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    public static void Copy(Scintilla editor, bool cut)
    {
        if (editor.DirectMessage(SciSelectionIsRectangle) != IntPtr.Zero)
        {
            if (cut) editor.Cut();
            else editor.Copy();
            return;
        }
        var ranges = Ranges(editor);
        if (ranges.All(range => range.Start == range.End))
        {
            CopyLines(editor, ranges, cut);
            return;
        }
        if (ranges.Count == 1)
        {
            if (cut) editor.Cut();
            else editor.Copy();
            return;
        }
        var text = string.Join(LineEnding(editor), ranges.Select(range => editor.GetTextRange(range.Start, range.End - range.Start)));
        if (!SetClipboard(text, false) || !cut) return;
        editor.BeginUndoAction();
        try
        {
            foreach (var range in ranges.AsEnumerable().Reverse())
            {
                editor.SetTargetRange(range.Start, range.End);
                editor.ReplaceTarget("");
            }
        }
        finally { editor.EndUndoAction(); }
    }

    static void CopyLines(Scintilla editor, List<(int Start, int End)> ranges, bool cut)
    {
        var eol = LineEnding(editor);
        var lines = ranges.Select(range => editor.LineFromPosition(range.Start)).Distinct().OrderBy(line => line).ToList();
        var text = string.Concat(lines.Select(line =>
        {
            var content = editor.Lines[line].Text;
            return content.EndsWith("\n", StringComparison.Ordinal) || content.EndsWith("\r", StringComparison.Ordinal) ? content : content + eol;
        }));
        if (!SetClipboard(text, true) || !cut) return;
        editor.BeginUndoAction();
        try
        {
            foreach (var line in lines.AsEnumerable().Reverse())
            {
                int start = editor.Lines[line].Position;
                int end = line + 1 < editor.Lines.Count ? editor.Lines[line + 1].Position : editor.TextLength;
                if (line + 1 >= editor.Lines.Count && line > 0) start = editor.Lines[line - 1].Position + editor.Lines[line - 1].Text.TrimEnd('\r', '\n').Length;
                editor.SetTargetRange(start, end);
                editor.ReplaceTarget("");
            }
        }
        finally { editor.EndUndoAction(); }
    }

    public static void Paste(Scintilla editor)
    {
        string? text = null;
        bool line = false;
        bool column = false;
        try
        {
            var data = Clipboard.GetDataObject();
            text = data?.GetData(DataFormats.UnicodeText) as string;
            line = data != null && (data.GetDataPresent(LineFormat) || data.GetDataPresent(VisualStudioLineFormat));
            column = data != null && data.GetDataPresent(ColumnFormat);
        }
        catch (ExternalException) { }
        var ranges = Ranges(editor);
        if (text == null || column || ranges.Count == 1)
        {
            editor.Paste();
            return;
        }
        var eol = LineEnding(editor);
        var pieces = LinePattern.Split(text).ToList();
        if (line && pieces.Count > 1 && pieces[pieces.Count - 1].Length == 0) pieces.RemoveAt(pieces.Count - 1);
        bool spread = pieces.Count == ranges.Count;
        var whole = LinePattern.Replace(text, eol);
        var edits = new List<(int At, int Removed, string Text, int Caret)>();
        for (int i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (line && range.Start == range.End)
            {
                int at = editor.Lines[editor.LineFromPosition(range.Start)].Position;
                var insert = spread ? pieces[i] + eol : whole;
                edits.Add((at, 0, insert, range.Start + insert.Length));
            }
            else
            {
                var insert = spread ? pieces[i] : whole;
                edits.Add((range.Start, range.End - range.Start, insert, range.Start + insert.Length));
            }
        }
        editor.BeginUndoAction();
        try
        {
            foreach (var edit in edits.AsEnumerable().Reverse())
            {
                editor.SetTargetRange(edit.At, edit.At + edit.Removed);
                editor.ReplaceTarget(edit.Text);
            }
        }
        finally { editor.EndUndoAction(); }
        int shift = 0;
        var carets = new List<int>();
        foreach (var edit in edits)
        {
            carets.Add(edit.Caret + shift);
            shift += edit.Text.Length - edit.Removed;
        }
        editor.ClearSelections();
        editor.SetSelection(carets[0], carets[0]);
        foreach (var caret in carets.Skip(1)) editor.AddSelection(caret, caret);
        editor.ScrollCaret();
    }

    static List<(int Start, int End)> Ranges(Scintilla editor) =>
        editor.Selections.Select(selection => (selection.Start, selection.End)).OrderBy(range => range.Start).ToList();

    static string LineEnding(Scintilla editor) => editor.EolMode == Eol.CrLf ? "\r\n" : editor.EolMode == Eol.Cr ? "\r" : "\n";

    static bool SetClipboard(string text, bool line)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        if (line)
        {
            data.SetData(LineFormat, new MemoryStream(new byte[1]));
            data.SetData(VisualStudioLineFormat, new MemoryStream(new byte[1]));
        }
        try
        {
            Clipboard.SetDataObject(data, true, 5, 50);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
