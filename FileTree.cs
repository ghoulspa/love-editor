using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace LoveEditor;

sealed class FileTree : TreeView
{
    sealed class Entry
    {
        public Entry(string path, bool directory)
        {
            Path = path;
            Directory = directory;
        }

        public string Path { get; set; }
        public bool Directory { get; }
    }

    readonly ImageList icons = new() { ColorDepth = ColorDepth.Depth32Bit };
    readonly Dictionary<string, int> iconIndex = new(StringComparer.OrdinalIgnoreCase);
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem renameItem;
    readonly ToolStripMenuItem deleteItem;
    readonly HashSet<string> pendingDirectories = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer refreshTimer = new() { Interval = 150 };
    readonly float scale;
    FileSystemWatcher? watcher;
    string? openAfterRename;

    public event Action<string, bool>? OpenRequested;
    public event Action<string>? FileChangedOnDisk;
    public event Action<string, string>? PathRenamed;
    public event Action<string>? PathDeleted;

    public FileTree()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        int iconSize = (int)Math.Round(16 * scale);
        icons.ImageSize = new Size(iconSize, iconSize);
        ImageList = icons;
        ItemHeight = (int)Math.Round(22 * scale);
        BorderStyle = BorderStyle.None;
        FullRowSelect = true;
        ShowLines = false;
        HideSelection = false;
        LabelEdit = true;
        AllowDrop = true;

        menu.Items.Add("New File", null, (s, e) => NewFile());
        menu.Items.Add("New Folder", null, (s, e) => NewFolder());
        menu.Items.Add(new ToolStripSeparator());
        renameItem = new ToolStripMenuItem("Rename", null, (s, e) => Rename()) { ShortcutKeyDisplayString = "F2" };
        deleteItem = new ToolStripMenuItem("Delete", null, (s, e) => DeleteSelected()) { ShortcutKeyDisplayString = "Del" };
        menu.Items.Add(renameItem);
        menu.Items.Add(deleteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show in Explorer", null, (s, e) => Reveal());
        menu.Opening += (s, e) =>
        {
            bool canChange = SelectedNode != null && SelectedNode.Parent != null;
            renameItem.Enabled = canChange;
            deleteItem.Enabled = canChange;
            e.Cancel = RootPath == null;
        };
        ContextMenuStrip = menu;
        refreshTimer.Tick += (s, e) => FlushRefresh();
    }

    public string? RootPath { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowTheme(Handle, "Explorer", null);
    }

