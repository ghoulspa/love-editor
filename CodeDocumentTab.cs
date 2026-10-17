using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScintillaNET;

namespace LoveEditor;

sealed class CodeTabKind : ITabKind
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".lua", ".txt", ".md", ".json", ".glsl", ".frag", ".vert", ".csv", ".xml", ".ini", ".cfg", ".toml", "" };

    public bool CanOpen(string path) => Extensions.Contains(Path.GetExtension(path));

    public IDocumentTab Open(string path)
    {
        ScintillaWarmup.Wait();
        return new CodeDocumentTab(path);
    }
}

sealed class CodeDocumentTab : IDocumentTab
{
    const int IndentSize = 2;
    const string Keywords = "and break do else elseif end false for function goto if in local nil not or repeat return then true until while";
    const string Builtins = "assert collectgarbage dofile error getmetatable ipairs load loadfile loadstring next pairs pcall print rawequal rawget rawlen rawset require select setmetatable tonumber tostring type unpack xpcall self";
    static readonly HashSet<string> Closers = new() { "end", "else", "elseif", "until", "}", ")" };
    static readonly Regex OpensBlock = new(@"(\bthen|\bdo|\belse|\brepeat|\bfunction\b[^)]*\)|[{(])\s*(--.*)?$", RegexOptions.Compiled);
    static readonly Color KeywordColor = Color.FromArgb(0, 0, 255);
    static readonly Color BuiltinColor = Color.FromArgb(0, 112, 160);
    static readonly Color CommentColor = Color.FromArgb(0, 128, 0);
    static readonly Color StringColor = Color.FromArgb(163, 21, 21);
    static readonly Color NumberColor = Color.FromArgb(9, 134, 88);

    readonly CodeEditor editor = new();
    readonly ContextMenu menu = new();
    readonly bool isLua;
    readonly LuaAssist? assist;
    string filePath;
    int dedentedLine = -1;
    int dedentedBy;

    public event EventHandler? StateChanged;

    public CodeDocumentTab(string path)
    {
        filePath = path;
        isLua = string.Equals(Path.GetExtension(path), ".lua", StringComparison.OrdinalIgnoreCase);
        Configure();
        Load();
        editor.SavePointLeft += (s, e) => Changed();
        editor.SavePointReached += (s, e) => Changed();
        editor.UpdateUI += (s, e) =>
        {
            HighlightBraces();
            Changed();
        };
        editor.CharAdded += (s, e) => OnCharAdded(e.Char);
        editor.KeyDown += OnKeyDown;
        editor.TextChanged += (s, e) => UpdateMarginWidth();
        if (isLua) assist = new LuaAssist(editor, path);
        BuildMenu();
        editor.ContextMenuRequested += point => menu.Show(editor, point);
    }

    public string FilePath
    {
        get => filePath;
        set
        {
            if (filePath == value) return;
            filePath = value;
            assist?.Renamed(value);
        }
    }

    public Control View => editor;

    public bool IsDirty => editor.Modified;

    public string Language => isLua ? "Lua" : "Text";

    public string Position
    {
        get
        {
            if (editor.Selections.Count > 1) return $"{editor.Selections.Count} selections";
            int position = editor.CurrentPosition;
            return $"Ln {editor.LineFromPosition(position) + 1}, Col {editor.GetColumn(position) + 1}";
        }
    }

    void Changed() => StateChanged?.Invoke(this, EventArgs.Empty);

