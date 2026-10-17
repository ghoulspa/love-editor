using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScintillaNET;

namespace LoveEditor;

sealed partial class LuaAssist : IDisposable
{
    const int ErrorIndicator = 8;
    const int WarningIndicator = 9;
    const int InfoIndicator = 10;
    const int FadedIndicator = 11;
    const int PlaceholderIndicator = 12;
    const int IndentSize = 2;
    const char ListSeparator = '\n';
    const char ImageSeparator = '\u001f';

    static readonly Color CodeHighlight = Color.FromArgb(0, 70, 160);
    static readonly Color ProblemHighlight = Color.FromArgb(190, 30, 30);
    static readonly Regex LinePattern = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    enum Tip { None, Signature, Hover }

    sealed class Entry
    {
        public object? Item;
        public LuaDocs.Doc? Doc;
        public string Display = "";
        public string Filter = "";
        public string Insert = "";
        public bool IsSnippet;
        public string? Template;
        public int Image;
        public object? Range;
        public List<object> Extra = new();
    }

    sealed class Problem
    {
        public int Start;
        public int End;
        public int Indicator;
        public string Message = "";
    }

    sealed class Placeholder
    {
        public int Number;
        public int Start;
        public int Length;
        public int End => Start + Length;
    }

    readonly Scintilla editor;
    readonly LuaLanguage? language;
    readonly List<Problem> problems = new();
    readonly List<Placeholder> placeholders = new();
    string uri;
    int version;
    bool opened;
    bool changed;
    bool flushPosted;
    int sessionStart = -1;
    bool sessionTriggered;
    int completionSerial;
    string sessionReceiver = "";
    bool sessionDefinesCallback;
    List<Entry>? sessionItems;
    List<Entry> shown = new();
    bool refilterPosted;
    CompletionDocs? docs;
    int docsSerial;
    bool quiet;
    int imagesDpi;
    Tip tip;
    string tipText = "";
    int tipPosition = -1;
    int signatureSerial;
    bool signaturePosted;
    bool resumeSignature;
    int hoverSerial;
    int hoverStart = -1;
    int hoverEnd = -1;
    Placeholder? snippet;
    int placeholderIndex = -1;
    bool placeholdersPosted;

    public LuaAssist(Scintilla editor, string path)
    {
        this.editor = editor;
        FilePath = path;
        uri = LuaLanguage.ToUri(path);
        language = LuaLanguage.Current;
        Configure();
        editor.Insert += OnInsert;
        editor.Delete += OnDelete;
        editor.CharAdded += OnCharAdded;
        editor.AutoCSelection += OnAutoCSelection;
        editor.AutoCCancelled += (s, e) => { if (!quiet) EndSession(false); };
        editor.AutoCSelectionChange += (s, e) => UpdateDocs();
        editor.DwellStart += OnDwellStart;
        editor.DwellEnd += OnDwellEnd;
        editor.UpdateUI += OnUpdateUI;
        editor.LostFocus += (s, e) => CancelTip();
        language?.Register(this);
    }

    public string FilePath { get; private set; }

    public bool CanFormat => opened;

    void Configure()
    {
        editor.MouseDwellTime = 500;
        editor.AutoCSeparator = ListSeparator;
        editor.AutoCTypeSeparator = ImageSeparator;
        editor.AutoCIgnoreCase = true;
        editor.AutoCAutoHide = true;
        editor.AutoCCancelAtStart = false;
        editor.AutoCDropRestOfWord = false;
        editor.AutoCChooseSingle = false;
        editor.AutoCOrder = Order.Custom;
        editor.AutoCMaxHeight = 10;
        editor.CallTipTabSize(4);
        var tipStyle = editor.Styles[Style.CallTip];
        tipStyle.Font = "Segoe UI";
        tipStyle.Size = 9;
        tipStyle.ForeColor = Color.FromArgb(30, 30, 30);
        tipStyle.BackColor = Color.White;
        SetIndicator(ErrorIndicator, IndicatorStyle.SquigglePixmap, Color.FromArgb(228, 20, 0));
        SetIndicator(WarningIndicator, IndicatorStyle.SquigglePixmap, Color.FromArgb(214, 150, 0));
        SetIndicator(InfoIndicator, IndicatorStyle.SquigglePixmap, Color.FromArgb(26, 133, 255));
        SetIndicator(FadedIndicator, IndicatorStyle.TextFore, Color.FromArgb(150, 150, 150));
        var box = editor.Indicators[PlaceholderIndicator];
        box.Style = IndicatorStyle.StraightBox;
        box.ForeColor = Color.FromArgb(0, 120, 215);
        box.Alpha = 25;
        box.OutlineAlpha = 110;
        box.Under = true;
    }

