using COptimizer.Core.Environment;
using COptimizer.Core.Safety;

namespace COptimizer.Core;

/// <summary>Machine snapshot shared by the scanner and the clean engine.</summary>
public sealed class CleanupContext
{
    public CleanupContext(SystemPaths system, IReadOnlyList<UserProfile> profiles)
    {
        System = system;
        Profiles = profiles;
        Policy = new SafetyPolicy(system, profiles);
    }

    public SystemPaths System { get; }

    public IReadOnlyList<UserProfile> Profiles { get; }

    public SafetyPolicy Policy { get; }

    public static CleanupContext Create(ISystemPathsProvider systemPaths, IProfileProvider profiles) =>
        new(systemPaths.GetSystemPaths(), profiles.GetProfiles());
}
