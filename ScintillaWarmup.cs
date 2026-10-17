using System;
using System.Threading;
using ScintillaNET;

namespace LoveEditor;

static class ScintillaWarmup
{
    static readonly ManualResetEventSlim done = new(true);

    public static void Start()
    {
        done.Reset();
        var thread = new Thread(() =>
        {
            try
            {
                using var editor = new Scintilla();
                _ = editor.Handle;
                editor.LexerName = "lua";
            }
            catch (Exception)
            {
            }
            finally
            {
                done.Set();
            }
        })
        { IsBackground = true, Name = "scintilla-warmup" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void Wait() => done.Wait();
}