    void SetIndicator(int index, IndicatorStyle style, Color color)
    {
        var indicator = editor.Indicators[index];
        indicator.Style = style;
        indicator.ForeColor = color;
    }

    public void ServerReady()
    {
        opened = true;
        changed = false;
        version = 1;
        language!.Notify("textDocument/didOpen", new { textDocument = new { uri, languageId = "lua", version, text = editor.Text } });
    }

    public void ServerLost()
    {
        opened = false;
        SetProblems(new List<Problem>());
    }

    public void Renamed(string path)
    {
        if (opened) language!.Notify("textDocument/didClose", new { textDocument = new { uri } });
        FilePath = path;
        uri = LuaLanguage.ToUri(path);
        SetProblems(new List<Problem>());
        if (!opened) return;
        changed = false;
        version++;
        language!.Notify("textDocument/didOpen", new { textDocument = new { uri, languageId = "lua", version, text = editor.Text } });
    }

    public void Saved()
    {
        if (!opened) return;
        Flush();
        language!.Notify("textDocument/didSave", new { textDocument = new { uri } });
    }

    void OnInsert(object? sender, ModificationEventArgs e)
    {
        if (snippet != null) ShiftPlaceholders(e.Position, e.Text.Length);
        if (sessionStart >= 0 && (e.Position < sessionStart || (e.Position == sessionStart && editor.CurrentPosition > sessionStart)))
        {
            sessionStart += e.Text.Length;
            HideList();
            PostRefilter();
        }
        Modified();
    }

    void OnDelete(object? sender, ModificationEventArgs e)
    {
        if (snippet != null) ShrinkPlaceholders(e.Position, e.Text.Length);
        if (sessionStart >= 0)
        {
            if (e.Position < sessionStart)
            {
                sessionStart -= Math.Min(e.Text.Length, sessionStart - e.Position);
                HideList();
            }
            PostRefilter();
        }
        Modified();
    }

    void Modified()
    {
        changed = true;
        if (flushPosted || !opened || !editor.IsHandleCreated) return;
        flushPosted = true;
        editor.BeginInvoke(new Action(() =>
        {
            flushPosted = false;
            Flush();
        }));
    }

    void Flush()
    {
        if (!changed || !opened || language == null || editor.IsDisposed) return;
        changed = false;
        version++;
        language.Notify("textDocument/didChange", new { textDocument = new { uri, version }, contentChanges = new[] { new { text = editor.Text } } });
    }

    object Position(int position)
    {
        int line = editor.LineFromPosition(position);
        return new { line, character = position - editor.Lines[line].Position };
    }

    int PositionOf(object? value)
    {
        int line = Json.Int(value, "line");
        if (line < 0) return 0;
        if (line >= editor.Lines.Count) return editor.TextLength;
        var target = editor.Lines[line];
        int length = target.Text.TrimEnd('\r', '\n').Length;
        return target.Position + Math.Max(0, Math.Min(Json.Int(value, "character"), length));
    }

    char CharAt(int position) => position >= 0 && position < editor.TextLength ? (char)editor.GetCharAt(position) : '\0';

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    int WordStart(int position)
    {
        while (position > 0 && IsWordChar(CharAt(position - 1))) position--;
        return position;
    }

