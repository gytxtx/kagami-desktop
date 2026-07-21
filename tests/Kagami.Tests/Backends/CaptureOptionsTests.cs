using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class CaptureOptionsTests
{
    [Fact]
    public void DefaultCaptureOptions_HasAutoMode()
    {
        var options = new CaptureOptions();
        Assert.Equal(CaptureMode.Auto, options.RequestedMode);
        Assert.False(options.AllowSemanticFallback);
    }

    [Fact]
    public void CaptureResult_WithFallback_UpdatesModeAndFlags()
    {
        var original = new CaptureResult
        {
            FilePath = @"C:\Temp\img.png",
            Width = 800,
            Height = 600,
            X = 100,
            Y = 200,
            CaptureBackend = "legacy_gdi",
            ActualMode = "gdi-desktop-crop",
            RequestedMode = "window",
            FallbackUsed = false,
            OcclusionPossible = false
        };

        var withFallback = original.WithFallback("visible-desktop-crop", "dxgi", CaptureMethod.DxgiDesktopDuplication);

        Assert.True(withFallback.FallbackUsed);
        Assert.True(withFallback.OcclusionPossible);
        Assert.Equal("visible-desktop-crop", withFallback.ActualMode);
        Assert.Equal("dxgi", withFallback.CaptureBackend);
        Assert.Equal(original.FilePath, withFallback.FilePath); // path preserved
    }
}

public class GuardValidationResultTests
{
    [Fact]
    public void ValidResult_HasNoFailureInfo()
    {
        var result = new GuardValidationResult { Valid = true };

        Assert.True(result.Valid);
        Assert.Null(result.FailureCode);
        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public void InvalidResult_HasFailureInfo()
    {
        var result = new GuardValidationResult
        {
            Valid = false,
            FailureCode = ErrorCodes.WindowDestroyed,
            FailureMessage = "Window gone."
        };

        Assert.False(result.Valid);
        Assert.Equal(ErrorCodes.WindowDestroyed, result.FailureCode);
        Assert.Equal("Window gone.", result.FailureMessage);
    }
}
