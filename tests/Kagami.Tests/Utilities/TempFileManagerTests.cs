using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class TempFileManagerTests
{
    [Fact]
    public void BaseDir_IsUnderTempPath()
    {
        var baseDir = TempFileManager.BaseDir;
        var tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        Assert.StartsWith(tempPath, baseDir);
        Assert.EndsWith("kagami", baseDir);
    }

    [Fact]
    public void GetScreenshotPath_WithoutOutput_UuidBasedPath()
    {
        var path = TempFileManager.GetScreenshotPath();

        Assert.Contains("kagami", path);
        Assert.Contains("screenshots", path);
        Assert.EndsWith(".png", path);
    }

    [Fact]
    public void GetScreenshotPath_WithOutput_UsesProvidedPath()
    {
        var provided = @"C:\test\custom.png";
        var path = TempFileManager.GetScreenshotPath(provided);

        Assert.Equal(Path.GetFullPath(provided), path);
    }

    [Fact]
    public void GetGuardPath_ReturnsGuardJsonPath()
    {
        var path = TempFileManager.GetGuardPath();

        Assert.Contains("kagami", path);
        Assert.Contains("guards", path);
        Assert.StartsWith("guard-", Path.GetFileName(path));
        Assert.EndsWith(".json", path);
    }

    [Fact]
    public void CleanupExpired_DoesNotThrow_WhenDirectoryDoesNotExist()
    {
        // Should not throw even if directories don't exist yet
        var exception = Record.Exception(() => TempFileManager.CleanupExpired());
        Assert.Null(exception);
    }

    [Fact]
    public void CleanupExpired_DoesNotThrow_WhenFilesExist()
    {
        // Touch some files and verify cleanup doesn't throw
        var screenshotPath = TempFileManager.GetScreenshotPath();
        File.WriteAllText(screenshotPath, "test");
        var guardPath = TempFileManager.GetGuardPath();
        File.WriteAllText(guardPath, "{}");

        var exception = Record.Exception(() => TempFileManager.CleanupExpired());
        Assert.Null(exception);
    }
}
