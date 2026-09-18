using System.Text;

namespace Weymela.Infrastructure.Operations;

public sealed class WorkerHealthSignal
{
    public const string DefaultPath = "/tmp/weymela-worker/healthy";
    private static readonly byte[] HealthyContent = Encoding.ASCII.GetBytes("healthy\n");
    private readonly string path;

    public WorkerHealthSignal() : this(DefaultPath) { }
    internal WorkerHealthSignal(string path) => this.path = path;

    public void MarkSuccessfulCycle()
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Worker health path requires a directory.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(HealthyContent);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void MarkFailedCycle()
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