    void Configure()
    {
        editor.BorderStyle = ScintillaNET.BorderStyle.None;
        editor.StyleResetDefault();
        editor.Styles[Style.Default].Font = "Consolas";
        editor.Styles[Style.Default].Size = 10;
        editor.Styles[Style.Default].ForeColor = SystemColors.WindowText;
        editor.Styles[Style.Default].BackColor = SystemColors.Window;
        editor.StyleClearAll();

        editor.Styles[Style.LineNumber].ForeColor = Color.FromArgb(43, 145, 175);
        editor.Styles[Style.LineNumber].BackColor = SystemColors.Window;
        editor.Styles[Style.IndentGuide].ForeColor = Color.FromArgb(220, 220, 220);
        editor.Styles[Style.BraceLight].BackColor = Color.FromArgb(219, 224, 204);
        editor.Styles[Style.BraceBad].ForeColor = Color.FromArgb(200, 30, 30);
        editor.Margins[0].Type = MarginType.Number;
        editor.Margins[1].Width = 6;
        editor.Margins[1].Mask = 0;
        editor.CaretLineBackColor = Color.FromArgb(246, 246, 246);
        editor.SelectionBackColor = Color.FromArgb(173, 214, 255);
        editor.IndentationGuides = IndentView.LookBoth;

        editor.UseTabs = false;
        editor.TabWidth = 4;
        editor.IndentWidth = IndentSize;
        editor.TabIndents = true;
        editor.BackspaceUnindents = true;
        editor.ScrollWidth = 1;
        editor.ScrollWidthTracking = true;

        editor.MultipleSelection = true;
        editor.AdditionalSelectionTyping = true;
        editor.MultiPaste = MultiPaste.Each;
        editor.MouseSelectionRectangularSwitch = true;
        editor.VirtualSpaceOptions = VirtualSpace.RectangularSelection;
        editor.SearchFlags = SearchFlags.MatchCase;
        editor.ClearCmdKey(Keys.Control | Keys.D);
        editor.ClearCmdKey(Keys.Control | Keys.Shift | Keys.L);
        editor.UsePopup(PopupMode.Never);

        if (!isLua) return;
        editor.LexerName = "lua";
        editor.SetKeywords(0, Keywords);
        editor.SetKeywords(1, Builtins);
        editor.Styles[Style.Lua.Comment].ForeColor = CommentColor;
        editor.Styles[Style.Lua.CommentLine].ForeColor = CommentColor;
        editor.Styles[Style.Lua.CommentDoc].ForeColor = CommentColor;
        editor.Styles[Style.Lua.Number].ForeColor = NumberColor;
        editor.Styles[Style.Lua.Word].ForeColor = KeywordColor;
        editor.Styles[Style.Lua.Word2].ForeColor = BuiltinColor;
        editor.Styles[Style.Lua.String].ForeColor = StringColor;
        editor.Styles[Style.Lua.Character].ForeColor = StringColor;
        editor.Styles[Style.Lua.LiteralString].ForeColor = StringColor;
        editor.Styles[Style.Lua.StringEol].ForeColor = StringColor;
    }

    void UpdateMarginWidth()
    {
        int digits = Math.Max(3, editor.Lines.Count.ToString().Length);
        int width = editor.TextWidth(Style.LineNumber, new string('9', digits)) + editor.TextWidth(Style.LineNumber, "9");
        if (editor.Margins[0].Width != width) editor.Margins[0].Width = width;
    }

    void Load()
    {
        var text = File.ReadAllText(FilePath);
        editor.EolMode = text.Contains("\r\n") ? Eol.CrLf : Eol.Lf;
        editor.Text = text;
        editor.EmptyUndoBuffer();
        editor.SetSavePoint();
        editor.GotoPosition(0);
        UpdateMarginWidth();
    }

    public void Save()
    {
        File.WriteAllText(FilePath, editor.Text, new UTF8Encoding(false));
        editor.SetSavePoint();
        assist?.Saved();
        Changed();
    }

    public void Reload()
    {
        if (editor.Modified) return;
        string disk;
        try { disk = File.ReadAllText(FilePath); }
        catch (IOException) { return; }
        if (disk == editor.Text) return;
        int caret = editor.CurrentPosition;
        int first = editor.FirstVisibleLine;
        editor.Text = disk;
        editor.EmptyUndoBuffer();
        editor.SetSavePoint();
        editor.GotoPosition(Math.Min(caret, editor.TextLength));
        editor.FirstVisibleLine = first;
        Changed();
    }

    public void GoTo(int line, int column)
    {
        int index = Math.Max(0, Math.Min(line - 1, editor.Lines.Count - 1));
        editor.GotoPosition(editor.Lines[index].Position + Math.Max(0, column - 1));
        editor.FirstVisibleLine = Math.Max(0, index - editor.LinesOnScreen / 2);
        editor.Focus();
        Changed();
    }

    public void FocusView() => editor.Focus();

