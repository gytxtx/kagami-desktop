using System.Diagnostics;
using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class WaitForCommand
{
    private readonly IAutomationBackend _automation;
    private readonly CaptureService _capture;

    public WaitForCommand(IAutomationBackend automation, CaptureService capture)
    {
        _automation = automation;
        _capture = capture;
    }

    public async Task<int> RunAsync(
        string condition,
        string? hwndStr,
        string? processName,
        string? title,
        string? locatorJson,
        string? property,
        string? equalsValue,
        int timeoutMs,
        int pollIntervalMs,
        int consecutiveCount,
        double screenshotThreshold,
        string? screenshotRegion,
        string? expectedStatePath)
    {
        var writer = new ResponseWriter("wait-for");

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var ct = cts.Token;
            var sw = Stopwatch.StartNew();

            IntPtr? hwnd = hwndStr is not null ? ParseHwnd(hwndStr) : null;
            Locator? locator = locatorJson is not null
                ? JsonSerializer.Deserialize<Locator>(locatorJson, JsonConfig.Options)
                : null;

            switch (condition)
            {
                case "window":
                    if (processName is null && title is null)
                        return writer.Fail(ErrorCodes.InvalidArgument,
                            "At least one of --process or --title is required for 'window' condition.");
                    await WaitForWindow(processName, title, pollIntervalMs, ct);
                    break;

                case "element":
                    if (locator is null) return writer.Fail(ErrorCodes.InvalidArgument, "--locator required for element condition");
                    await WaitForElement(locator, pollIntervalMs, ct);
                    break;

                case "element-gone":
                    if (locator is null) return writer.Fail(ErrorCodes.InvalidArgument, "--locator required for element-gone condition");
                    await WaitForElementGone(locator, pollIntervalMs, ct);
                    break;

                case "property":
                    if (locator is null) return writer.Fail(ErrorCodes.InvalidArgument, "--locator required for property condition");
                    if (property is null) return writer.Fail(ErrorCodes.InvalidArgument, "--property required for property condition");
                    await WaitForProperty(locator, property, equalsValue, pollIntervalMs, ct);
                    break;

                case "window-rect-stable":
                    if (hwnd is null) return writer.Fail(ErrorCodes.InvalidArgument, "--hwnd required for window-rect-stable condition");
                    await WaitForWindowRectStable(hwnd.Value, consecutiveCount, pollIntervalMs, ct);
                    break;

                case "screenshot-stable":
                    if (hwnd is null) return writer.Fail(ErrorCodes.InvalidArgument, "--hwnd required for screenshot-stable condition");
                    await WaitForScreenshotStable(hwnd.Value, screenshotThreshold, consecutiveCount, screenshotRegion, pollIntervalMs, ct);
                    break;

                default:
                    return writer.Fail(ErrorCodes.InvalidArgument, $"Unknown condition: {condition}. " +
                        "Expected: window, element, element-gone, property, window-rect-stable, screenshot-stable.");
            }

            return writer.Success(new { condition, satisfied = true, elapsed_ms = sw.ElapsedMilliseconds });
        }
        catch (OperationCanceledException)
        {
            return writer.Fail(ErrorCodes.OperationTimeout, $"Condition '{condition}' not satisfied within {timeoutMs}ms.", retryable: true);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    private async Task WaitForWindow(string? processName, string? title, int pollMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var windows = await _automation.ListWindowsAsync(true, null, null, ct);
            var found = windows.Any(w =>
                (processName is null || w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) &&
                (title is null || w.Title.Contains(title, StringComparison.OrdinalIgnoreCase)));

            if (found) return;
            await Task.Delay(pollMs, ct);
        }
    }

    private async Task WaitForElement(Locator locator, int pollMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var resolved = await _automation.ResolveLocatorAsync(locator, ct);
            if (resolved is not null) return;
            await Task.Delay(pollMs, ct);
        }
    }

    private async Task WaitForElementGone(Locator locator, int pollMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var resolved = await _automation.ResolveLocatorAsync(locator, ct);
            if (resolved is null) return;
            await Task.Delay(pollMs, ct);
        }
    }

    private async Task WaitForProperty(Locator locator, string property, string? equalsValue, int pollMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var resolved = await _automation.ResolveLocatorAsync(locator, ct);
            if (resolved is not null)
            {
                var node = resolved.Node;
                var currentValue = property switch
                {
                    "is_enabled" => node.IsEnabled.ToString().ToLowerInvariant(),
                    "is_offscreen" => node.IsOffscreen.ToString().ToLowerInvariant(),
                    "is_keyboard_focusable" => node.IsKeyboardFocusable.ToString().ToLowerInvariant(),
                    "has_keyboard_focus" => node.HasKeyboardFocus.ToString().ToLowerInvariant(),
                    _ => null
                };

                if (currentValue is not null && (equalsValue is null || currentValue == equalsValue.ToLowerInvariant()))
                    return;
            }
            await Task.Delay(pollMs, ct);
        }
    }

    private async Task WaitForWindowRectStable(IntPtr hwnd, int consecutiveCount, int pollMs, CancellationToken ct)
    {
        int stable = 0;
        Rect? lastRect = null;

        while (!ct.IsCancellationRequested)
        {
            var rect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);

            if (lastRect is not null &&
                rect.X == lastRect.X && rect.Y == lastRect.Y &&
                rect.W == lastRect.W && rect.H == lastRect.H)
            {
                stable++;
                if (stable >= consecutiveCount) return;
            }
            else
            {
                stable = 0;
            }

            lastRect = rect;
            await Task.Delay(pollMs, ct);
        }
    }

    private async Task WaitForScreenshotStable(IntPtr hwnd, double threshold, int consecutiveCount,
        string? regionStr, int pollMs, CancellationToken ct)
    {
        int stable = 0;
        ulong? lastHash = null;

        while (!ct.IsCancellationRequested)
        {
            // Parse region
            int rx = 0, ry = 0, rw = 0, rh = 0;
            bool hasRegion = false;
            if (regionStr is not null)
            {
                var parts = regionStr.Split(',');
                if (parts.Length == 4 &&
                    int.TryParse(parts[0], out rx) && int.TryParse(parts[1], out ry) &&
                    int.TryParse(parts[2], out rw) && int.TryParse(parts[3], out rh))
                {
                    hasRegion = true;
                }
            }

            var rect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);
            var opts = new CaptureOptions
            {
                RequestedMode = CaptureMode.Auto,
                AllowSemanticFallback = true,
                X = hasRegion ? rx : rect.X,
                Y = hasRegion ? ry : rect.Y,
                Width = hasRegion ? rw : rect.W,
                Height = hasRegion ? rh : rect.H
            };

            var result = await _capture.CaptureAsync(opts, ct);

            // Compute perceptual hash
            try
            {
                var imageBytes = await File.ReadAllBytesAsync(result.FilePath, ct);
                var hash = SimplePerceptualHash(imageBytes);

                if (lastHash.HasValue && hash == lastHash.Value)
                {
                    stable++;
                    if (stable >= consecutiveCount) return;
                }
                else
                {
                    stable = 0;
                }

                lastHash = hash;
            }
            catch
            {
                // If we can't read the screenshot, skip this round
            }

            await Task.Delay(pollMs, ct);
        }
    }

    private static ulong SimplePerceptualHash(byte[] pngBytes)
    {
        // Simple hash of the PNG file bytes for comparison.
        // For MVP, we hash the compressed data rather than decoding.
        // A true perceptual hash would decode and average pixels.
        ulong hash = 14695981039346656037;
        foreach (var b in pngBytes)
        {
            hash ^= b;
            hash *= 1099511628211;
        }
        return hash;
    }

    private static IntPtr ParseHwnd(string hwndStr)
    {
        if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hwndStr = hwndStr[2..];

        if (long.TryParse(hwndStr, System.Globalization.NumberStyles.HexNumber, null, out long val))
            return (IntPtr)val;

        return IntPtr.Zero;
    }
}
