using System.Runtime.InteropServices;
using COptimizer.Core;
using COptimizer.Core.Model;
using COptimizer.Core.SystemCleaners;

namespace COptimizer.Windows;

/// <summary>Empties the Recycle Bin on all drives.</summary>
public sealed partial class RecycleBinCleaner : ISystemCleaner
{
    private const uint SherbNoConfirmation = 0x1;
    private const uint SherbNoProgressUi = 0x2;
    private const uint SherbNoSound = 0x4;
    private const int EUnexpected = unchecked((int)0x8000FFFF); // returned when the bin is already empty

    public CleanerInfo Info { get; } = new()
    {
        Id = "windows.recycle-bin",
        Name = "Recycle Bin",
        Description = "Files you deleted earlier. After emptying the Recycle Bin they can no longer be restored.",
        Category = CleanerCategory.System,
        Risk = RiskLevel.Moderate,
    };

    public bool IsSlow => false;

    public bool RequiresAdministrator => false;

    public Task<SystemScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var (bytes, items) = Query();
        return new SystemScanResult
        {
            Bytes = bytes,
            IsApplicable = items > 0,
            Details = [items == 1 ? "1 item" : $"{items:N0} items on all drives"],
        };
    }, cancellationToken);

    public Task<SystemCleanResult> CleanAsync(IProgress<SystemCleanProgress>? progress, CancellationToken cancellationToken) =>
        StaThread.RunAsync(() =>
        {
            var (before, _) = Query();
            var hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
            var (after, _) = Query();

            if (hr < 0 && hr != EUnexpected)
                return SystemCleanResult.Failure($"The Recycle Bin could not be emptied (0x{hr:X8}).", Math.Max(0, before - after));

            progress?.Report(new SystemCleanProgress(before - after, before, null));
            return SystemCleanResult.Success(Math.Max(0, before - after));
        }, cancellationToken);

    private static (long Bytes, long Items) Query()
    {
        var info = new ShQueryRbInfo { cbSize = (uint)Marshal.SizeOf<ShQueryRbInfo>() };
        var hr = SHQueryRecycleBinW(null, ref info);
        return hr < 0 ? (0, 0) : (info.i64Size, info.i64NumItems);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string? pszRootPath, ref ShQueryRbInfo pSHQueryRBInfo);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
