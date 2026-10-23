using System.Diagnostics;
using CoopKeep.Core.Server;
using Xunit;

namespace CoopKeep.Core.Tests.Server;

public class ServerResourceMonitorTests
{
    [Fact]
    public void Sample_FindsProcessesUnderServerFolder_ByPath()
    {
        // Test süreci (dotnet) kendi klasörünün altında: "sunucu klasörü" o kabul edilir.
        using var self = Process.GetCurrentProcess();
        var folder = Path.GetDirectoryName(self.MainModule!.FileName)!;
        var monitor = new ServerResourceMonitor(folder);

        var first = monitor.Sample(null);
        var second = monitor.Sample(null); // önbellekteki süreçlerle

        Assert.True(first.ProcessCount >= 1);
        Assert.True(second.ProcessCount >= 1);
        Assert.True(second.MemoryBytes > 0);
    }

    [Fact]
    public void Sample_IgnoresProcessesOutsideServerFolder()
    {
        var monitor = new ServerResourceMonitor(Path.Combine(Path.GetTempPath(), "coopkeep-no-such-server"));

        Assert.Equal(0, monitor.Sample(null).ProcessCount);
    }
}
