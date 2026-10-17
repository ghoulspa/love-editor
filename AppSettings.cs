using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace LoveEditor;

sealed class AppSettings
{
    public List<string> RecentProjects { get; set; } = new();
    public int TreeWidth { get; set; } = 230;
    public int OutputHeight { get; set; } = 170;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Maximized { get; set; }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoveEditor", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
        }
        catch (Exception)
        {
        }
    }

    public void AddRecent(string path)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        if (RecentProjects.Count > 8) RecentProjects.RemoveRange(8, RecentProjects.Count - 8);
    }
}
