using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LoveEditor;

interface IDocumentTab : IDisposable
{
    string FilePath { get; set; }
    Control View { get; }
    bool IsDirty { get; }
    string Language { get; }
    string Position { get; }
    event EventHandler? StateChanged;
    void Save();
    void Reload();
    void GoTo(int line, int column);
    void FocusView();
}

interface ITabKind
{
    bool CanOpen(string path);
    IDocumentTab Open(string path);
}

static class TabKinds
{
    public static List<ITabKind> All { get; } = new() { new ImageTabKind(), new CodeTabKind() };

    public static ITabKind? For(string path) => All.FirstOrDefault(kind => kind.CanOpen(path));
}
