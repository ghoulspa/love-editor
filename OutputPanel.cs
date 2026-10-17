using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LoveEditor;

sealed class OutputPanel : UserControl
{
    static readonly Regex LocationPattern = new(@"(?<file>[^\s:""'\[\]()<>]+\.lua):(?<line>\d+)", RegexOptions.Compiled);
    static readonly Color InfoColor = Color.FromArgb(110, 110, 110);
    static readonly Color ErrorColor = Color.FromArgb(200, 30, 30);
    static readonly Color LinkColor = Color.FromArgb(0, 102, 204);
    const int MaxLength = 1_000_000;

    readonly Label header;
    readonly RichTextBox box;
    readonly Font linkFont;
    readonly StringBuilder pending = new();
    readonly Timer flushTimer = new() { Interval = 40 };
    readonly Dictionary<int, (string File, int Line)> links = new();
    int removedLines;
    bool inError;

    public event Action<string, int>? LocationClicked;

    public OutputPanel()
    {
        header = new Label { Text = "Output", Dock = DockStyle.Top, AutoSize = false, Height = 22, Padding = new Padding(6, 4, 0, 0), BackColor = SystemColors.Control };
        box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font("Consolas", 9.5f),
            WordWrap = false,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            HideSelection = false,
        };
        linkFont = new Font(box.Font, FontStyle.Underline);
        Controls.Add(box);
        Controls.Add(header);
        flushTimer.Tick += (s, e) => Flush();
        box.MouseMove += (s, e) => box.Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.IBeam;
        box.MouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Left || box.SelectionLength > 0) return;
            var link = LinkAt(e.Location);
            if (link != null) LocationClicked?.Invoke(link.Value.File, link.Value.Line);
        };
    }

    public string? ProjectRoot { get; set; }

    public void Clear()
    {
        lock (pending) pending.Clear();
        box.Clear();
        links.Clear();
        removedLines = 0;
        inError = false;
    }

    public void AppendInfo(string text) => Append(text, InfoColor);

    public void AppendError(string text) => Append(text, ErrorColor);

    public void AppendOutput(string text)
    {
        lock (pending) pending.Append(text);
        if (IsHandleCreated) BeginInvoke(new Action(() => flushTimer.Start()));
    }

    void Flush()
    {
        flushTimer.Stop();
        string text;
        lock (pending)
        {
            text = pending.ToString();
            pending.Clear();
        }
        if (text.Length == 0) return;
        Native.SendMessage(box.Handle, Native.WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        foreach (var line in SplitLines(text))
        {
            string content = line.TrimEnd('\n');
            if (content.StartsWith("Error")) inError = true;
            else if (inError && !(content.StartsWith("stack traceback") || content.StartsWith("\t") || content.StartsWith(" ") || content.Length == 0)) inError = false;
            AppendLine(line, inError ? ErrorColor : box.ForeColor);
        }
        Trim();
        Native.SendMessage(box.Handle, Native.WmSetRedraw, (IntPtr)1, IntPtr.Zero);
        box.Invalidate();
        ScrollToEnd();
    }

    void Append(string text, Color color)
    {
        Flush();
        foreach (var line in SplitLines(text)) AppendLine(line, color);
        ScrollToEnd();
    }

    static IEnumerable<string> SplitLines(string text)
    {
        int start = 0;
        while (start < text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length - 1;
            yield return text.Substring(start, end - start + 1);
            start = end + 1;
        }
    }

    void AppendLine(string line, Color color)
    {
        int lineIndex = box.GetLineFromCharIndex(box.TextLength) + removedLines;
        int position = 0;
        foreach (Match match in LocationPattern.Matches(line))
        {
            string file = match.Groups["file"].Value;
            if (ProjectRoot == null || !File.Exists(Path.Combine(ProjectRoot, file.Replace('/', '\\')))) continue;
            Write(line.Substring(position, match.Index - position), color, box.Font);
            Write(match.Value, LinkColor, linkFont);
            position = match.Index + match.Length;
            if (!links.ContainsKey(lineIndex)) links[lineIndex] = (file, int.Parse(match.Groups["line"].Value));
        }
        Write(line.Substring(position), color, box.Font);
    }

    void Write(string text, Color color, Font font)
    {
        if (text.Length == 0) return;
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = color;
        box.SelectionFont = font;
        box.AppendText(text);
    }

    void Trim()
    {
        if (box.TextLength <= MaxLength) return;
        int cut = box.GetFirstCharIndexFromLine(box.GetLineFromCharIndex(box.TextLength - MaxLength * 4 / 5) + 1);
        if (cut <= 0) return;
        int lines = box.GetLineFromCharIndex(cut);
        box.ReadOnly = false;
        box.Select(0, cut);
        box.SelectedText = "";
        box.ReadOnly = true;
        removedLines += lines;
    }

    void ScrollToEnd()
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.ScrollToCaret();
    }

    (string File, int Line)? LinkAt(Point point)
    {
        if (box.TextLength == 0) return null;
        int line = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(point));
        int top = box.GetPositionFromCharIndex(box.GetFirstCharIndexFromLine(line)).Y;
        if (point.Y < top || point.Y > top + box.Font.Height + 2) return null;
        return links.TryGetValue(line + removedLines, out var link) ? link : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flushTimer.Dispose();
            linkFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
