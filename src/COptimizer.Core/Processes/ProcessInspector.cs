using System.Diagnostics;

namespace COptimizer.Core.Processes;

public interface IProcessInspector
{
    /// <summary>Returns the subset of process names (without ".exe") that are currently running.</summary>
    IReadOnlyList<string> GetRunning(IEnumerable<string> processNames);
}

public sealed class ProcessInspector : IProcessInspector
{
    public IReadOnlyList<string> GetRunning(IEnumerable<string> processNames)
    {
        var running = new List<string>();
        foreach (var name in processNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
                running.Add(name);

            foreach (var p in processes)
                p.Dispose();
        }

        return running;
    }
}
