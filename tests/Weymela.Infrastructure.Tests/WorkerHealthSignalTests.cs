using Weymela.Infrastructure.Operations;
using Xunit;

namespace Weymela.Infrastructure.Tests;

public sealed class WorkerHealthSignalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "weymela-worker-health-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Successful_cycle_atomically_publishes_only_a_non_secret_health_marker()
    {
        var path = Path.Combine(directory, "healthy");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "stale-partial-content");

        new WorkerHealthSignal(path).MarkSuccessfulCycle();

        Assert.Equal("healthy\n", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory));
        }
    }

    [Fact]
    public void Failed_cycle_removes_a_previously_healthy_signal()
    {
        var path = Path.Combine(directory, "healthy");
        var signal = new WorkerHealthSignal(path);
        signal.MarkSuccessfulCycle();

        signal.MarkFailedCycle();

        Assert.False(File.Exists(path));
        signal.MarkFailedCycle();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
