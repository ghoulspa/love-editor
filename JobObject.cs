using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoveEditor;

sealed class JobObject : IDisposable
{
    const int ExtendedLimitInformationClass = 9;
    const uint LimitKillOnJobClose = 0x2000;

    readonly IntPtr handle;
    int disposed;

    public JobObject()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        var limits = new ExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = LimitKillOnJobClose;
        SetInformationJobObject(handle, ExtendedLimitInformationClass, ref limits, Marshal.SizeOf<ExtendedLimitInformation>());
    }

    public void Assign(IntPtr process) => AssignProcessToJobObject(handle, process);

    public void Terminate() => TerminateJobObject(handle, 1);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
