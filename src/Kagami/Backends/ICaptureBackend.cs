namespace Kagami.Backends;

/// <summary>
/// Captures screenshots from windows, desktop regions, or full displays.
/// Each backend has different semantics — see ADR 0002.
/// </summary>
public enum CaptureMode
{
    Window,
    VisibleDesktop,
    Auto
}

/// <summary>
/// The specific capture method used within a backend.
/// Reported to the caller for transparency about what technique was actually used.
/// </summary>
public enum CaptureMethod
{
    /// <summary>PrintWindow with PW_RENDERFULLCONTENT</summary>
    PrintWindow,
    /// <summary>DWM thumbnail compositor capture</summary>
    DwmThumbnail,
    /// <summary>GDI CopyFromScreen (has occlusion risk)</summary>
    GdiCopyFromScreen,
    /// <summary>DXGI Desktop Duplication (composited framebuffer)</summary>
    DxgiDesktopDuplication,
    /// <summary>Windows.Graphics.Capture (WGC) — true window surface capture</summary>
    WindowsGraphicsCapture,
    /// <summary>Unknown or unspecified method</summary>
    Unknown
}

public class CaptureOptions
{
    public CaptureMode RequestedMode { get; init; } = CaptureMode.Auto;
    public bool AllowSemanticFallback { get; init; }
    public IntPtr? Hwnd { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? DisplayIndex { get; init; }
    public string? OutputPath { get; init; }
}

public class CaptureResult
{
    public string FilePath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public string CaptureBackend { get; init; } = "";
    public CaptureMethod CaptureMethod { get; init; } = CaptureMethod.Unknown;
    public string ActualMode { get; init; } = "";
    public string RequestedMode { get; init; } = "";
    public bool FallbackUsed { get; init; }
    public bool OcclusionPossible { get; init; }
    public List<string> Warnings { get; init; } = new();

    public CaptureResult WithFallback(string actualMode, string captureBackend, CaptureMethod captureMethod)
    {
        return new CaptureResult
        {
            FilePath = FilePath,
            Width = Width,
            Height = Height,
            X = X,
            Y = Y,
            CaptureBackend = captureBackend,
            CaptureMethod = captureMethod,
            ActualMode = actualMode,
            RequestedMode = RequestedMode,
            FallbackUsed = true,
            OcclusionPossible = true,
            Warnings = Warnings
        };
    }
}

public interface ICaptureBackend
{
    /// <summary>Human-readable backend name, e.g. "windows_graphics_capture".</summary>
    string Name { get; }

    /// <summary>Whether this backend is available on the current system.</summary>
    bool IsAvailable { get; }

    /// <summary>Try to capture. Returns null if this backend cannot service the request at all.</summary>
    Task<CaptureResult?> CaptureAsync(CaptureOptions options, CancellationToken ct);
}
