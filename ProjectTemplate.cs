using System;
using System.IO;
using System.Text;

namespace LoveEditor;

static class ProjectTemplate
{
    public static string DefaultLocation => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Love Projects");

    public static string SuggestName(string location)
    {
        string name = "MyGame";
        for (int i = 2; Directory.Exists(Path.Combine(location, name)); i++) name = "MyGame" + i;
        return name;
    }

    public static void Create(string directory, string title)
    {
        Directory.CreateDirectory(directory);
        var encoding = new UTF8Encoding(false);
        string escaped = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(Path.Combine(directory, "conf.lua"),
            "function love.conf(t)\n" +
            $"  t.window.title = \"{escaped}\"\n" +
            "  t.window.width = 800\n" +
            "  t.window.height = 600\n" +
            "end\n", encoding);
        File.WriteAllText(Path.Combine(directory, "main.lua"),
            "function love.load()\n" +
            "end\n" +
            "\n" +
            "function love.update(dt)\n" +
            "end\n" +
            "\n" +
            "function love.draw()\n" +
            "  love.graphics.print(\"Hello, world!\", 360, 290)\n" +
            "end\n", encoding);
    }
}
