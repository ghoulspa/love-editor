using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoveEditor;

sealed class EditorTabs : TabControl
{
    readonly Dictionary<TabPage, IDocumentTab> documents = new();
    readonly ContextMenuStrip menu = new();
    TabPage? menuPage;

    public event EventHandler? ActiveDocumentChanged;

    public EditorTabs()
    {
        Padding = new Point(12, 4);
        ShowToolTips = true;
        menu.Items.Add("Close", null, (s, e) => { if (menuPage != null) Close(menuPage); });
        menu.Items.Add("Close Others", null, (s, e) =>
        {
            foreach (var page in TabPages.Cast<TabPage>().Where(p => p != menuPage).ToList())
                if (!Close(page)) return;
        });
        menu.Items.Add("Close All", null, (s, e) => CloseAll());
    }

    public bool DeferFocus { get; set; }

    public IDocumentTab? Active => SelectedTab != null && documents.TryGetValue(SelectedTab, out var document) ? document : null;

    public IEnumerable<IDocumentTab> Documents => documents.Values;

    TabPage? PageOf(IDocumentTab document) => documents.FirstOrDefault(pair => pair.Value == document).Key;

    public IDocumentTab? Find(string path) => documents.Values.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));

    static string Title(IDocumentTab document) => Path.GetFileName(document.FilePath) + (document.IsDirty ? "*" : "");

    public IDocumentTab? Open(string path, bool external)
    {
        var existing = Find(path);
        if (existing != null)
        {
            SelectedTab = PageOf(existing);
            if (!DeferFocus) existing.FocusView();
            return existing;
        }
        var kind = TabKinds.For(path);
        if (kind == null)
        {
            if (external)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            return null;
        }
        IDocumentTab document;
        try { document = kind.Open(path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        var page = new TabPage(Title(document)) { ToolTipText = path, UseVisualStyleBackColor = false, BackColor = SystemColors.Window };
        document.View.Dock = DockStyle.Fill;
        page.Controls.Add(document.View);
        documents[page] = document;
        document.StateChanged += (s, e) =>
        {
            var title = Title(document);
            if (page.Text != title) page.Text = title;
            page.ToolTipText = document.FilePath;
            if (document == Active) ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        };
        TabPages.Add(page);
        SelectedTab = page;
        if (!DeferFocus) document.FocusView();
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        return document;
    }

    public bool Close(TabPage page)
    {
        if (!documents.TryGetValue(page, out var document)) return true;
        if (document.IsDirty)
        {
            var answer = MessageBox.Show(this, $"Save changes to {Path.GetFileName(document.FilePath)}?", "Love Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes && !TrySave(document)) return false;
        }
        Discard(page);
        return true;
    }

    void Discard(TabPage page)
    {
        if (!documents.TryGetValue(page, out var document)) return;
        documents.Remove(page);
        TabPages.Remove(page);
        document.Dispose();
        page.Dispose();
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseActive()
    {
        if (SelectedTab != null) Close(SelectedTab);
    }

    public bool CloseAll()
    {
        foreach (var page in TabPages.Cast<TabPage>().ToList())
            if (!Close(page)) return false;
        return true;
    }

    public bool TrySave(IDocumentTab document)
    {
        try
        {
            document.Save();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save {Path.GetFileName(document.FilePath)}.\n\n{ex.Message}", "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    public bool SaveAll() => documents.Values.Where(d => d.IsDirty).ToList().All(TrySave);

    public void PathRenamed(string oldPath, string newPath)
    {
        foreach (var document in documents.Values)
        {
            if (string.Equals(document.FilePath, oldPath, StringComparison.OrdinalIgnoreCase)) document.FilePath = newPath;
            else if (document.FilePath.StartsWith(oldPath + "\\", StringComparison.OrdinalIgnoreCase)) document.FilePath = newPath + document.FilePath.Substring(oldPath.Length);
            else continue;
            var page = PageOf(document);
            if (page != null)
            {
                page.Text = Title(document);
                page.ToolTipText = document.FilePath;
            }
        }
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PathDeleted(string path)
    {
        foreach (var pair in documents.ToList())
        {
            var file = pair.Value.FilePath;
            if (string.Equals(file, path, StringComparison.OrdinalIgnoreCase) || file.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)) Discard(pair.Key);
        }
    }

    public void FileChangedOnDisk(string path) => Find(path)?.Reload();

    TabPage? PageAt(Point point)
    {
        for (int i = 0; i < TabCount; i++)
            if (GetTabRect(i).Contains(point)) return TabPages[i];
        return null;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var page = PageAt(e.Location);
        if (page == null) return;
        if (e.Button == MouseButtons.Middle) Close(page);
        else if (e.Button == MouseButtons.Right)
        {
            menuPage = page;
            menu.Show(this, e.Location);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        if (!DeferFocus) Active?.FocusView();
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var document in documents.Values) document.Dispose();
            documents.Clear();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
