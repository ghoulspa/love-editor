using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LoveEditor;

static class Native
{
    public const int WmSetRedraw = 0x000B;
    public const int TvmSelectItem = 0x110B;
    public const int TvgnDropHilite = 0x0008;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    public static Bitmap? ShellIcon(string name, bool directory, bool large)
    {
        var info = new ShFileInfo();
        uint flags = 0x100 | 0x10 | (large ? 0x0u : 0x1u);
        SHGetFileInfo(name, directory ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        if (info.hIcon == IntPtr.Zero) return null;
        using var icon = Icon.FromHandle(info.hIcon);
        var bitmap = icon.ToBitmap();
        DestroyIcon(info.hIcon);
        return bitmap;
    }
}
