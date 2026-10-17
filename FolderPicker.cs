using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoveEditor;

sealed class FolderPick
{
    public FolderPick(string path, Task closed)
    {
        Path = path;
        Closed = closed;
    }

    public string Path { get; }
    public Task Closed { get; }
}

static class FolderPicker
{
    const uint FosPickFolders = 0x20;
    const uint FosForceFileSystem = 0x40;
    const uint FosPathMustExist = 0x800;
    const uint SigdnFileSystemPath = 0x80058000;

    public static Task<FolderPick?> PickAsync(IWin32Window owner, string title, string? initialFolder)
    {
        var result = new TaskCompletionSource<FolderPick?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerHandle = owner.Handle;
        var thread = new Thread(() =>
        {
            IFileOpenDialog? dialog = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogClass();
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
                dialog.SetTitle(title);
                if (initialFolder != null && Directory.Exists(initialFolder) && SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                    dialog.SetFolder(folder);
                dialog.Advise(new Events(dialog, result, closed), out _);
                dialog.Show(ownerHandle);
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
            finally
            {
                result.TrySetResult(null);
                closed.TrySetResult(true);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        })
        { IsBackground = true, Name = "folder-picker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    sealed class Events : IFileDialogEvents
    {
        readonly IFileOpenDialog dialog;
        readonly TaskCompletionSource<FolderPick?> result;
        readonly TaskCompletionSource<bool> closed;

        public Events(IFileOpenDialog dialog, TaskCompletionSource<FolderPick?> result, TaskCompletionSource<bool> closed)
        {
            this.dialog = dialog;
            this.result = result;
            this.closed = closed;
        }

        public int OnFileOk(IntPtr sender)
        {
            try
            {
                dialog.GetResult(out var item);
                item.GetDisplayName(SigdnFileSystemPath, out var path);
                ((IOleWindow)dialog).GetWindow(out var window);
                new Thread(() =>
                {
                    var waited = Stopwatch.StartNew();
                    while (IsWindow(window) && waited.ElapsedMilliseconds < 1000) Thread.Sleep(2);
                    closed.TrySetResult(true);
                })
                { IsBackground = true, Name = "folder-picker-close" }.Start();
                result.TrySetResult(new FolderPick(path, closed.Task));
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
            return 0;
        }

        public int OnFolderChanging(IntPtr sender, IntPtr folder) => 0;
        public int OnFolderChange(IntPtr sender) => 0;
        public int OnSelectionChange(IntPtr sender) => 0;
        public int OnShareViolation(IntPtr sender, IntPtr item, out int response) { response = 0; return 0; }
        public int OnTypeChange(IntPtr sender) => 0;
        public int OnOverwrite(IntPtr sender, IntPtr item, out int response) { response = 0; return 0; }
    }

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialogClass
    {
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IFileDialogEvents events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("973510DB-7D7F-452B-8975-74A85828D354"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialogEvents
    {
        [PreserveSig] int OnFileOk(IntPtr sender);
        [PreserveSig] int OnFolderChanging(IntPtr sender, IntPtr folder);
        [PreserveSig] int OnFolderChange(IntPtr sender);
        [PreserveSig] int OnSelectionChange(IntPtr sender);
        [PreserveSig] int OnShareViolation(IntPtr sender, IntPtr item, out int response);
        [PreserveSig] int OnTypeChange(IntPtr sender);
        [PreserveSig] int OnOverwrite(IntPtr sender, IntPtr item, out int response);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow
    {
        void GetWindow(out IntPtr window);
        void ContextSensitiveHelp(bool enterMode);
    }

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr window);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, out IShellItem item);
}
