using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LoveEditor;

sealed class CompletionDocs : Form
{
    const int WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x00000080;
    const int WidthDip = 380;
    const int MaxHeightDip = 360;
    const int PaddingDip = 8;
    const int ParagraphGapDip = 6;
    const int TagGapDip = 2;
    const int MaxCodeLines = 8;
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

    static readonly Color CodeColor = Color.FromArgb(0, 70, 160);
    static readonly Color TextColor = Color.FromArgb(30, 30, 30);
    static readonly Color BorderColor = Color.FromArgb(160, 160, 160);

    readonly List<(string Text, bool Code, int Gap)> blocks = new();
    Font? codeFont;
    Font? textFont;
    int fontDpi;
    int dpi = 96;

    public CompletionDocs()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.White;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    public static Rectangle? FindList()
    {
        Rectangle? found = null;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var name = new StringBuilder(32);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "ListBoxX") return true;
            GetWindowRect(window, out var rect);
            found = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public void ShowBeside(Control editor, Rectangle list, LuaDocs.Doc doc)
    {
        dpi = editor.DeviceDpi;
        if (fontDpi != dpi)
        {
            codeFont?.Dispose();
            textFont?.Dispose();
            codeFont = new Font("Consolas", 9f * dpi / 72f, GraphicsUnit.Pixel);
            textFont = new Font("Segoe UI", 9f * dpi / 72f, GraphicsUnit.Pixel);
            fontDpi = dpi;
        }
        blocks.Clear();
        var code = doc.Code;
        if (code.Count > MaxCodeLines) code = new List<string> { code[0].TrimEnd().EndsWith("{") ? code[0].TrimEnd() + " … }" : code[0] + " …" };
        if (code.Count > 0) blocks.Add((string.Join("\n", code), true, 0));
        foreach (var paragraph in doc.Paragraphs) blocks.Add((paragraph, false, ParagraphGapDip));
        for (int i = 0; i < doc.Tags.Count; i++) blocks.Add((doc.Tags[i], false, i == 0 ? ParagraphGapDip : TagGapDip));

        int width = Scale(WidthDip);
        int height = Math.Min(Arrange(width).Height, Math.Max(list.Height, Scale(MaxHeightDip)));
        var area = Screen.FromRectangle(list).WorkingArea;
        int x = list.Right - 1;
        if (x + width > area.Right) x = list.Left - width + 1;
        int y = Math.Max(area.Top, Math.Min(list.Top, area.Bottom - height));
        var owner = editor.FindForm();
        if (owner != null && Owner != owner) Owner = owner;
        Bounds = new Rectangle(x, y, width, height);
        Invalidate();
        if (!Visible) Show();
    }

    int Scale(int dip) => (int)Math.Round(dip * dpi / 96.0);

    (int Height, List<(Rectangle Bounds, string Text, bool Code)> Lines) Arrange(int width)
    {
        int padding = Scale(PaddingDip);
        int inner = width - 2 * padding;
        int y = padding;
        var placed = new List<(Rectangle, string, bool)>();
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (i > 0) y += Scale(block.Gap);
            var size = TextRenderer.MeasureText(block.Text, block.Code ? codeFont : textFont, new Size(inner, int.MaxValue), Flags);
            placed.Add((new Rectangle(padding, y, inner, size.Height), block.Text, block.Code));
            y += size.Height;
        }
        return (y + padding, placed);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int bottom = Height - Scale(PaddingDip);
        foreach (var (bounds, text, code) in Arrange(Width).Lines)
        {
            if (bounds.Top >= bottom) break;
            var clipped = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, Math.Min(bounds.Bottom, bottom));
            TextRenderer.DrawText(e.Graphics, text, code ? codeFont : textFont, clipped, code ? CodeColor : TextColor, Flags | TextFormatFlags.EndEllipsis);
        }
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            codeFont?.Dispose();
            textFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    static extern bool EnumThreadWindows(int threadId, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("kernel32.dll")]
    static extern int GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}
