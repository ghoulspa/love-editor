using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ScintillaNET;

namespace LoveEditor;

static class CompletionIcons
{
    static readonly (int Image, Color Color, string Letter)[] Kinds =
    {
        (1, Color.FromArgb(126, 72, 204), "f"),
        (2, Color.FromArgb(0, 128, 128), "p"),
        (3, Color.FromArgb(0, 110, 200), "v"),
        (4, Color.FromArgb(200, 115, 0), "m"),
        (5, Color.FromArgb(110, 110, 110), "k"),
        (6, Color.FromArgb(60, 140, 60), "s"),
        (7, Color.FromArgb(150, 150, 150), "t"),
    };

    public const int Module = 4;

    public static int For(int kind) => kind switch
    {
        2 or 3 or 4 => 1,
        5 or 10 or 13 or 20 => 2,
        6 or 21 => 3,
        7 or 8 or 9 or 22 => Module,
        14 => 5,
        15 => 6,
        _ => 7,
    };

    public static void Register(Scintilla editor, int dpi)
    {
        int size = Math.Max(12, (int)Math.Round(14 * dpi / 96.0));
        foreach (var kind in Kinds)
        {
            using var image = Draw(size, kind.Color, kind.Letter);
            editor.RegisterRgbaImage(kind.Image, image);
        }
    }

    static Bitmap Draw(int size, Color color, string letter)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float inset = size * 0.06f;
        var rect = new RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
        float diameter = size * 0.44f;
        using (var path = new GraphicsPath())
        {
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(color);
            graphics.FillPath(brush, path);
        }
        using var font = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(letter, font, Brushes.White, new RectangleF(0, -size * 0.04f, size, size), format);
        return bitmap;
    }
}
