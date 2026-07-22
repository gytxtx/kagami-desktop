using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class ObserveCommand
{
    private readonly IAutomationBackend _automation;
    private readonly CaptureService _capture;
    private readonly IObservationGuardStore _guardStore;

    public ObserveCommand(IAutomationBackend automation, CaptureService capture, IObservationGuardStore guardStore)
    {
        _automation = automation;
        _capture = capture;
        _guardStore = guardStore;
    }

    public async Task<int> RunAsync(
        string hwndStr,
        int depth,
        int maxNodes,
        string view,
        bool interactiveOnly,
        string includeLocators,
        string captureMode,
        bool allowSemanticFallback,
        string? outputPath)
    {
        var writer = new ResponseWriter("observe");
        var warnings = new List<JsonWarning>();
        var instabilityReasons = new List<string>();
        var startedAt = DateTime.UtcNow;

        try
        {
            if (!TreeOutputPolicy.IsSupportedLocatorMode(includeLocators))
            {
                return writer.Fail(
                    ErrorCodes.InvalidArgument,
                    "--include-locators must be one of: all, interactive, none.");
            }

            // Step 1: Check window state
            var hwnd = ParseHwnd(hwndStr);
            if (hwnd == IntPtr.Zero)
                return writer.Fail(ErrorCodes.InvalidArgument, $"Invalid HWND: {hwndStr}");

            if (!NativeMethods.IsWindow(hwnd))
                return writer.Fail(ErrorCodes.WindowDestroyed, "Window no longer exists.");

            if (NativeMethods.IsIconic(hwnd))
                return writer.Fail(ErrorCodes.WindowMinimized, "Window is minimized. Restore before observing.");

            if (!NativeMethods.IsWindowVisible(hwnd))
                return writer.Fail(ErrorCodes.WindowDestroyed, "Window is not visible.");

            // Record window rect before
            var rectBefore = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);
            var detailedBefore = new DetailedRect
            {
                Left = rectBefore.X,
                Top = rectBefore.Y,
                Right = rectBefore.X + rectBefore.W,
                Bottom = rectBefore.Y + rectBefore.H
            };

            // Step 2: Screenshot
            var captureModeEnum = captureMode switch
            {
                "window" => CaptureMode.Window,
                "visible-desktop" => CaptureMode.VisibleDesktop,
                _ => CaptureMode.Auto
            };

            var captureResult = await _capture.CaptureAsync(new CaptureOptions
            {
                RequestedMode = captureModeEnum,
                AllowSemanticFallback = allowSemanticFallback,
                Hwnd = hwnd,
                OutputPath = outputPath
            }, CancellationToken.None);

            if (captureResult.Warnings.Count > 0)
            {
                warnings.AddRange(captureResult.Warnings.Select(w =>
                    new JsonWarning { Code = "capture_fallback", Message = w }));
            }

            var screenshotAt = DateTime.UtcNow;

            // Step 3: UIA tree
            var treeOptions = new GetTreeOptions
            {
                Hwnd = hwnd,
                MaxDepth = depth,
                MaxNodes = maxNodes,
                View = view,
                InteractiveOnly = interactiveOnly,
                IncludeLocators = includeLocators
            };

            TreeNode? tree = null;
            string? uiaCompletedAt = null;

            try
            {
                tree = await _automation.GetTreeAsync(treeOptions, CancellationToken.None);
                uiaCompletedAt = DateTime.UtcNow.ToString("O");
            }
            catch (Exception ex)
            {
                warnings.Add(new JsonWarning
                {
                    Code = "uia_tree_partial",
                    Message = $"UIA tree capture failed: {ex.Message}"
                });
            }

            // Step 4: Record window rect after
            var rectAfter = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);
            var detailedAfter = new DetailedRect
            {
                Left = rectAfter.X,
                Top = rectAfter.Y,
                Right = rectAfter.X + rectAfter.W,
                Bottom = rectAfter.Y + rectAfter.H
            };

            // Step 5: Detect instability
            bool rectStable = rectBefore.X == rectAfter.X && rectBefore.Y == rectAfter.Y &&
                              rectBefore.W == rectAfter.W && rectBefore.H == rectAfter.H;

            if (!rectStable)
                instabilityReasons.Add("WINDOW_RECT_CHANGED");

            bool stable = instabilityReasons.Count == 0;

            // Step 6: Build guard
            var guard = ((UiaAutomationBackend)_automation).BuildGuard(hwnd);
            string guardPath = guard is not null
                ? await _guardStore.SaveAsync(guard, CancellationToken.None)
                : "";

            // Step 7: Cursor position
            NativeMethods.GetCursorPos(out var cursorPt);
            var cursor = new Point { X = cursorPt.X, Y = cursorPt.Y };

            // Step 8: Foreground window
            var foregroundHwnd = NativeMethods.GetForegroundWindow();

            // Step 9: Window info
            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            var procName = ProcessHelper.GetProcessName((int)pid) ?? "";

            var windowInfo = new WindowInfo
            {
                Hwnd = UiaAutomationBackend.FormatHwnd(hwnd),
                Pid = (int)pid,
                ProcessName = procName,
                Title = "", // Filled from tree root
                ClassName = "",
                Visible = true,
                Cloaked = false,
                Minimized = false,
                Foreground = hwnd == foregroundHwnd,
                Rect = rectAfter
            };

            var observationData = new ObservationData
            {
                ObservationId = Guid.NewGuid().ToString(),
                GuardPath = guardPath,
                StartedAt = startedAt.ToString("O"),
                ScreenshotAt = screenshotAt.ToString("O"),
                UiaCompletedAt = uiaCompletedAt,
                WindowRectBefore = detailedBefore,
                WindowRectAfter = detailedAfter,
                Stable = stable,
                InstabilityReasons = instabilityReasons,
                Screenshot = new ScreenshotData
                {
                    Path = captureResult.FilePath,
                    Width = captureResult.Width,
                    Height = captureResult.Height,
                    Rect = new Rect
                    {
                        X = captureResult.X,
                        Y = captureResult.Y,
                        W = captureResult.Width,
                        H = captureResult.Height
                    },
                    CaptureBackend = captureResult.CaptureBackend,
                    ActualMode = captureResult.ActualMode,
                    RequestedMode = captureResult.RequestedMode,
                    FallbackUsed = captureResult.FallbackUsed,
                    OcclusionPossible = captureResult.OcclusionPossible
                },
                Window = windowInfo,
                Tree = tree,
                ForegroundHwnd = UiaAutomationBackend.FormatHwnd(foregroundHwnd),
                Cursor = cursor
            };

            return writer.Success(observationData, warnings);
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

    private static IntPtr ParseHwnd(string hwndStr)
    {
        if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hwndStr = hwndStr[2..];

        if (long.TryParse(hwndStr, System.Globalization.NumberStyles.HexNumber, null, out long val))
            return (IntPtr)val;

        return IntPtr.Zero;
    }
}
