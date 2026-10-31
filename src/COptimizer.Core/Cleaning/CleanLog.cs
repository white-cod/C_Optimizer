using System.Globalization;

namespace COptimizer.Core.Cleaning;

/// <summary>Receives every file decision made by the clean engine (not called during dry runs).</summary>
public interface ICleanLog
{
    void Removed(string ruleId, string path, long length);

    void Skipped(string ruleId, SkippedFile file);
}

/// <summary>Plain-text log file with one line per removed or skipped file.</summary>
public sealed class FileCleanLog : ICleanLog, IDisposable
{
    private readonly StreamWriter _writer;

    public FileCleanLog(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        Path = path;
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
        _writer.WriteLine($"# C_Optimizer clean log, started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
    }

    public string Path { get; }

    /// <summary>Creates a log in the given folder named after the current time.</summary>
    public static FileCleanLog CreateIn(string folder) =>
        new(System.IO.Path.Combine(folder, $"clean-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log"));

    public void Removed(string ruleId, string path, long length) =>
        _writer.WriteLine($"REMOVED\t{ruleId}\t{length}\t{path}");

    public void Skipped(string ruleId, SkippedFile file)
    {
        if (file.Reason != SkipReason.Missing)
            _writer.WriteLine($"SKIPPED\t{ruleId}\t{file.Length}\t{file.Path}\t{file.Reason}\t{file.Details}");
    }

    /// <summary>Free-form line, e.g. a system cleaner result or a service that could not be stopped.</summary>
    public void Note(string message) => _writer.WriteLine($"NOTE\t{message}");

    public void Dispose() => _writer.Dispose();
}
