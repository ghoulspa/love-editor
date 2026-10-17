using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoveEditor;

sealed class NewProjectDialog : Form
{
    readonly TextBox nameBox;
    readonly TextBox locationBox;
    readonly Label pathLabel;
    readonly Button createButton;

    public NewProjectDialog()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        Text = "New Project";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var location = ProjectTemplate.DefaultLocation;
        nameBox = new TextBox { Width = 320, Text = ProjectTemplate.SuggestName(location), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        locationBox = new TextBox { Width = 320, Text = location, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        var browseButton = new Button { Text = "Browse…", AutoSize = true };
        pathLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 12) };
        createButton = new Button { Text = "Create", AutoSize = true, DialogResult = DialogResult.None };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(nameBox, 1, 0);
        grid.Controls.Add(new Label { Text = "Location:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(locationBox, 1, 1);
        grid.Controls.Add(browseButton, 2, 1);
        grid.Controls.Add(pathLabel, 0, 2);
        grid.SetColumnSpan(pathLabel, 3);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(createButton);
        grid.Controls.Add(buttons, 0, 3);
        grid.SetColumnSpan(buttons, 3);
        Controls.Add(grid);
        AcceptButton = createButton;
        CancelButton = cancelButton;
        ResumeLayout(false);
        PerformLayout();

        nameBox.TextChanged += (s, e) => UpdatePreview();
        locationBox.TextChanged += (s, e) => UpdatePreview();
        browseButton.Click += async (s, e) =>
        {
            browseButton.Enabled = false;
            try
            {
                var picked = await FolderPicker.PickAsync(this, "Choose where to put the project", locationBox.Text);
                if (picked != null)
                {
                    locationBox.Text = picked.Path;
                    await picked.Closed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "New Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                browseButton.Enabled = true;
            }
        };
        createButton.Click += (s, e) => Create();
        UpdatePreview();
    }

    public string ProjectName => nameBox.Text.Trim();

    public string ProjectPath => Path.Combine(locationBox.Text.Trim(), ProjectName);

    bool NameIsValid => ProjectName.Length > 0 && ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && ProjectName.Trim('.').Length > 0;

    void UpdatePreview()
    {
        bool valid = NameIsValid && locationBox.Text.Trim().Length > 0 && locationBox.Text.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        createButton.Enabled = valid;
        pathLabel.Text = valid ? "Creates " + ProjectPath : "Pick a name without \\ / : * ? \" < > |";
    }

    void Create()
    {
        if (Directory.Exists(ProjectPath) && Directory.EnumerateFileSystemEntries(ProjectPath).Any())
        {
            MessageBox.Show(this, $"There's already a folder called \"{ProjectName}\" there.", "New Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            ProjectTemplate.Create(ProjectPath, ProjectName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "New Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        nameBox.SelectAll();
        nameBox.Focus();
    }
}
