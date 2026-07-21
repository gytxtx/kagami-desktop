using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class ProcessHelperTests
{
    [Fact]
    public void GetProcessName_ForCurrentProcess_ReturnsDotnetOrTestRunner()
    {
        var pid = Environment.ProcessId;
        var name = ProcessHelper.GetProcessName(pid);

        Assert.NotNull(name);
        Assert.EndsWith(".exe", name);
    }

    [Fact]
    public void GetProcessName_ForInvalidPid_ReturnsNull()
    {
        var name = ProcessHelper.GetProcessName(-1);
        Assert.Null(name);
    }

    [Fact]
    public void GetProcessStartTime_ForCurrentProcess_ReturnsIso8601()
    {
        var pid = Environment.ProcessId;
        var startTime = ProcessHelper.GetProcessStartTime(pid);

        Assert.NotNull(startTime);
        Assert.Contains("T", startTime);   // ISO 8601 has T separator
        Assert.EndsWith("Z", startTime);   // UTC ends with Z
    }

    [Fact]
    public void GetProcessStartTime_ForInvalidPid_ReturnsNull()
    {
        var startTime = ProcessHelper.GetProcessStartTime(-1);
        Assert.Null(startTime);
    }

    [Fact]
    public void GetProcessCreationTimeRaw_ForCurrentProcess_ReturnsPositiveTicks()
    {
        var pid = Environment.ProcessId;
        var raw = ProcessHelper.GetProcessCreationTimeRaw(pid);

        Assert.NotNull(raw);
        Assert.True(raw!.Value > 0);
    }
}
