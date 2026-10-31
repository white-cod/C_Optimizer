using System.Runtime.InteropServices;

namespace COptimizer.Windows.VolumeCaches;

// COM interfaces of Disk Cleanup handlers (emptyvc.h). Handlers are registered under
// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches and are what cleanmgr.exe and Storage Sense use.

[ComImport]
[Guid("8FCE5227-04DA-11d1-A004-00805F8ABE06")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEmptyVolumeCache
{
    [PreserveSig]
    int Initialize(IntPtr hkRegKey, [MarshalAs(UnmanagedType.LPWStr)] string pcwszVolume, out IntPtr ppwszDisplayName, out IntPtr ppwszDescription, ref uint pdwFlags);

    [PreserveSig]
    int GetSpaceUsed(out ulong pdwlSpaceUsed, IEmptyVolumeCacheCallBack picb);

    [PreserveSig]
    int Purge(ulong dwlSpaceToFree, IEmptyVolumeCacheCallBack picb);

    [PreserveSig]
    int ShowProperties(IntPtr hwnd);

    [PreserveSig]
    int Deactivate(out uint pdwFlags);
}

[ComImport]
[Guid("02b7e3ba-4db3-11d2-b2d9-00c04f8eec8c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEmptyVolumeCache2
{
    // Base interface methods must be redeclared in order for classic COM interop.
    [PreserveSig]
    int Initialize(IntPtr hkRegKey, [MarshalAs(UnmanagedType.LPWStr)] string pcwszVolume, out IntPtr ppwszDisplayName, out IntPtr ppwszDescription, ref uint pdwFlags);

    [PreserveSig]
    int GetSpaceUsed(out ulong pdwlSpaceUsed, IEmptyVolumeCacheCallBack picb);

    [PreserveSig]
    int Purge(ulong dwlSpaceToFree, IEmptyVolumeCacheCallBack picb);

    [PreserveSig]
    int ShowProperties(IntPtr hwnd);

    [PreserveSig]
    int Deactivate(out uint pdwFlags);

    [PreserveSig]
    int InitializeEx(
        IntPtr hkRegKey,
        [MarshalAs(UnmanagedType.LPWStr)] string pcwszVolume,
        [MarshalAs(UnmanagedType.LPWStr)] string pcwszKeyName,
        out IntPtr ppwszDisplayName,
        out IntPtr ppwszDescription,
        out IntPtr ppwszBtnText,
        ref uint pdwFlags);
}

[ComImport]
[Guid("6E793361-73C6-11D0-8469-00AA00442901")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEmptyVolumeCacheCallBack
{
    [PreserveSig]
    int ScanProgress(ulong dwlSpaceUsed, uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string? pcwszStatus);

    [PreserveSig]
    int PurgeProgress(ulong dwlSpaceFreed, ulong dwlSpaceToFree, uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string? pcwszStatus);
}

internal static class VolumeCacheConstants
{
    public const int SOk = 0;
    public const int SFalse = 1;
    public const int EAbort = unchecked((int)0x80004004);
}

/// <summary>Receives progress from a handler; aborts the handler when cancellation is requested.</summary>
[ComVisible(true)]
internal sealed class VolumeCacheCallback(Action<ulong, ulong, string?>? onPurgeProgress, CancellationToken cancellationToken)
    : IEmptyVolumeCacheCallBack
{
    public int ScanProgress(ulong dwlSpaceUsed, uint dwFlags, string? pcwszStatus) =>
        cancellationToken.IsCancellationRequested ? VolumeCacheConstants.EAbort : VolumeCacheConstants.SOk;

    public int PurgeProgress(ulong dwlSpaceFreed, ulong dwlSpaceToFree, uint dwFlags, string? pcwszStatus)
    {
        onPurgeProgress?.Invoke(dwlSpaceFreed, dwlSpaceToFree, pcwszStatus);
        return cancellationToken.IsCancellationRequested ? VolumeCacheConstants.EAbort : VolumeCacheConstants.SOk;
    }
}
