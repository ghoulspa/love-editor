using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace LoveEditor;

interface IGameProcess : IDisposable
{
    event Action<int>? Exited;
    void Kill();
}

sealed class WindowedGame : IGameProcess
{
    readonly Process process;
    readonly JobObject job = new();
    int disposed;

    public event Action<int>? Exited;

    WindowedGame(Process process)
    {
        this.process = process;
        job.Assign(process.Handle);
        process.Exited += (s, e) => Exited?.Invoke(process.ExitCode);
        process.EnableRaisingEvents = true;
    }

    public static WindowedGame Start(string love, string game)
    {
        var info = new ProcessStartInfo(love, $"\"{game}\"") { UseShellExecute = false, WorkingDirectory = game };
        return new WindowedGame(Process.Start(info) ?? throw new InvalidOperationException("LÖVE didn't start."));
    }

    public void Kill()
    {
        job.Terminate();
        try { process.Kill(); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        job.Dispose();
        process.Dispose();
    }
}
