using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoveEditor;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ScintillaWarmup.Start();
        var options = StartOptions.Parse(args);
        var settings = options.ShotPath == null ? AppSettings.Load() : new AppSettings();
        Application.Run(new MainForm(settings, options));
    }
}

sealed class StartOptions
{
    public string? Project { get; private set; }
    public string? ShotPath { get; private set; }
    public List<string> Open { get; } = new();
    public int Delay { get; private set; } = 800;
    public bool Play { get; private set; }
    public Size? ClientSize { get; private set; }

    public static StartOptions Parse(string[] args)
    {
        var options = new StartOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--project": options.Project = Next(); break;
                case "--shot": options.ShotPath = Path.GetFullPath(Next()); break;
                case "--open": options.Open.Add(Next()); break;
                case "--delay": options.Delay = int.TryParse(Next(), out var delay) ? delay : options.Delay; break;
                case "--play": options.Play = true; break;
                case "--size":
                    var parts = Next().Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)) options.ClientSize = new Size(w, h);
                    break;
                default:
                    if (Directory.Exists(args[i])) options.Project = args[i];
                    break;
            }
        }
        return options;
    }
}
