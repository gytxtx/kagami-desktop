using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class TempFileObservationGuardStoreTests
{
    private readonly TempFileObservationGuardStore _store = new();

    [Fact]
    public void Save_ReturnsValidPath()
    {
        var guard = CreateTestGuard();
        var path = _store.SaveAsync(guard, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"Guard file not created at {path}");
        Assert.Contains("kagami", path);
        Assert.Contains("guards", path);
    }

    [Fact]
    public void Save_FileContent_IsValidJson()
    {
        var guard = CreateTestGuard();
        var path = _store.SaveAsync(guard, CancellationToken.None).GetAwaiter().GetResult();

        var content = File.ReadAllText(path);
        Assert.Contains("\"hwnd\"", content);
        Assert.Contains(guard.Hwnd, content);
        Assert.Contains(guard.ProcessStartTime, content);
    }

    [Fact]
    public void LoadAndValidate_WithNonExistentPath_Fails()
    {
        var result = _store.LoadAndValidateAsync(@"C:\nonexistent\path\guard.json", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.Valid);
        Assert.Equal(ErrorCodes.StaleObservation, result.FailureCode);
    }

    [Fact]
    public void LoadAndValidate_WithCorruptedFile_Fails()
    {
        var path = _store.SaveAsync(CreateTestGuard(), CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllText(path, "not valid json {{{");

        var result = _store.LoadAndValidateAsync(path, CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(result.Valid);
    }

    [Fact]
    public void LoadAndValidate_WithInvalidHwnd_Fails()
    {
        var guard = new ObservationGuard
        {
            Hwnd = "0xDEADBEEF",
            Pid = CreateTestGuard().Pid,
            ProcessStartTime = CreateTestGuard().ProcessStartTime,
            ForegroundHwnd = CreateTestGuard().ForegroundHwnd,
            WindowRect = new Rect { X = 0, Y = 0, W = 1920, H = 1080 },
            RootRuntimeId = "1.2",
            CapturedAt = DateTime.UtcNow.ToString("O")
        };
        var path = _store.SaveAsync(guard, CancellationToken.None).GetAwaiter().GetResult();

        var result = _store.LoadAndValidateAsync(path, CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(result.Valid);
    }

    [Fact]
    public void LoadAndValidate_WithWrongProcessStartTime_Fails()
    {
        var baseGuard = CreateTestGuard();
        var guard = new ObservationGuard
        {
            Hwnd = baseGuard.Hwnd,
            Pid = baseGuard.Pid,
            ProcessStartTime = "2000-01-01T00:00:00.0000000Z",
            ForegroundHwnd = baseGuard.ForegroundHwnd,
            WindowRect = new Rect { X = 0, Y = 0, W = 1920, H = 1080 },
            RootRuntimeId = "1.2",
            CapturedAt = DateTime.UtcNow.ToString("O")
        };
        var path = _store.SaveAsync(guard, CancellationToken.None).GetAwaiter().GetResult();

        var result = _store.LoadAndValidateAsync(path, CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(result.Valid);
        Assert.Equal(ErrorCodes.StaleObservation, result.FailureCode);
    }

    [Fact]
    public void CleanupExpired_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _store.CleanupExpiredAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Null(ex);
    }

    private static ObservationGuard CreateTestGuard()
    {
        var pid = Environment.ProcessId;
        var startTime = Kagami.Utilities.ProcessHelper.GetProcessStartTime(pid) ?? "2026-01-01T00:00:00.0000000Z";
        var fg = Kagami.Utilities.NativeMethods.GetForegroundWindow();

        return new ObservationGuard
        {
            Hwnd = Kagami.Backends.UiaAutomationBackend.FormatHwnd(fg),
            Pid = pid,
            ProcessStartTime = startTime,
            ForegroundHwnd = Kagami.Backends.UiaAutomationBackend.FormatHwnd(fg),
            WindowRect = new Rect { X = 0, Y = 0, W = 1920, H = 1080 },
            RootRuntimeId = "1.2",
            CapturedAt = DateTime.UtcNow.ToString("O")
        };
    }
}
