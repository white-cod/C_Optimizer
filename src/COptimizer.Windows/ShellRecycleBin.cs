using System.Runtime.InteropServices;
using COptimizer.Core.Cleaning;

namespace COptimizer.Windows;

/// <summary>
/// Moves files to the Recycle Bin via the shell's IFileOperation. If the shell would delete a file permanently
/// instead (file larger than the bin, drive without a bin, bin disabled), the operation is aborted and an
/// <see cref="IOException"/> is thrown — a "recycle" request never silently turns into a permanent delete.
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin
{
    private const uint FofSilent = 0x0004;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofAllowUndo = 0x0040;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;

    private const uint TsfDeleteRecycleIfPossible = 0x0080;

    private const int EAbort = unchecked((int)0x80004004);

    public void MoveToRecycleBin(string path)
    {
        Exception? failure = null;

        // IFileOperation must run in a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try
            {
                RecycleOnCurrentThread(path);
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is IOException)
            throw failure;
        if (failure is not null)
            throw new IOException($"Cannot move '{path}' to the Recycle Bin: {failure.Message}", failure);
    }

    private static void RecycleOnCurrentThread(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("Item not found.", path);

        var operation = (IFileOperation)new FileOperationClass();
        try
        {
            operation.SetOperationFlags(FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi | FofxRecycleOnDelete | FofxEarlyFailure);

            NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
            var sink = new RecycleGuardSink();
            operation.DeleteItem(item, sink);

            var hr = operation.PerformOperations();

            if (sink.WouldDeletePermanently)
                throw new IOException($"'{path}' cannot be moved to the Recycle Bin (too large or the bin is disabled on this drive).");
            if (hr < 0 || operation.GetAnyOperationsAborted() || sink.DeleteResult < 0)
                throw new IOException($"Cannot move '{path}' to the Recycle Bin (0x{(hr < 0 ? hr : sink.DeleteResult):X8}).");
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException($"'{path}' is still present after moving to the Recycle Bin.");
        }
        finally
        {
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [ComVisible(true)]
    private sealed class RecycleGuardSink : IFileOperationProgressSink
    {
        public bool WouldDeletePermanently { get; private set; }

        public int DeleteResult { get; private set; }

        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            if ((dwFlags & TsfDeleteRecycleIfPossible) == 0)
            {
                WouldDeletePermanently = true;
                return EAbort;
            }

            return 0;
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated)
        {
            DeleteResult = hrDelete;
            return 0;
        }

        public int StartOperations() => 0;

        public int FinishOperations(int hrResult) => 0;

        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => 0;

        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName, int hrRename, IShellItem psiNewlyCreated) => 0;

        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => 0;

        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrMove, IShellItem psiNewlyCreated) => 0;

        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => 0;

        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrCopy, IShellItem psiNewlyCreated) => 0;

        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName) => 0;

        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName, string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem) => 0;

        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => 0;

        public int ResetTimer() => 0;

        public int PauseTimer() => 0;

        public int ResumeTimer() => 0;
    }
}
