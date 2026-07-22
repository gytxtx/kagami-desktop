using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class ProcessNameMatcherTests
{
    [Theory]
    [InlineData("Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia.exe")]
    [InlineData("Cafe.Launcher.Avalonia.exe", "Cafe.Launcher.Avalonia")]
    [InlineData("CAFE.LAUNCHER.AVALONIA.EXE", "cafe.launcher.avalonia")]
    [InlineData("Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia")]
    public void EqualsIgnoringExe_AcceptsOptionalExe(string expected, string actual) =>
        Assert.True(ProcessNameMatcher.EqualsIgnoringExe(expected, actual));

    [Theory]
    [InlineData("Cafe.Launcher", "Cafe.Launcher.Avalonia.exe")]
    [InlineData("Cafe.Launcher.Avalonia.exe.exe", "Cafe.Launcher.Avalonia.exe")]
    public void EqualsIgnoringExe_DoesNotMatchDifferentProcessNames(string expected, string actual) =>
        Assert.False(ProcessNameMatcher.EqualsIgnoringExe(expected, actual));
}
