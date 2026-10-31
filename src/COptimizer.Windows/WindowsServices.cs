using System.ComponentModel;
using System.ServiceProcess;
using COptimizer.Core.Environment;
using Microsoft.Win32;

namespace COptimizer.Windows;

public sealed class WindowsServiceManager : IServiceManager
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public IStoppedServices StopTemporarily(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken)
    {
        var stopped = new List<string>();
        var problems = new List<string>();

        foreach (var name in serviceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var service = new ServiceController(name);
                if (service.Status == ServiceControllerStatus.Stopped)
                    continue;

                if (service.Status != ServiceControllerStatus.StopPending)
                    service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
                stopped.Add(name);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                problems.Add($"Service '{name}' did not stop in time; files it uses are skipped.");
            }
            catch (InvalidOperationException e)
            {
                // Service missing, access denied or not stoppable.
                problems.Add($"Service '{name}' could not be stopped: {(e.InnerException as Win32Exception)?.Message ?? e.Message}");
            }
        }

        return new StoppedServices(stopped, problems);
    }

    private sealed class StoppedServices(List<string> stopped, List<string> problems) : IStoppedServices
    {
        public IReadOnlyList<string> Problems => problems;

        public void Dispose()
        {
            // Start in reverse order so dependencies come back first.
            for (var i = stopped.Count - 1; i >= 0; i--)
            {
                try
                {
                    using var service = new ServiceController(stopped[i]);
                    if (service.Status == ServiceControllerStatus.Stopped)
                        service.Start();
                }
                catch (InvalidOperationException)
                {
                    // Windows starts these services on demand anyway (trigger start).
                }
            }
        }
    }
}

public sealed class WindowsSystemState : ISystemState
{
    public bool IsRebootPending()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
            || KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
    }

    private static bool KeyExists(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key is not null;
    }
}
