using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class ScreenshotCommand
{
    private readonly CaptureService _capture;

    public ScreenshotCommand(CaptureService capture)
    {
        _capture = capture;
    }

    public async Task<int> RunAsync(
        string? hwndStr,
        int? x, int? y, int? w, int? h,
        int? displayIndex,
        string captureMode,
        bool allowSemanticFallback,
        string? outputPath)
    {
        var writer = new ResponseWriter("screenshot");

        try
        {
            var mode = captureMode switch
            {
                "window" => CaptureMode.Window,
                "visible-desktop" => CaptureMode.VisibleDesktop,
                _ => CaptureMode.Auto
            };

            IntPtr? hwnd = null;
            if (hwndStr is not null)
                hwnd = HwndHelper.ParseExisting(hwndStr);

            var options = new CaptureOptions
            {
                RequestedMode = mode,
                AllowSemanticFallback = allowSemanticFallback,
                Hwnd = hwnd,
                X = x,
                Y = y,
                Width = w,
                Height = h,
                DisplayIndex = displayIndex,
                OutputPath = outputPath
            };

            var result = await _capture.CaptureAsync(options, CancellationToken.None);

            var data = new ScreenshotData
            {
                Path = result.FilePath,
                Width = result.Width,
                Height = result.Height,
                Rect = new Rect { X = result.X, Y = result.Y, W = result.Width, H = result.Height },
                CaptureBackend = result.CaptureBackend,
                ActualMode = result.ActualMode,
                RequestedMode = result.RequestedMode,
                FallbackUsed = result.FallbackUsed,
                OcclusionPossible = result.OcclusionPossible
            };

            var warnings = result.Warnings.Select(w => new JsonWarning
            {
                Code = "capture_fallback",
                Message = w
            }).ToList();

            return writer.Success(data, warnings);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode, exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }
}
