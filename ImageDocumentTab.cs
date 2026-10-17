using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LoveEditor;

sealed class ImageTabKind : ITabKind
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    public bool CanOpen(string path) => Extensions.Contains(Path.GetExtension(path));

    public IDocumentTab Open(string path) => new ImageDocumentTab(path);
}

sealed class ImageDocumentTab : IDocumentTab
{
    readonly ImageView view = new();

    public event EventHandler? StateChanged;

    public ImageDocumentTab(string path)
    {
        FilePath = path;
        Load();
    }

    public string FilePath { get; set; }

    public Control View => view;

    public bool IsDirty => false;

    public string Language => Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant() + " image";

    public string Position => view.Image == null ? "" : $"{view.Image.Width} × {view.Image.Height}";

    void Load()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(FilePath));
        using var loaded = Image.FromStream(stream);
        var old = view.Image;
        view.Image = new Bitmap(loaded);
        old?.Dispose();
    }

    public void Save()
    {
    }

    public void Reload()
    {
        try { Load(); }
        catch (Exception) { return; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void GoTo(int line, int column)
    {
    }

    public void FocusView() => view.Focus();

    public void Dispose()
    {
        view.Image?.Dispose();
        view.Dispose();
    }
}

sealed class ImageView : Control
{
    Bitmap? image;

    public ImageView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = SystemColors.Window;
    }

    public Bitmap? Image
    {
        get => image;
        set { image = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (image == null) return;
        float fit = Math.Min((Width * 0.8f) / image.Width, (Height * 0.8f) / image.Height);
        float scale = fit >= 1 ? (float)Math.Max(1, Math.Floor(fit)) : fit;
        int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
        var rect = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        int cell = Math.Max(4, LogicalToDeviceUnits(8));
        using (var light = new SolidBrush(Color.FromArgb(238, 238, 238)))
        {
            e.Graphics.SetClip(rect);
            for (int y = rect.Top; y < rect.Bottom; y += cell)
                for (int x = rect.Left + ((y - rect.Top) / cell % 2) * cell; x < rect.Right; x += cell * 2)
                    e.Graphics.FillRectangle(light, x, y, cell, cell);
            e.Graphics.ResetClip();
        }
        e.Graphics.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(image, rect);
    }
}
