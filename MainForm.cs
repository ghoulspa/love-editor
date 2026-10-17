using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoveEditor;

sealed class MainForm : Form
{
    const int DefaultTreeWidth = 230;
    const int DefaultOutputHeight = 170;

    readonly AppSettings settings;
    readonly StartOptions options;
    readonly ToolStripButton playButton;
    readonly ToolStripButton newFileButton;
    readonly ToolStripButton newFolderButton;
    readonly ToolStripStatusLabel projectLabel;
    readonly ToolStripStatusLabel positionLabel;
    readonly ToolStripStatusLabel languageLabel;
    readonly LiveSplitContainer outer;
    readonly LiveSplitContainer inner;
    readonly FileTree tree = new() { Dock = DockStyle.Fill };
    readonly EditorTabs tabs = new() { Dock = DockStyle.Fill, Visible = false };
    readonly StartPage startPage = new() { Dock = DockStyle.Fill };
    readonly OutputPanel output = new() { Dock = DockStyle.Fill };
    readonly LoveRunner runner = new();
    readonly Font playFont;
    string? projectRoot;
    bool picking;
    int treeWidth;
    int outputHeight;

    public MainForm(AppSettings settings, StartOptions options)
    {
        this.settings = settings;
        this.options = options;
        treeWidth = settings.TreeWidth > 0 ? settings.TreeWidth : DefaultTreeWidth;
        outputHeight = settings.OutputHeight > 0 ? settings.OutputHeight : DefaultOutputHeight;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        Text = "Love Editor";
        ClientSize = options.ClientSize ?? new Size(1100, 680);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 400);
        if (options.ShotPath != null)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            ShowInTaskbar = false;
        }

        playFont = new Font(Font, FontStyle.Bold);
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        playButton = new ToolStripButton { Font = playFont, ToolTipText = "Run the game (F5)" };
        newFileButton = new ToolStripButton("New File", null, (s, e) => tree.NewFile()) { ToolTipText = "New file (Ctrl+N)" };
        newFolderButton = new ToolStripButton("New Folder", null, (s, e) => tree.NewFolder());
        var openButton = new ToolStripButton("Open Project", null, (s, e) => ChooseFolder()) { ToolTipText = "Open a project folder (Ctrl+O)" };
        var newProjectButton = new ToolStripButton("New Project", null, (s, e) => NewProject());
        playButton.Click += (s, e) => { if (runner.IsRunning) StopGame(); else Play(); };
        tools.Items.AddRange(new ToolStripItem[] { playButton, new ToolStripSeparator(), newFileButton, newFolderButton, new ToolStripSeparator(), newProjectButton, openButton });

        var status = new StatusStrip();
        projectLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        positionLabel = new ToolStripStatusLabel();
        languageLabel = new ToolStripStatusLabel();
        status.Items.AddRange(new ToolStripItem[] { projectLabel, positionLabel, languageLabel });

        outer = new LiveSplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
        outer.Panel1.BackColor = SystemColors.Window;
        outer.Panel1.Controls.Add(tree);
        inner = new LiveSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5 };
        inner.Panel1.Controls.Add(tabs);
        inner.Panel1.Controls.Add(startPage);
        inner.Panel2.Controls.Add(output);
        outer.Panel2.Controls.Add(inner);

        Controls.Add(outer);
        Controls.Add(status);
        Controls.Add(tools);
        ResumeLayout(false);
        PerformLayout();

        outer.SplitterCommitted += (s, e) => treeWidth = ToLogical(outer.SplitterDistance);
        inner.SplitterCommitted += (s, e) => outputHeight = ToLogical(inner.Panel2Size);
        outer.ResetRequested += (s, e) => { treeWidth = DefaultTreeWidth; ApplySplitters(); };
        inner.ResetRequested += (s, e) => { outputHeight = DefaultOutputHeight; ApplySplitters(); };

        tree.OpenRequested += (path, external) => { tabs.Open(path, external); };
        tree.FileChangedOnDisk += path => tabs.FileChangedOnDisk(path);
        tree.PathRenamed += (oldPath, newPath) => tabs.PathRenamed(oldPath, newPath);
        tree.PathDeleted += path => tabs.PathDeleted(path);
        tabs.ActiveDocumentChanged += (s, e) => UpdateStatus();
        startPage.NewProjectClicked += (s, e) => NewProject();
        startPage.OpenFolderClicked += (s, e) => ChooseFolder();
        startPage.RecentClicked += path => OpenProject(path);
        output.LocationClicked += GoToLocation;
        runner.Output += text => output.AppendOutput(text);
        runner.Exited += (code, current) => RunOnUi(() =>
        {
            if (!current) return;
            output.AppendInfo(code == 0 ? "Finished\n" : $"Exited with code {code}\n");
            SetRunning(false);
        });

        SetRunning(false);
        ShowStartPage();
    }

    protected override bool ShowWithoutActivation => options.ShotPath != null;

    int ToDevice(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    int ToLogical(int device) => (int)Math.Round(device * 96.0 / DeviceDpi);

    void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    void ApplySplitters()
    {
        outer.SplitterWidth = ToDevice(5);
        inner.SplitterWidth = ToDevice(5);
        outer.SetLimits(ToDevice(120), ToDevice(300));
        inner.SetLimits(ToDevice(120), ToDevice(60));
        if (!outer.Panel1Collapsed) outer.MoveTo(ToDevice(treeWidth));
        if (!inner.Panel2Collapsed) inner.MoveTo(inner.Height - inner.SplitterWidth - ToDevice(outputHeight));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (options.ShotPath == null) RestoreWindow();
        ApplySplitters();
        startPage.SetRecent(settings.RecentProjects);
        var project = options.Project ?? (options.ShotPath == null ? settings.RecentProjects.FirstOrDefault(Directory.Exists) : null);
        if (project != null) OpenProject(project);
        if (projectRoot != null)
            foreach (var file in options.Open) tabs.Open(Path.Combine(projectRoot, file), false);
        if (options.Play) Play();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplySplitters();
        if (options.ShotPath == null) return;
        var timer = new Timer { Interval = Math.Max(50, options.Delay) };
        timer.Tick += (s, args) =>
        {
            timer.Dispose();
            try { SaveScreenshot(options.ShotPath); }
            finally { Close(); }
        };
        timer.Start();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        ApplySplitters();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(new Action(ApplySplitters));
    }

    void RestoreWindow()
    {
        if (settings.WindowWidth <= 0 || settings.WindowHeight <= 0) return;
        var bounds = new Rectangle(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    void SaveWindow()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.WindowX = bounds.X;
        settings.WindowY = bounds.Y;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.Maximized = WindowState == FormWindowState.Maximized;
        settings.TreeWidth = treeWidth;
        settings.OutputHeight = outputHeight;
        settings.Save();
    }

    void ShowStartPage()
    {
        projectRoot = null;
        outer.Panel1Collapsed = true;
        inner.Panel2Collapsed = true;
        tabs.Visible = false;
        startPage.Visible = true;
        startPage.SetRecent(settings.RecentProjects);
        Text = "Love Editor";
        projectLabel.Text = "";
        UpdateCommands();
        UpdateStatus();
    }

    void OpenProject(string directory)
    {
        if (!Directory.Exists(directory))
        {
            MessageBox.Show(this, $"Couldn't find {directory}.", "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            settings.RecentProjects.RemoveAll(p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase));
            startPage.SetRecent(settings.RecentProjects);
            return;
        }
        if (!tabs.CloseAll()) return;
        runner.Stop();
        SetRunning(false);
        output.Clear();
        projectRoot = Path.GetFullPath(directory).TrimEnd('\\');
        LuaLanguage.Open(projectRoot);
        string name = Path.GetFileName(projectRoot);
        tree.Open(projectRoot);
        output.ProjectRoot = projectRoot;
        startPage.Visible = false;
        tabs.Visible = true;
        outer.Panel1Collapsed = false;
        inner.Panel2Collapsed = false;
        ApplySplitters();
        Text = $"{name} - Love Editor";
        projectLabel.Text = name;
        if (options.ShotPath == null)
        {
            settings.AddRecent(projectRoot);
            settings.Save();
        }
        var main = Path.Combine(projectRoot, "main.lua");
        if (File.Exists(main)) tabs.Open(main, false);
        UpdateCommands();
        UpdateStatus();
    }

    async void ChooseFolder()
    {
        if (picking) return;
        picking = true;
        FolderPick? pick;
        try
        {
            pick = await FolderPicker.PickAsync(this, "Open a project folder", projectRoot != null ? Path.GetDirectoryName(projectRoot) : ProjectTemplate.DefaultLocation);
        }
        catch (Exception ex)
        {
            picking = false;
            MessageBox.Show(this, ex.Message, "Love Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (pick == null)
        {
            picking = false;
            return;
        }
        tabs.DeferFocus = true;
        try
        {
            OpenProject(pick.Path);
        }
        finally
        {
            await pick.Closed;
            tabs.DeferFocus = false;
            tabs.Active?.FocusView();
            picking = false;
        }
    }

    void NewProject()
    {
        using var dialog = new NewProjectDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenProject(dialog.ProjectPath);
    }

    void Play()
    {
        if (projectRoot == null) return;
        if (!tabs.SaveAll()) return;
        output.Clear();
        output.AppendInfo($"Running {Path.GetFileName(projectRoot)}\n");
        if (!LoveRunner.CapturesOutput) output.AppendInfo("print() output needs Windows 10 or newer, so it won't show up here.\n");
        try
        {
            runner.Start(projectRoot);
        }
        catch (Exception ex)
        {
            output.AppendError(ex.Message + "\n");
            SetRunning(false);
            return;
        }
        SetRunning(true);
    }

    void StopGame()
    {
        if (!runner.IsRunning) return;
        runner.Stop();
        output.AppendInfo("Stopped\n");
        SetRunning(false);
    }

    void SetRunning(bool running)
    {
        playButton.Text = running ? "\u25a0  Stop" : "\u25b6  Play";
        playButton.ForeColor = running ? Color.FromArgb(170, 30, 30) : Color.FromArgb(0, 120, 0);
        playButton.ToolTipText = running ? "Stop the game (Shift+F5)" : "Run the game (F5)";
    }

    void UpdateCommands()
    {
        bool open = projectRoot != null;
        playButton.Enabled = open;
        newFileButton.Enabled = open;
        newFolderButton.Enabled = open;
    }

    void UpdateStatus()
    {
        var document = tabs.Visible ? tabs.Active : null;
        positionLabel.Text = document?.Position ?? "";
        languageLabel.Text = document?.Language ?? "";
    }

    void GoToLocation(string file, int line)
    {
        if (projectRoot == null) return;
        var document = tabs.Open(Path.Combine(projectRoot, file.Replace('/', '\\')), false);
        document?.GoTo(line, 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5: Play(); return true;
            case Keys.Shift | Keys.F5: StopGame(); return true;
            case Keys.Control | Keys.S: if (tabs.Active != null) tabs.TrySave(tabs.Active); return true;
            case Keys.Control | Keys.Shift | Keys.S: tabs.SaveAll(); return true;
            case Keys.Control | Keys.W: tabs.CloseActive(); return true;
            case Keys.Control | Keys.N: tree.NewFile(); return true;
            case Keys.Control | Keys.O: ChooseFolder(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (options.ShotPath != null) return;
        if (!tabs.CloseAll())
        {
            e.Cancel = true;
            return;
        }
        runner.Stop();
        LuaLanguage.Close();
        SaveWindow();
    }

    void SaveScreenshot(string path)
    {
        using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
        using (var graphics = Graphics.FromImage(bitmap))
        {
            foreach (var rich in Descendants(this).Where(c => (c is RichTextBox || c is ScintillaNET.Scintilla) && c.Visible && c.Width > 0 && c.Height > 0))
            {
                using var part = new Bitmap(rich.Width, rich.Height);
                using (var partGraphics = Graphics.FromImage(part))
                {
                    var hdc = partGraphics.GetHdc();
                    Native.PrintWindow(rich.Handle, hdc, 0);
                    partGraphics.ReleaseHdc(hdc);
                }
                var origin = rich.PointToScreen(Point.Empty);
                graphics.DrawImage(part, origin.X - Left, origin.Y - Top);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
    }

    static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            runner.Stop();
            LuaLanguage.Close();
            playFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
