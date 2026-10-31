using COptimizer.Core.SystemCleaners;
using COptimizer.Windows.VolumeCaches;

namespace COptimizer.Windows;

public static class WindowsSystemCleaners
{
    /// <summary>All system cleaners available on this machine for the given system volume (e.g. "C:\").</summary>
    public static IReadOnlyList<ISystemCleaner> Create(string systemVolume) =>
        [new RecycleBinCleaner(), .. VolumeCacheCatalog.CreateAvailable(systemVolume)];
}
