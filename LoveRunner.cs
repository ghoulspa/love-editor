using System;
using System.IO;

namespace LoveEditor;

sealed class LoveRunner
{
    IGameProcess? process;

    public event Action<string>? Output;
    public event Action<int, bool>? Exited;

    public bool IsRunning => process != null;

    public static bool CapturesOutput => PseudoConsoleProcess.IsSupported;

    static string FindLove(string name)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "love", name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"LÖVE wasn't found next to Love Editor (love\\{name}).");
    }

    public void Start(string projectDirectory)
    {
        Stop();
        var game = projectDirectory.TrimEnd('\\');
        IGameProcess started;
        if (PseudoConsoleProcess.IsSupported)
        {
            var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var console = PseudoConsoleProcess.Start($"\"{shell}\" /d /s /c \"chcp 65001 >nul & \"{FindLove("lovec.exe")}\" \"{game}\"\"", game);
            console.Output += text => Output?.Invoke(text);
            started = console;
        }
        else started = WindowedGame.Start(FindLove("love.exe"), game);
        started.Exited += code =>
        {
            bool current = process == started;
            if (current) process = null;
            started.Dispose();
            Exited?.Invoke(code, current);
        };
        process = started;
    }

    public void Stop()
    {
        var running = process;
        process = null;
        running?.Kill();
    }
}
