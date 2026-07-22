using System.Text.Json;
using Kagami.Protocol;

namespace Kagami.Tests.Protocol;

public class DataTypesTests
{
    [Fact]
    public void WindowInfo_Serialization_RoundTrips()
    {
        var win = new WindowInfo
        {
            Hwnd = "0x1234",
            Pid = 9876,
            ProcessName = "notepad.exe",
            Title = "Untitled - Notepad",
            ClassName = "Notepad",
            Visible = true,
            Cloaked = false,
            Minimized = false,
            Foreground = true,
            Rect = new Rect { X = 100, Y = 200, W = 800, H = 600 }
        };

        var json = JsonSerializer.Serialize(win, JsonConfig.Options);
        var deserialized = JsonSerializer.Deserialize<WindowInfo>(json, JsonConfig.Options);

        Assert.NotNull(deserialized);
        Assert.Equal("0x1234", deserialized!.Hwnd);
        Assert.Equal(9876, deserialized.Pid);
        Assert.Equal(800, deserialized.Rect.W);
    }

    [Fact]
    public void ObservationGuard_Serialization_RoundTrips()
    {
        var guard = new ObservationGuard
        {
            Hwnd = "0xABCD",
            Pid = 1234,
            ProcessStartTime = "2026-07-20T12:00:00.0000000Z",
            ForegroundHwnd = "0xABCD",
            WindowRect = new Rect { X = 0, Y = 0, W = 1920, H = 1080 },
            RootRuntimeId = "42.1",
            CapturedAt = "2026-07-20T12:00:00.1000000Z"
        };

        var json = JsonSerializer.Serialize(guard, JsonConfig.Options);
        var deserialized = JsonSerializer.Deserialize<ObservationGuard>(json, JsonConfig.Options);

        Assert.NotNull(deserialized);
        Assert.Equal("0xABCD", deserialized!.Hwnd);
        Assert.Equal(1234, deserialized.Pid);
        Assert.Equal(1920, deserialized.WindowRect!.W);
    }

    [Fact]
    public void CapabilitiesData_Serialization_ContainsExpectedFields()
    {
        var caps = new CapabilitiesData
        {
            WindowsVersion = "10.0.22631",
            DpiAwareness = "per-monitor-v2",
            CaptureBackends = new Dictionary<string, bool>
            {
                ["legacy_window_capture"] = true,
                ["desktop_duplication"] = true
            },
            Uia = new UiaCapabilityInfo { Version = 3 },
            Elevated = false,
            InteractiveSession = true
        };

        var json = JsonSerializer.Serialize(caps, JsonConfig.Options);
        Assert.Contains("\"dpi_awareness\":\"per-monitor-v2\"", json);
        Assert.Contains("\"elevated\":false", json);
        Assert.Contains("\"interactive_session\":true", json);
    }

    [Fact]
    public void ScreenshotData_Serialization_IncludesFallbackInfo()
    {
        var ss = new ScreenshotData
        {
            Path = @"C:\Temp\img.png",
            Width = 800,
            Height = 600,
            Rect = new Rect { X = 100, Y = 200, W = 800, H = 600 },
            CaptureBackend = "legacy_gdi",
            ActualMode = "gdi-desktop-crop",
            RequestedMode = "window",
            FallbackUsed = true,
            OcclusionPossible = true
        };

        var json = JsonSerializer.Serialize(ss, JsonConfig.Options);
        Assert.Contains("\"fallback_used\":true", json);
        Assert.Contains("\"occlusion_possible\":true", json);
    }

    [Fact]
    public void InteractionResult_Serialization_IncludesTargetVerificationFields()
    {
        var interaction = new InteractionResult
        {
            TargetHwnd = "0x1234",
            TargetForegroundVerified = true,
            TargetDeliveryVerified = true
        };

        var json = JsonSerializer.Serialize(interaction, JsonConfig.Options);

        Assert.Contains("\"target_hwnd\":\"0x1234\"", json);
        Assert.Contains("\"target_foreground_verified\":true", json);
        Assert.Contains("\"target_delivery_verified\":true", json);
    }
}