    bool InCode(int position)
    {
        if (position < 0 || position >= editor.TextLength) return true;
        editor.Colorize(editor.Lines[editor.LineFromPosition(position)].Position, position + 1);
        int style = editor.GetStyleAt(position);
        return style != Style.Lua.Comment && style != Style.Lua.CommentLine && style != Style.Lua.CommentDoc
            && style != Style.Lua.String && style != Style.Lua.Character && style != Style.Lua.LiteralString && style != Style.Lua.StringEol;
    }

    public bool HandleKey(Keys keys)
    {
        switch (keys)
        {
            case Keys.Control | Keys.Space:
                TriggerCompletion();
                return true;
            case Keys.Control | Keys.Shift | Keys.Space:
                RequestSignature();
                return true;
            case Keys.Shift | Keys.Alt | Keys.F:
                FormatDocument();
                return true;
            case Keys.Enter when editor.AutoCActive:
                var entry = SelectedEntry();
                if (entry != null && sessionStart >= 0 && !entry.IsSnippet && entry.Template == null && editor.CurrentPosition >= sessionStart && entry.Insert == editor.GetTextRange(sessionStart, editor.CurrentPosition - sessionStart))
                {
                    HideList();
                    EndSession(false);
                }
                return false;
            case Keys.Tab when snippet != null && !editor.AutoCActive:
                MovePlaceholder(1);
                return true;
            case Keys.Shift | Keys.Tab when snippet != null && !editor.AutoCActive:
                MovePlaceholder(-1);
                return true;
            case Keys.Escape:
                CancelTip();
                EndSnippet();
                return false;
        }
        return false;
    }

    void OnUpdateUI(object? sender, UpdateUIEventArgs e)
    {
        if (tip != Tip.None && !editor.CallTipActive) tip = Tip.None;
        if ((e.Change & UpdateChange.Selection) == 0) return;
        if (tip == Tip.Signature) PostSignature();
        if (snippet != null)
        {
            int caret = editor.CurrentPosition;
            if (caret < snippet.Start || caret > snippet.End) EndSnippet();
        }
    }

    public void SetDiagnostics(object? parameters)
    {
        if (Json.Get(parameters, "version") is int published && published != version) return;
        if (changed) return;
        var list = new List<Problem>();
        foreach (var item in Json.Items(parameters, "diagnostics"))
        {
            var code = Json.Str(item, "code");
            if (code == "duplicate-set-field" && Json.Items(item, "relatedInformation").All(r => LuaLanguage.IsLibrary(Json.Str(Json.Get(r, "location"), "uri")))) continue;
            int severity = Json.Int(item, "severity", 1);
            bool faded = Json.Items(item, "tags").Any(t => t is int value && value == 1);
            int indicator = severity switch
            {
                1 => ErrorIndicator,
                2 => WarningIndicator,
                3 => InfoIndicator,
                _ => faded ? FadedIndicator : -1,
            };
            if (indicator < 0) continue;
            int start = PositionOf(Json.Get(item, "range", "start"));
            int end = PositionOf(Json.Get(item, "range", "end"));
            if (end <= start)
            {
                if (start < editor.TextLength) end = start + 1;
                else if (start > 0) start--;
            }
            list.Add(new Problem { Start = start, End = end, Indicator = indicator, Message = (Json.Str(item, "message") ?? "").Replace('`', '\'') });
        }
        SetProblems(list);
    }

    void SetProblems(List<Problem> list)
    {
        if (editor.IsDisposed) return;
        problems.Clear();
        problems.AddRange(list);
        foreach (var indicator in new[] { ErrorIndicator, WarningIndicator, InfoIndicator, FadedIndicator })
        {
            editor.IndicatorCurrent = indicator;
            editor.IndicatorClearRange(0, editor.TextLength);
        }
        foreach (var problem in problems)
        {
            editor.IndicatorCurrent = problem.Indicator;
            editor.IndicatorFillRange(problem.Start, problem.End - problem.Start);
        }
    }

    public void Dispose()
    {
        docs?.Dispose();
        if (language == null) return;
        if (opened) language.Notify("textDocument/didClose", new { textDocument = new { uri } });
        opened = false;
        language.Unregister(this);
    }
}
