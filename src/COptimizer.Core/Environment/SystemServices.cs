namespace COptimizer.Core.Environment;

/// <summary>Stops Windows services for the duration of a cleanup and restores them afterwards.</summary>
public interface IServiceManager
{
    /// <summary>
    /// Stops the listed services that are running. Disposing the returned handle starts again exactly the services
    /// that were stopped. Problems (missing service, access denied, timeout) are reported in <see cref="IStoppedServices.Problems"/>.
    /// </summary>
    IStoppedServices StopTemporarily(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken);
}

public interface IStoppedServices : IDisposable
{
    IReadOnlyList<string> Problems { get; }
}

public interface ISystemState
{
    /// <summary>True when Windows waits for a restart to finish installing updates.</summary>
    bool IsRebootPending();
}
