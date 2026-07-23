using Kagami.Protocol;

namespace Kagami.Backends;

/// <summary>
/// Orchestrates capture backends in priority order.
/// Design (ADR 0002):
///   window → uses window-capture backends (legacy_window_capture)
///   visible-desktop → uses DXGI desktop duplication
///   auto + hwnd → means window capture intent; cross-semantic fallback is blocked by default
///   auto without hwnd → means desktop capture intent
/// </summary>
public class CaptureService
{
    private readonly List<ICaptureBackend> _backends;

    public CaptureService()
        : this(new ICaptureBackend[]
        {
            new LegacyWindowCaptureBackend(),
            new DesktopDuplicationBackend(),
            new LegacyCaptureBackend()
        })
    {
    }

    internal CaptureService(IEnumerable<ICaptureBackend> backends)
    {
        _backends = backends.ToList();
    }

    public List<string> AvailableBackendNames =>
        _backends.Where(b => b.IsAvailable).Select(b => b.Name).ToList();

    public Dictionary<string, bool> GetBackendAvailability() =>
        _backends.ToDictionary(b => b.Name, b => b.IsAvailable);

    public async Task<CaptureResult> CaptureAsync(CaptureOptions options, CancellationToken ct)
    {
        var warnings = new List<string>();

        // Determine the effective intent: when hwnd is given with Auto, treat as Window intent
        bool hasWindowIntent = options.RequestedMode == CaptureMode.Window
            || (options.RequestedMode == CaptureMode.Auto && options.Hwnd.HasValue);

        bool hasDesktopIntent = options.RequestedMode == CaptureMode.VisibleDesktop
            || (options.RequestedMode == CaptureMode.Auto && !options.Hwnd.HasValue);

        CaptureResult? result = null;

        foreach (var backend in _backends)
        {
            if (!backend.IsAvailable) continue;

            // Cross-semantic fallback guard:
            // If caller wants a window capture (hwnd given), only use window backends
            // unless semantic fallback is explicitly allowed.
            if (hasWindowIntent)
            {
                bool isWindowBackend = backend.Name switch
                {
                    "legacy_window_capture" => true,
                    // "windows_graphics_capture" → true (when WGC is implemented)
                    _ => false
                };

                if (!isWindowBackend && !options.AllowSemanticFallback)
                    continue;
            }

            // If caller wants desktop capture, skip the window-only backends
            if (hasDesktopIntent && backend.Name == "legacy_window_capture")
                continue;

            result = await backend.CaptureAsync(options, ct);
            if (result is not null)
            {
                // Detect cross-semantic fallback: a window intent served by a
                // non-window backend
                bool crossSemanticFallback = hasWindowIntent && backend.Name != "legacy_window_capture";

                if (result.FallbackUsed || crossSemanticFallback)
                {
                    warnings.Add(crossSemanticFallback
                        ? $"Cross-semantic fallback: window intent serviced by {result.CaptureBackend} ({result.ActualMode}). Occlusion possible."
                        : $"Fell back from {options.RequestedMode} to {result.ActualMode} via {result.CaptureBackend}.");
                }

                // Compute FallbackUsed and OcclusionPossible at the service level
                // (backends report their own perspective; the service resolves intent)
                bool actualFallback = result.FallbackUsed || crossSemanticFallback;
                bool occlusionPossible = result.OcclusionPossible || crossSemanticFallback;

                result = new CaptureResult
                {
                    FilePath = result.FilePath,
                    Width = result.Width,
                    Height = result.Height,
                    X = result.X,
                    Y = result.Y,
                    CaptureBackend = result.CaptureBackend,
                    CaptureMethod = result.CaptureMethod,
                    ActualMode = result.ActualMode,
                    RequestedMode = result.RequestedMode,
                    FallbackUsed = actualFallback,
                    OcclusionPossible = occlusionPossible,
                    Warnings = warnings
                };
                break;
            }
        }

        if (result is null)
        {
            throw new CommandException(
                ErrorCodes.CaptureFailed,
                "All capture backends failed. The window may be minimized, on a different desktop, or inaccessible.",
                exitCode: 1);
        }

        return result;
    }
}