    void BuildMenu()
    {
        var undo = new MenuItem("Undo\tCtrl+Z", (s, e) => editor.Undo());
        var redo = new MenuItem("Redo\tCtrl+Y", (s, e) => editor.Redo());
        var cut = new MenuItem("Cut\tCtrl+X", (s, e) => EditorClipboard.Copy(editor, true));
        var copy = new MenuItem("Copy\tCtrl+C", (s, e) => EditorClipboard.Copy(editor, false));
        var paste = new MenuItem("Paste\tCtrl+V", (s, e) => EditorClipboard.Paste(editor));
        var delete = new MenuItem("Delete\tDel", (s, e) => editor.Clear());
        var selectAll = new MenuItem("Select All\tCtrl+A", (s, e) => editor.SelectAll());
        menu.MenuItems.AddRange(new[] { undo, redo, new MenuItem("-"), cut, copy, paste, delete, new MenuItem("-"), selectAll });
        MenuItem? formatDocument = null;
        MenuItem? formatSelection = null;
        if (assist != null)
        {
            formatDocument = new MenuItem("Format Document\tShift+Alt+F", (s, e) => assist.FormatDocument());
            formatSelection = new MenuItem("Format Selection", (s, e) => assist.FormatSelection());
            menu.MenuItems.AddRange(new[] { new MenuItem("-"), formatDocument, formatSelection });
        }
        menu.Popup += (s, e) =>
        {
            bool selected = editor.SelectionStart != editor.SelectionEnd;
            undo.Enabled = editor.CanUndo;
            redo.Enabled = editor.CanRedo;
            paste.Enabled = editor.CanPaste;
            delete.Enabled = selected;
            if (formatDocument != null) formatDocument.Enabled = assist!.CanFormat;
            if (formatSelection != null) formatSelection.Enabled = assist!.CanFormat && selected;
        };
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (assist != null && assist.HandleKey(e.KeyData))
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }
        if (e.KeyData == (Keys.Control | Keys.C) || e.KeyData == (Keys.Control | Keys.Insert)) EditorClipboard.Copy(editor, false);
        else if (e.KeyData == (Keys.Control | Keys.X) || e.KeyData == (Keys.Shift | Keys.Delete)) EditorClipboard.Copy(editor, true);
        else if (e.KeyData == (Keys.Control | Keys.V) || e.KeyData == (Keys.Shift | Keys.Insert)) EditorClipboard.Paste(editor);
        else if (e.KeyData == (Keys.Control | Keys.D))
        {
            editor.TargetWholeDocument();
            editor.MultipleSelectAddNext();
            editor.ScrollCaret();
        }
        else if (e.KeyData == (Keys.Control | Keys.Shift | Keys.L))
        {
            editor.TargetWholeDocument();
            editor.MultipleSelectAddEach();
        }
        else if (e.KeyData == Keys.Escape && editor.Selections.Count > 1)
        {
            int caret = editor.CurrentPosition;
            editor.SetSelection(caret, caret);
        }
        else return;
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    void OnCharAdded(int character)
    {
        if (!isLua || editor.Selections.Count > 1) return;
        int lineIndex = editor.LineFromPosition(editor.CurrentPosition);
        if (character == '\n' || character == '\r')
        {
            if (lineIndex == 0) return;
            var previous = PreviousCodeLine(lineIndex);
            int indent = previous == null ? 0 : previous.Indentation + (OpensBlock.IsMatch(previous.Text.TrimEnd()) ? IndentSize : 0);
            var line = editor.Lines[lineIndex];
            if (line.Text.Trim().Length == 0)
            {
                line.Indentation = indent;
                editor.GotoPosition(line.Position + indent);
            }
            dedentedLine = -1;
            return;
        }
        var current = editor.Lines[lineIndex];
        string word = current.Text.Trim();
        if (dedentedLine == lineIndex && !Closers.Contains(word))
        {
            current.Indentation += dedentedBy;
            dedentedLine = -1;
            return;
        }
        if (dedentedLine == lineIndex || !Closers.Contains(word)) return;
        var opener = PreviousCodeLine(lineIndex);
        if (opener == null) return;
        int target = OpensBlock.IsMatch(opener.Text.TrimEnd()) ? opener.Indentation : Math.Max(0, opener.Indentation - IndentSize);
        if (current.Indentation <= target) return;
        dedentedBy = current.Indentation - target;
        dedentedLine = lineIndex;
        current.Indentation = target;
    }

    Line? PreviousCodeLine(int lineIndex)
    {
        for (int i = lineIndex - 1; i >= 0; i--)
            if (editor.Lines[i].Text.Trim().Length > 0) return editor.Lines[i];
        return null;
    }

    void HighlightBraces()
    {
        int caret = editor.CurrentPosition;
        int brace = -1;
        if (caret > 0 && IsBrace(editor.GetCharAt(caret - 1))) brace = caret - 1;
        else if (IsBrace(editor.GetCharAt(caret))) brace = caret;
        if (brace < 0)
        {
            editor.BraceHighlight(Scintilla.InvalidPosition, Scintilla.InvalidPosition);
            return;
        }
        int match = editor.BraceMatch(brace);
        if (match == Scintilla.InvalidPosition) editor.BraceBadLight(brace);
        else editor.BraceHighlight(brace, match);
    }

    static bool IsBrace(int c) => c == '(' || c == ')' || c == '[' || c == ']' || c == '{' || c == '}';

    public void Dispose()
    {
        assist?.Dispose();
        menu.Dispose();
        editor.Dispose();
    }
}

sealed class CodeEditor : Scintilla
{
    const int WmContextMenu = 0x007B;

    public event Action<Point>? ContextMenuRequested;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmContextMenu && ContextMenuRequested != null)
        {
            long value = m.LParam.ToInt64();
            Point point;
            if ((value & 0xFFFFFFFF) == 0xFFFFFFFF)
            {
                int caret = CurrentPosition;
                point = new Point(PointXFromPosition(caret), PointYFromPosition(caret) + Lines[LineFromPosition(caret)].Height);
            }
            else point = PointToClient(new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF)));
            ContextMenuRequested(point);
            return;
        }
        base.WndProc(ref m);
    }
}
