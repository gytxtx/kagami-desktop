using System.Text.Json;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Backends;

/// <summary>
/// Stores observation guard files as JSON on disk.
/// Guards auto-expire after 30 seconds; cleanup runs on each operation.
///
/// Validation order:
///   1. Guard file exists and is within TTL (file creation time + guard.captured_at)
///   2. HWND still exists (IsWindow, not just IsWindowVisible)
///   3. HWND still belongs to the expected PID (GetWindowThreadProcessId)
///   4. Expected PID is still running with the same start time
///   5. Window rect hasn't shifted beyond tolerance
///   6. Foreground window hasn't changed to an unrelated process
/// </summary>
public class TempFileObservationGuardStore : IObservationGuardStore
{
    private static readonly TimeSpan GuardTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum allowed window rect shift in pixels for physical actions.
    /// Physical coordinates are pixel-precise, so we use a tight tolerance.
    /// </summary>
    internal const int PhysicalActionRectTolerance = 5;

    /// <summary>
    /// Maximum allowed window rect shift for semantic/UIA operations.
    /// UIA operations use locators that don't depend on pixel coordinates,
    /// so a larger tolerance is acceptable.
    /// </summary>
    internal const int SemanticActionRectTolerance = 20;

    public Task<string> SaveAsync(ObservationGuard guard, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CleanupExpiredAsync(ct).GetAwaiter().GetResult();

        var path = TempFileManager.GetGuardPath();
        var json = JsonSerializer.Serialize(guard, JsonConfig.Options);
        File.WriteAllText(path, json);

        return Task.FromResult(path);
    }

    public Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return LoadAndValidateAsync(guardPath, PhysicalActionRectTolerance, ct);
    }

    /// <summary>
    /// Load and validate a guard with a custom rect tolerance (e.g. tighter for physical clicks).
    /// </summary>
    public Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, int rectTolerance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(guardPath))
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.StaleObservation,
                FailureMessage = "Guard file not found. It may have expired or been deleted."
            });
        }

        // Validate guard path is under expected directory (path traversal prevention)
        var expectedDir = TempFileManager.GetGuardDirectory();
        var fullPath = Path.GetFullPath(guardPath);
        if (!fullPath.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.InvalidArgument,
                FailureMessage = "Guard file path is outside the expected guard directory."
            });
        }

        // Check TTL using both file creation time AND guard.captured_at
        var fileCreated = File.GetCreationTimeUtc(guardPath);
        var now = DateTime.UtcNow;
        ObservationGuard guard;

        try
        {
            var json = File.ReadAllText(guardPath);
            guard = JsonSerializer.Deserialize<ObservationGuard>(json, JsonConfig.Options)
                    ?? throw new InvalidOperationException("Failed to deserialize guard.");
        }
        catch
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.StaleObservation,
                FailureMessage = "Guard file corrupted."
            });
        }

        // Validate captured_at timestamp
        if (DateTime.TryParse(guard.CapturedAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var capturedAt))
        {
            if (now - capturedAt > GuardTtl)
            {
                TryDeleteGuard(guardPath);
                return Task.FromResult(new GuardValidationResult
                {
                    Valid = false,
                    FailureCode = ErrorCodes.StaleObservation,
                    FailureMessage = $"Observation is stale: captured {capturedAt:O}, now {now:O} (>30s TTL)."
                });
            }
        }

        // Fallback to file creation time TTL
        if (now - fileCreated > GuardTtl)
        {
            TryDeleteGuard(guardPath);
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.StaleObservation,
                FailureMessage = "Guard file expired (30s TTL)."
            });
        }

        // ── Step 1: HWND still exists as a window ──
        var hwnd = UiaAutomationBackend.ParseHwnd(guard.Hwnd);
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.WindowDestroyed,
                FailureMessage = $"Window {guard.Hwnd} no longer exists."
            });
        }

        // Check if window is minimized (restoreable but useless for input)
        if (NativeMethods.IsIconic(hwnd))
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.WindowMinimized,
                FailureMessage = $"Window {guard.Hwnd} is minimized. Restore before operating."
            });
        }

        // ── Step 2: HWND still belongs to the expected PID ──
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint currentPid);
        if ((int)currentPid != guard.Pid)
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.StaleObservation,
                FailureMessage = $"HWND {guard.Hwnd} now belongs to PID {currentPid}, " +
                    $"expected PID {guard.Pid}. Window may have been recreated."
            });
        }

        // ── Step 3: Verify process start time ──
        var actualStartTime = ProcessHelper.GetProcessStartTime(guard.Pid);
        if (actualStartTime is null || actualStartTime != guard.ProcessStartTime)
        {
            return Task.FromResult(new GuardValidationResult
            {
                Valid = false,
                FailureCode = ErrorCodes.StaleObservation,
                FailureMessage = $"Process {guard.Pid} has restarted or exited since observation."
            });
        }

        // ── Step 4: Validate window rect stability ──
        var currentRect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);
        if (guard.WindowRect is not null)
        {
            int dx = Math.Abs(currentRect.X - guard.WindowRect.X);
            int dy = Math.Abs(currentRect.Y - guard.WindowRect.Y);
            int dw = Math.Abs(currentRect.W - guard.WindowRect.W);
            int dh = Math.Abs(currentRect.H - guard.WindowRect.H);

            if (dx > rectTolerance || dy > rectTolerance || dw > rectTolerance || dh > rectTolerance)
            {
                return Task.FromResult(new GuardValidationResult
                {
                    Valid = false,
                    FailureCode = ErrorCodes.StaleObservation,
                    FailureMessage = $"Window rect changed significantly since observation " +
                        $"(dx={dx}, dy={dy}, dw={dw}, dh={dh}, tolerance={rectTolerance})."
                });
            }
        }

        // ── Step 5: Validate foreground window ──
        var currentForeground = NativeMethods.GetForegroundWindow();
        var expectedForeground = UiaAutomationBackend.ParseHwnd(guard.ForegroundHwnd);

        if (currentForeground != expectedForeground && expectedForeground != IntPtr.Zero)
        {
            // Check if current foreground is still in the same process
            // (a child dialog or popup of the target is acceptable)
            if (currentForeground != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(currentForeground, out uint fgPid);
                if ((int)fgPid != guard.Pid)
                {
                    // Foreground changed to a different process
                    return Task.FromResult(new GuardValidationResult
                    {
                        Valid = false,
                        FailureCode = ErrorCodes.ForegroundActivationDenied,
                        FailureMessage = $"Foreground window changed to a different process " +
                            $"(PID {fgPid}, expected guard PID {guard.Pid}). " +
                            "Physical input may go to the wrong application."
                    });
                }
                // Same process — allowed (e.g. child dialog gained focus)
            }
        }

        return Task.FromResult(new GuardValidationResult { Valid = true, Guard = guard });
    }

    public Task CleanupExpiredAsync(CancellationToken ct)
    {
        TempFileManager.CleanupExpired();
        return Task.CompletedTask;
    }

    private static void TryDeleteGuard(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
