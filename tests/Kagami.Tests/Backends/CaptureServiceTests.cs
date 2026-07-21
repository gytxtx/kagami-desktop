using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class CaptureServiceTests
{
    [Fact]
    public void GetBackendAvailability_ReturnsThreeBackends()
    {
        var service = new CaptureService();
        var availability = service.GetBackendAvailability();

        Assert.True(availability.Count >= 3);
        Assert.Contains("legacy_window_capture", availability);
        Assert.Contains("desktop_duplication", availability);
        Assert.Contains("legacy_gdi", availability);
        Assert.True(availability["legacy_gdi"]); // GDI always available
    }

    [Fact]
    public void AvailableBackendNames_AllHaveNonEmptyNames()
    {
        var service = new CaptureService();
        var names = service.AvailableBackendNames;

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }

    [Fact]
    public void Capture_FullDesktop_ReturnsResult()
    {
        var service = new CaptureService();
        var result = service.CaptureAsync(new CaptureOptions
        {
            RequestedMode = CaptureMode.Auto,
            AllowSemanticFallback = true
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.True(File.Exists(result.FilePath),
            $"Screenshot file not found at {result.FilePath}");
        Assert.True(new FileInfo(result.FilePath).Length > 0,
            "Screenshot file is empty");

        // Cleanup
        try { File.Delete(result.FilePath); } catch { }
    }

    [Fact]
    public void Capture_WithOutputPath_UsesProvidedPath()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"kagami-test-{Guid.NewGuid()}.png");
        var service = new CaptureService();

        try
        {
            var result = service.CaptureAsync(new CaptureOptions
            {
                RequestedMode = CaptureMode.Auto,
                AllowSemanticFallback = true,
                OutputPath = outputPath
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(outputPath), result.FilePath);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            try { File.Delete(outputPath); } catch { }
        }
    }

    [Fact]
    public void Capture_Region_ReturnsCorrectSize()
    {
        var service = new CaptureService();
        var result = service.CaptureAsync(new CaptureOptions
        {
            RequestedMode = CaptureMode.Auto,
            AllowSemanticFallback = true,
            X = 0, Y = 0, Width = 100, Height = 100
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.Equal(100, result.Width);
        Assert.Equal(100, result.Height);

        try { File.Delete(result.FilePath); } catch { }
    }
}
