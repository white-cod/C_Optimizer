using System.Runtime.InteropServices;
using COptimizer.Core.SystemCleaners;
using Microsoft.Win32;

namespace COptimizer.Windows.VolumeCaches;

/// <summary>
/// Runs one official Windows Disk Cleanup handler (the same COM component cleanmgr.exe and Storage Sense use),
/// e.g. "Previous Installations" (Windows.old) or "Update Cleanup" (superseded WinSxS components).
/// </summary>
public sealed class VolumeCacheCleaner : ISystemCleaner
{
    internal const string HandlersKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

    private readonly string _keyName;
    private readonly string _volume;

    public VolumeCacheCleaner(string keyName, CleanerInfo info, bool isSlow, string volume)
    {
        _keyName = keyName;
        _volume = volume.EndsWith('\\') ? volume : volume + "\\";
        Info = info;
        IsSlow = isSlow;
    }

    public CleanerInfo Info { get; }

    public bool IsSlow { get; }

    public bool RequiresAdministrator => true;

    /// <summary>Registry key name of the handler, e.g. "Previous Installations".</summary>
    public string HandlerName => _keyName;

    public static bool IsRegistered(string keyName)
    {
        using var key = OpenHandlerKey(keyName);
        return key?.GetValue(null) is string clsid && Guid.TryParse(clsid, out _);
    }

    public Task<SystemScanResult> ScanAsync(CancellationToken cancellationToken) =>
        StaThread.RunAsync(() => Scan(cancellationToken), cancellationToken);

    public Task<SystemCleanResult> CleanAsync(IProgress<SystemCleanProgress>? progress, CancellationToken cancellationToken) =>
        StaThread.RunAsync(() => Clean(progress, cancellationToken), cancellationToken);

    private SystemScanResult Scan(CancellationToken cancellationToken)
    {
        using var session = HandlerSession.Open(_keyName, _volume);
        if (session.Error is { } error)
            return SystemScanResult.NotApplicable(error);
        if (!session.HasData)
            return SystemScanResult.NotApplicable();

        var hr = session.Cache.GetSpaceUsed(out var used, new VolumeCacheCallback(null, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (hr < 0)
            return SystemScanResult.NotApplicable($"'{_keyName}' could not calculate its size (0x{hr:X8}).");

        return new SystemScanResult { Bytes = (long)used, IsApplicable = used > 0 };
    }

    private SystemCleanResult Clean(IProgress<SystemCleanProgress>? progress, CancellationToken cancellationToken)
    {
        using var session = HandlerSession.Open(_keyName, _volume);
        if (session.Error is { } error)
            return SystemCleanResult.Failure(error);
        if (!session.HasData)
            return SystemCleanResult.Success(0);

        // Handlers expect GetSpaceUsed before Purge (cleanmgr always calls it first).
        var hr = session.Cache.GetSpaceUsed(out var used, new VolumeCacheCallback(null, cancellationToken));
        if (hr < 0)
            return SystemCleanResult.Failure($"Size calculation failed (0x{hr:X8}).");

        ulong freed = 0;
        var callback = new VolumeCacheCallback((spaceFreed, spaceToFree, status) =>
        {
            freed = spaceFreed;
            progress?.Report(new SystemCleanProgress((long)spaceFreed, (long)Math.Max(spaceToFree, used), status));
        }, cancellationToken);

        hr = session.Cache.Purge(used, callback);
        cancellationToken.ThrowIfCancellationRequested();

        // Some handlers never report progress; on success assume the estimated amount was freed.
        var result = freed > 0 ? freed : used;
        return hr < 0
            ? SystemCleanResult.Failure($"Cleanup failed (0x{hr:X8}).", (long)freed)
            : SystemCleanResult.Success((long)result);
    }

    private static RegistryKey? OpenHandlerKey(string keyName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return baseKey.OpenSubKey($@"{HandlersKey}\{keyName}");
    }

    /// <summary>An initialized handler instance; released (and deactivated) on dispose.</summary>
    private sealed class HandlerSession : IDisposable
    {
        private readonly RegistryKey? _key;
        private readonly object? _instance;

        private HandlerSession(RegistryKey? key, object? instance, IEmptyVolumeCache? cache, bool hasData, string? error)
        {
            _key = key;
            _instance = instance;
            Cache = cache!;
            HasData = hasData;
            Error = error;
        }

        public IEmptyVolumeCache Cache { get; }

        public bool HasData { get; }

        public string? Error { get; }

        public static HandlerSession Open(string keyName, string volume)
        {
            var key = OpenHandlerKey(keyName);
            if (key?.GetValue(null) is not string clsidText || !Guid.TryParse(clsidText, out var clsid))
                return new HandlerSession(key, null, null, false, $"Cleanup handler '{keyName}' is not available on this system.");

            object instance;
            try
            {
                var type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
                instance = Activator.CreateInstance(type)!;
            }
            catch (COMException e)
            {
                return new HandlerSession(key, null, null, false, $"Cleanup handler '{keyName}' could not be loaded (0x{e.HResult:X8}).");
            }

            var flags = 0u;
            int hr;
            IntPtr name, description, button = IntPtr.Zero;
            var hkey = key.Handle.DangerousGetHandle();

            if (instance is IEmptyVolumeCache2 v2)
                hr = v2.InitializeEx(hkey, volume, keyName, out name, out description, out button, ref flags);
            else if (instance is IEmptyVolumeCache v1)
                hr = v1.Initialize(hkey, volume, out name, out description, ref flags);
            else
                return new HandlerSession(key, instance, null, false, $"'{keyName}' is not a Disk Cleanup handler.");

            Marshal.FreeCoTaskMem(name);
            Marshal.FreeCoTaskMem(description);
            Marshal.FreeCoTaskMem(button);

            if (hr < 0)
                return new HandlerSession(key, instance, null, false, $"Cleanup handler '{keyName}' failed to start (0x{hr:X8}).");

            // S_FALSE means "nothing to clean on this volume".
            return new HandlerSession(key, instance, (IEmptyVolumeCache)instance, hr != VolumeCacheConstants.SFalse, null);
        }

        public void Dispose()
        {
            if (_instance is not null)
            {
                if (HasData)
                    Cache.Deactivate(out _);
                Marshal.FinalReleaseComObject(_instance);
            }

            _key?.Dispose();
        }
    }
}
