using System.Text.Json.Serialization;

namespace Kagami.Protocol;

public class ObservationData
{
    [JsonPropertyName("observation_id")]
    public string ObservationId { get; init; } = "";

    [JsonPropertyName("guard_path")]
    public string GuardPath { get; init; } = "";

    [JsonPropertyName("started_at")]
    public string StartedAt { get; init; } = "";

    [JsonPropertyName("screenshot_at")]
    public string? ScreenshotAt { get; init; }

    [JsonPropertyName("uia_completed_at")]
    public string? UiaCompletedAt { get; init; }

    [JsonPropertyName("window_rect_before")]
    public DetailedRect? WindowRectBefore { get; init; }

    [JsonPropertyName("window_rect_after")]
    public DetailedRect? WindowRectAfter { get; init; }

    [JsonPropertyName("stable")]
    public bool Stable { get; init; }

    [JsonPropertyName("instability_reasons")]
    public List<string> InstabilityReasons { get; init; } = new();

    [JsonPropertyName("screenshot")]
    public ScreenshotData? Screenshot { get; init; }

    [JsonPropertyName("window")]
    public WindowInfo? Window { get; init; }

    [JsonPropertyName("tree")]
    public TreeNode? Tree { get; init; }

    [JsonPropertyName("foreground_hwnd")]
    public string? ForegroundHwnd { get; init; }

    [JsonPropertyName("cursor")]
    public Point? Cursor { get; init; }
}

public class ScreenshotData
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("rect")]
    public Rect Rect { get; init; } = new();

    [JsonPropertyName("capture_backend")]
    public string CaptureBackend { get; init; } = "";

    [JsonPropertyName("actual_mode")]
    public string ActualMode { get; init; } = "";

    [JsonPropertyName("requested_mode")]
    public string RequestedMode { get; init; } = "";

    [JsonPropertyName("fallback_used")]
    public bool FallbackUsed { get; init; }

    [JsonPropertyName("occlusion_possible")]
    public bool OcclusionPossible { get; init; }
}

public class InteractionResult
{
    [JsonPropertyName("mode_requested")]
    public string ModeRequested { get; init; } = "";

    [JsonPropertyName("mode_actual")]
    public string ModeActual { get; init; } = "";

    [JsonPropertyName("physical_input_generated")]
    public bool PhysicalInputGenerated { get; init; }

    [JsonPropertyName("target_hwnd")]
    public string? TargetHwnd { get; init; }

    [JsonPropertyName("target_foreground_verified")]
    public bool TargetForegroundVerified { get; init; }

    [JsonPropertyName("target_delivery_verified")]
    public bool TargetDeliveryVerified { get; init; }
}

public class CapabilitiesData
{
    [JsonPropertyName("windows_version")]
    public string WindowsVersion { get; init; } = "";

    [JsonPropertyName("dpi_awareness")]
    public string DpiAwareness { get; init; } = "";

    [JsonPropertyName("capture_backends")]
    public Dictionary<string, bool> CaptureBackends { get; init; } = new();

    [JsonPropertyName("uia")]
    public UiaCapabilityInfo Uia { get; init; } = new();

    [JsonPropertyName("elevated")]
    public bool Elevated { get; init; }

    [JsonPropertyName("interactive_session")]
    public bool InteractiveSession { get; init; }
}

public class UiaCapabilityInfo
{
    [JsonPropertyName("version")]
    public int Version { get; init; }
}

public class DisplayInfo
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("primary")]
    public bool Primary { get; init; }

    [JsonPropertyName("rect")]
    public Rect Rect { get; init; } = new();

    [JsonPropertyName("dpi_scale")]
    public double DpiScale { get; init; }
}

public class ObservationGuard
{
    [JsonPropertyName("hwnd")]
    public string Hwnd { get; init; } = "";

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("process_start_time")]
    public string ProcessStartTime { get; init; } = "";

    [JsonPropertyName("foreground_hwnd")]
    public string ForegroundHwnd { get; init; } = "";

    [JsonPropertyName("window_rect")]
    public Rect? WindowRect { get; init; }

    [JsonPropertyName("root_runtime_id")]
    public string RootRuntimeId { get; init; } = "";

    [JsonPropertyName("captured_at")]
    public string CapturedAt { get; init; } = "";
}

public class GuardValidationResult
{
    public bool Valid { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public ObservationGuard? Guard { get; init; }
}