    public void Open(string root)
    {
        CloseFolder();
        RootPath = root;
        BeginUpdate();
        var node = CreateNode(root, true);
        node.Text = System.IO.Path.GetFileName(root);
        Nodes.Add(node);
        Populate(node);
        node.Expand();
        EndUpdate();
        watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
            SynchronizingObject = this,
        };
        watcher.Created += (s, e) => Queue(System.IO.Path.GetDirectoryName(e.FullPath));
        watcher.Deleted += (s, e) => Queue(System.IO.Path.GetDirectoryName(e.FullPath));
        watcher.Renamed += (s, e) =>
        {
            Queue(System.IO.Path.GetDirectoryName(e.OldFullPath));
            Queue(System.IO.Path.GetDirectoryName(e.FullPath));
        };
        watcher.Changed += (s, e) => FileChangedOnDisk?.Invoke(e.FullPath);
        watcher.Error += (s, e) => Queue(RootPath);
        watcher.EnableRaisingEvents = true;
    }

    public void CloseFolder()
    {
        watcher?.Dispose();
        watcher = null;
        refreshTimer.Stop();
        pendingDirectories.Clear();
        Nodes.Clear();
        RootPath = null;
    }

    int IconFor(string path, bool directory)
    {
        string key = directory ? "<folder>" : System.IO.Path.GetExtension(path);
        if (iconIndex.TryGetValue(key, out int index)) return index;
        var bitmap = Native.ShellIcon(directory ? "folder" : "file" + key, directory, scale > 1.25f) ?? new Bitmap(16, 16);
        icons.Images.Add(bitmap);
        index = icons.Images.Count - 1;
        iconIndex[key] = index;
        return index;
    }

    TreeNode CreateNode(string path, bool directory)
    {
        int image = IconFor(path, directory);
        var node = new TreeNode(System.IO.Path.GetFileName(path)) { Tag = new Entry(path, directory), Name = path, ImageIndex = image, SelectedImageIndex = image };
        if (directory && List(path).Count > 0) node.Nodes.Add(new TreeNode());
        return node;
    }

    static Entry? EntryOf(TreeNode? node) => node?.Tag as Entry;

    static bool IsPlaceholder(TreeNode node) => node.Nodes.Count == 1 && node.Nodes[0].Tag == null;

    static bool IsShown(string path)
    {
        if (string.Equals(System.IO.Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase)) return false;
        try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == 0; }
        catch (Exception) { return false; }
    }

    static List<(string Path, bool Directory)> List(string directory)
    {
        var result = new List<(string, bool)>();
        try
        {
            result.AddRange(Directory.EnumerateDirectories(directory).Where(IsShown).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(p => (p, true)));
            result.AddRange(Directory.EnumerateFiles(directory).Where(IsShown).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(p => (p, false)));
        }
        catch (Exception)
        {
        }
        return result;
    }

    void Populate(TreeNode node)
    {
        var entry = EntryOf(node);
        if (entry == null) return;
        node.Nodes.Clear();
        foreach (var (path, directory) in List(entry.Path)) node.Nodes.Add(CreateNode(path, directory));
    }

    protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
    {
        if (e.Node != null && IsPlaceholder(e.Node)) Populate(e.Node);
        base.OnBeforeExpand(e);
    }

    void Queue(string? directory)
    {
        if (directory == null) return;
        pendingDirectories.Add(directory);
        refreshTimer.Stop();
        refreshTimer.Start();
    }

    void FlushRefresh()
    {
        refreshTimer.Stop();
        var directories = pendingDirectories.ToList();
        pendingDirectories.Clear();
        BeginUpdate();
        foreach (var directory in directories)
        {
            var node = FindNode(directory);
            if (node != null) Sync(node);
        }
        EndUpdate();
    }

    public TreeNode? FindNode(string path)
    {
        if (Nodes.Count == 0) return null;
        if (string.Equals(EntryOf(Nodes[0])?.Path, path, StringComparison.OrdinalIgnoreCase)) return Nodes[0];
        return Nodes[0].Nodes.Find(path, true).FirstOrDefault(n => n.Tag != null);
    }

    void Sync(TreeNode node)
    {
        var entry = EntryOf(node);
        if (entry == null || !Directory.Exists(entry.Path)) return;
        var wanted = List(entry.Path);
        if (IsPlaceholder(node) || (node.Nodes.Count == 0 && !node.IsExpanded))
        {
            node.Nodes.Clear();
            if (wanted.Count > 0) node.Nodes.Add(new TreeNode());
            return;
        }
        var keep = new HashSet<string>(wanted.Select(w => w.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.Nodes.Cast<TreeNode>().ToList())
            if (EntryOf(child) is not Entry e || !keep.Contains(e.Path)) child.Remove();
        for (int i = 0; i < wanted.Count; i++)
        {
            var (path, directory) = wanted[i];
            var existing = node.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(EntryOf(n)?.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing == null) node.Nodes.Insert(i, CreateNode(path, directory));
            else if (existing.Index != i)
            {
                existing.Remove();
                node.Nodes.Insert(i, existing);
            }
            else if (EntryOf(existing)!.Directory && !existing.IsExpanded && existing.Nodes.Count == 0 && List(path).Count > 0) existing.Nodes.Add(new TreeNode());
        }
    }

    string TargetDirectory(TreeNode? node)
    {
        var entry = EntryOf(node);
        if (entry == null) return RootPath!;
        return entry.Directory ? entry.Path : System.IO.Path.GetDirectoryName(entry.Path)!;
    }

    static string Unique(string directory, string name, string extension)
    {
        string path = System.IO.Path.Combine(directory, name + extension);
        for (int i = 2; File.Exists(path) || Directory.Exists(path); i++) path = System.IO.Path.Combine(directory, $"{name} ({i}){extension}");
        return path;
    }

    public void NewFile() => CreateAndRename(false);

    public void NewFolder() => CreateAndRename(true);

    void CreateAndRename(bool directory)
    {
        if (RootPath == null) return;
        string parent = TargetDirectory(SelectedNode);
        string path;
        try
        {
            if (directory) Directory.CreateDirectory(path = Unique(parent, "new folder", ""));
            else File.WriteAllText(path = Unique(parent, "new", ".lua"), "");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var parentNode = FindNode(parent);
        if (parentNode == null) return;
        if (IsPlaceholder(parentNode) || parentNode.Nodes.Count == 0) Populate(parentNode);
        else Sync(parentNode);
        parentNode.Expand();
        var node = FindNode(path);
        if (node == null) return;
        SelectedNode = node;
        if (!directory) openAfterRename = path;
        node.BeginEdit();
    }

    public void Rename()
    {
        if (SelectedNode != null && SelectedNode.Parent != null) SelectedNode.BeginEdit();
    }

    protected override void OnBeforeLabelEdit(NodeLabelEditEventArgs e)
    {
        if (e.Node?.Parent == null) e.CancelEdit = true;
        base.OnBeforeLabelEdit(e);
    }

    protected override void OnAfterLabelEdit(NodeLabelEditEventArgs e)
    {
        base.OnAfterLabelEdit(e);
        var entry = EntryOf(e.Node);
        if (entry == null) return;
        string? open = openAfterRename;
        openAfterRename = null;
        string name = e.Label?.Trim() ?? "";
        if (e.Label == null || name.Length == 0 || name == e.Node!.Text)
        {
            e.CancelEdit = e.Label != null;
            if (open != null) OpenRequested?.Invoke(open, true);
            return;
        }
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            e.CancelEdit = true;
            MessageBox.Show(this, "A name can't contain any of these characters: \\ / : * ? \" < > |", "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string oldPath = entry.Path;
        string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(oldPath)!, name);
        bool caseOnly = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(newPath) || Directory.Exists(newPath)))
        {
            e.CancelEdit = true;
            MessageBox.Show(this, $"There's already something called \"{name}\" here.", "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            if (entry.Directory) Directory.Move(oldPath, newPath);
            else File.Move(oldPath, newPath);
        }
        catch (Exception ex)
        {
            e.CancelEdit = true;
            MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Repath(e.Node!, oldPath, newPath);
        PathRenamed?.Invoke(oldPath, newPath);
        Queue(System.IO.Path.GetDirectoryName(newPath));
        if (open != null) OpenRequested?.Invoke(newPath, true);
    }

    static void Repath(TreeNode node, string oldPrefix, string newPrefix)
    {
        if (EntryOf(node) is Entry entry)
        {
            entry.Path = newPrefix + entry.Path.Substring(oldPrefix.Length);
            node.Name = entry.Path;
        }
        foreach (TreeNode child in node.Nodes) Repath(child, oldPrefix, newPrefix);
    }

    public void DeleteSelected()
    {
        var node = SelectedNode;
        var entry = EntryOf(node);
        if (entry == null || node!.Parent == null) return;
        string name = System.IO.Path.GetFileName(entry.Path);
        if (MessageBox.Show(this, $"Move \"{name}\" to the Recycle Bin?", "Love Editor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            if (entry.Directory) FileSystem.DeleteDirectory(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else FileSystem.DeleteFile(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        node.Remove();
        PathDeleted?.Invoke(entry.Path);
    }

    void Reveal()
    {
        var entry = EntryOf(SelectedNode);
        string path = entry?.Path ?? RootPath ?? "";
        if (path.Length > 0) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        if (e.Button == MouseButtons.Right) SelectedNode = e.Node;
        else if (e.Button == MouseButtons.Left && EntryOf(e.Node) is Entry entry && !entry.Directory && e.Node.Bounds.Contains(e.Location)) OpenRequested?.Invoke(entry.Path, false);
    }

    protected override void OnNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseDoubleClick(e);
        if (EntryOf(e.Node) is Entry entry && !entry.Directory) OpenRequested?.Invoke(entry.Path, true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F2) Rename();
        else if (e.KeyCode == Keys.Delete) DeleteSelected();
        else if (e.KeyCode == Keys.Enter && EntryOf(SelectedNode) is Entry entry && !entry.Directory) OpenRequested?.Invoke(entry.Path, true);
        else return;
        e.Handled = true;
    }

    void HighlightDrop(TreeNode? node) => Native.SendMessage(Handle, Native.TvmSelectItem, (IntPtr)Native.TvgnDropHilite, node?.Handle ?? IntPtr.Zero);

    TreeNode? DropFolderNode(DragEventArgs e)
    {
        var node = GetNodeAt(PointToClient(new Point(e.X, e.Y)));
        if (node == null) return Nodes.Count > 0 ? Nodes[0] : null;
        return EntryOf(node)?.Directory == true ? node : node.Parent;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        OnDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (RootPath == null || e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        e.Effect = DragDropEffects.Copy;
        HighlightDrop(DropFolderNode(e));
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        HighlightDrop(null);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        HighlightDrop(null);
        if (RootPath == null || e.Data?.GetData(DataFormats.FileDrop) is not string[] sources) return;
        var target = DropFolderNode(e);
        string directory = TargetDirectory(target);
        foreach (var source in sources)
        {
            string full = System.IO.Path.GetFullPath(source).TrimEnd('\\');
            if (directory.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
            string name = System.IO.Path.GetFileNameWithoutExtension(full);
            bool isDirectory = Directory.Exists(full);
            string destination = Unique(directory, isDirectory ? System.IO.Path.GetFileName(full) : name, isDirectory ? "" : System.IO.Path.GetExtension(full));
            try
            {
                if (isDirectory) FileSystem.CopyDirectory(full, destination, UIOption.OnlyErrorDialogs);
                else File.Copy(full, destination);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        if (target != null)
        {
            if (IsPlaceholder(target) || target.Nodes.Count == 0) Populate(target);
            else Sync(target);
            target.Expand();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watcher?.Dispose();
            refreshTimer.Dispose();
            menu.Dispose();
            icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
