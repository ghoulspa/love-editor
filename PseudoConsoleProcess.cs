using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LoveEditor;

sealed class PseudoConsoleProcess : IGameProcess
{
    public static bool IsSupported { get; } = GetProcAddress(GetModuleHandle("kernel32.dll"), "CreatePseudoConsole") != IntPtr.Zero;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    readonly IntPtr pseudoConsole;
    readonly SafeFileHandle inputWrite;
    readonly SafeFileHandle outputRead;
    readonly IntPtr processHandle;
    readonly JobObject job;
    readonly IntPtr attributeList;
    readonly LineScreen screen;
    readonly ManualResetEvent readDone = new(false);
    int disposed;

    PseudoConsoleProcess(IntPtr pseudoConsole, SafeFileHandle inputWrite, SafeFileHandle outputRead, IntPtr processHandle, JobObject job, IntPtr attributeList, LineScreen screen)
    {
        this.pseudoConsole = pseudoConsole;
        this.inputWrite = inputWrite;
        this.outputRead = outputRead;
        this.processHandle = processHandle;
        this.job = job;
        this.attributeList = attributeList;
        this.screen = screen;
    }

    public static PseudoConsoleProcess Start(string commandLine, string workingDirectory, short columns = 1000, short rows = 100)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();

        int hr = CreatePseudoConsole(new Coord { X = columns, Y = rows }, inputRead, outputWrite, 0, out var pseudoConsole);
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0) throw new Win32Exception(hr);

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size)) throw new Win32Exception();
        if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)ProcThreadAttributePseudoConsole, pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();

        var job = new JobObject();

        var startup = new StartupInfoEx();
        startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
        startup.StartupInfo.dwFlags = StartfUseStdHandles;
        startup.lpAttributeList = attributeList;

        if (!CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment | CreateSuspended, IntPtr.Zero, workingDirectory, ref startup, out var info))
        {
            var error = new Win32Exception();
            ClosePseudoConsole(pseudoConsole);
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            job.Dispose();
            inputWrite.Dispose();
            outputRead.Dispose();
            throw error;
        }

        job.Assign(info.hProcess);
        ResumeThread(info.hThread);
        CloseHandle(info.hThread);

        var process = new PseudoConsoleProcess(pseudoConsole, inputWrite, outputRead, info.hProcess, job, attributeList, new LineScreen(rows));
        new Thread(process.ReadLoop) { IsBackground = true, Name = "pty-read" }.Start();
        new Thread(process.WaitLoop) { IsBackground = true, Name = "pty-wait" }.Start();
        return process;
    }

    public void Kill() => job.Terminate();

    void ReadLoop()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[8192];
        try
        {
            using var stream = new FileStream(outputRead, FileAccess.Read, 1, false);
            while (true)
            {
                int read;
                try { read = stream.Read(bytes, 0, bytes.Length); }
                catch (IOException) { break; }
                if (read <= 0) break;
                int count = decoder.GetChars(bytes, 0, read, chars, 0);
                var text = screen.Feed(chars, count);
                if (text.Length > 0) Output?.Invoke(text);
            }
            var rest = screen.Flush();
            if (rest.Length > 0) Output?.Invoke(rest);
        }
        finally
        {
            readDone.Set();
        }
    }

    void WaitLoop()
    {
        WaitForSingleObject(processHandle, Infinite);
        GetExitCodeProcess(processHandle, out var exitCode);
        ClosePseudoConsole(pseudoConsole);
        readDone.WaitOne(2000);
        Exited?.Invoke(exitCode);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        inputWrite.Dispose();
        CloseHandle(processHandle);
        job.Dispose();
        DeleteProcThreadAttributeList(attributeList);
        Marshal.FreeHGlobal(attributeList);
    }

    sealed class LineScreen
    {
        enum State { Text, Escape, Csi, Osc, OscEscape }
        State state;
        readonly StringBuilder line = new();
        readonly StringBuilder parameters = new();
        readonly int rows;
        int row, column;
        bool dirty;

        public LineScreen(int rows) => this.rows = rows;

        public string Feed(char[] chars, int count)
        {
            var output = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                switch (state)
                {
                    case State.Text:
                        if (c == '\x1b') state = State.Escape;
                        else if (c == '\n') EmitLine(output);
                        else if (c == '\r') column = 0;
                        else if (c == '\b') column = Math.Max(0, column - 1);
                        else if (c == '\t') Put(' ');
                        else if (c >= ' ') Put(c);
                        break;
                    case State.Escape:
                        if (c == '[') { state = State.Csi; parameters.Clear(); }
                        else state = c == ']' ? State.Osc : State.Text;
                        break;
                    case State.Csi:
                        if (c >= '@' && c <= '~') { state = State.Text; ApplyCsi(c, output); }
                        else parameters.Append(c);
                        break;
                    case State.Osc:
                        if (c == '\a') state = State.Text;
                        else if (c == '\x1b') state = State.OscEscape;
                        break;
                    case State.OscEscape:
                        state = c == '\\' ? State.Text : State.Osc;
                        break;
                }
            }
            return output.ToString();
        }

        public string Flush()
        {
            if (!dirty) return "";
            var output = new StringBuilder();
            EmitLine(output);
            return output.ToString();
        }

        void Put(char c)
        {
            while (line.Length < column) line.Append(' ');
            if (column < line.Length) line[column] = c;
            else line.Append(c);
            column++;
            dirty = true;
        }

        void EmitLine(StringBuilder output)
        {
            output.Append(line.ToString().TrimEnd()).Append('\n');
            line.Clear();
            column = 0;
            row = Math.Min(row + 1, rows - 1);
            dirty = false;
        }

        void MoveTo(StringBuilder output, int toRow, int toColumn)
        {
            while (row < Math.Min(toRow, rows - 1)) EmitLine(output);
            column = toColumn;
        }

        void ApplyCsi(char command, StringBuilder output)
        {
            var text = parameters.ToString();
            if (text.StartsWith("?")) return;
            var parts = text.Split(';');
            int Arg(int index, int fallback) => index < parts.Length && int.TryParse(parts[index], out var value) && value > 0 ? value : fallback;
            switch (command)
            {
                case 'G': column = Arg(0, 1) - 1; break;
                case 'C': column += Arg(0, 1); break;
                case 'D': column = Math.Max(0, column - Arg(0, 1)); break;
                case 'B': MoveTo(output, row + Arg(0, 1), column); break;
                case 'E': MoveTo(output, row + Arg(0, 1), 0); break;
                case 'd': MoveTo(output, Arg(0, 1) - 1, column); break;
                case 'H':
                case 'f': MoveTo(output, Arg(0, 1) - 1, Arg(1, 1) - 1); break;
                case 'X':
                    for (int i = column; i < Math.Min(line.Length, column + Arg(0, 1)); i++) line[i] = ' ';
                    break;
                case 'K':
                    int mode = text.Length == 0 ? 0 : Arg(0, 0);
                    if (mode == 0 && column < line.Length) line.Length = column;
                    else if (mode == 1) for (int i = 0; i < Math.Min(column + 1, line.Length); i++) line[i] = ' ';
                    else if (mode == 2) line.Clear();
                    break;
            }
        }
    }

    const int ProcThreadAttributePseudoConsole = 0x00020016;
    const uint ExtendedStartupInfoPresent = 0x00080000;
    const uint CreateUnicodeEnvironment = 0x00000400;
    const uint CreateSuspended = 0x00000004;
    const int StartfUseStdHandles = 0x00000100;
    const uint Infinite = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    struct Coord { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, int size);

    [DllImport("kernel32.dll")]
    static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pseudoConsole);

    [DllImport("kernel32.dll")]
    static extern void ClosePseudoConsole(IntPtr pseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? currentDirectory, ref StartupInfoEx startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    static extern bool GetExitCodeProcess(IntPtr process, out int exitCode);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
}
