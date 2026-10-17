using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoveEditor;

sealed class StartPage : UserControl
{
    readonly FlowLayoutPanel recentList;
    readonly Label recentHeader;
    readonly Font titleFont;
    readonly Font headingFont;

    public event EventHandler? NewProjectClicked;
    public event EventHandler? OpenFolderClicked;
    public event Action<string>? RecentClicked;

    public StartPage()
    {
        BackColor = SystemColors.Window;
        AutoScroll = true;
        var baseFont = SystemFonts.MessageBoxFont;
        titleFont = new Font(baseFont.FontFamily, 20f, FontStyle.Regular);
        headingFont = new Font(baseFont.FontFamily, 11f, FontStyle.Regular);

        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(48, 40),
            BackColor = SystemColors.Window,
        };
        column.Controls.Add(new Label { Text = "Love Editor", Font = titleFont, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        column.Controls.Add(new Label { Text = "Make games with LÖVE.", ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(2, 0, 0, 24) });
        column.Controls.Add(new Label { Text = "Start", Font = headingFont, AutoSize = true, Margin = new Padding(1, 0, 0, 6) });
        column.Controls.Add(MakeLink("New Project…", () => NewProjectClicked?.Invoke(this, EventArgs.Empty)));
        column.Controls.Add(MakeLink("Open Folder…", () => OpenFolderClicked?.Invoke(this, EventArgs.Empty)));
        recentHeader = new Label { Text = "Recent", Font = headingFont, AutoSize = true, Margin = new Padding(1, 24, 0, 6) };
        column.Controls.Add(recentHeader);
        recentList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        column.Controls.Add(recentList);
        Controls.Add(column);
    }

    static LinkLabel MakeLink(string text, Action click)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = Color.FromArgb(0, 102, 204),
            ActiveLinkColor = Color.FromArgb(0, 84, 166),
            Margin = new Padding(2, 2, 0, 4),
        };
        link.LinkClicked += (s, e) => click();
        return link;
    }

    public void SetRecent(IEnumerable<string> projects)
    {
        recentList.SuspendLayout();
        foreach (Control control in recentList.Controls.Cast<Control>().ToList()) control.Dispose();
        var existing = projects.Where(Directory.Exists).ToList();
        foreach (var project in existing)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            row.Controls.Add(MakeLink(Path.GetFileName(project), () => RecentClicked?.Invoke(project)));
            row.Controls.Add(new Label { Text = project, ForeColor = SystemColors.GrayText, AutoSize = true, Margin = new Padding(8, 2, 0, 4) });
            recentList.Controls.Add(row);
        }
        recentHeader.Visible = existing.Count > 0;
        recentList.ResumeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            headingFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
