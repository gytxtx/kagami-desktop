using System.Runtime.InteropServices;
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
    public void LoadAndValidate_WithSamePrefixSiblingDirectory_Fails()
    {
        var expectedDirectory = Kagami.Utilities.TempFileManager.GetGuardDirectory();
        var siblingDirectory = expectedDirectory + "_evil";
        Directory.CreateDirectory(siblingDirectory);
        var path = Path.Combine(siblingDirectory, $"guard-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");

        try
        {
            var result = _store.LoadAndValidateAsync(path, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.False(result.Valid);
            Assert.Equal(ErrorCodes.InvalidArgument, result.FailureCode);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(siblingDirectory, recursive: false);
        }
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

    [Fact]
    public void LoadAndValidate_AfterThirtyOneSeconds_RemainsValid()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new TempFileObservationGuardStore(timeProvider);
        using var window = NativeTestWindow.Create();
        var path = store.SaveAsync(
            CreateValidGuard(window.Handle, timeProvider.GetUtcNow()),
            CancellationToken.None).GetAwaiter().GetResult();

        try
        {
            timeProvider.Advance(TimeSpan.FromSeconds(31));

            var result = store.LoadAndValidateAsync(path, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.True(result.Valid, result.FailureMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadAndValidate_AfterOneHundredTwentyOneSeconds_ExpiresWithConfiguredTtl()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new TempFileObservationGuardStore(timeProvider);
        using var window = NativeTestWindow.Create();
        var path = store.SaveAsync(
            CreateValidGuard(window.Handle, timeProvider.GetUtcNow()),
            CancellationToken.None).GetAwaiter().GetResult();

        timeProvider.Advance(TimeSpan.FromSeconds(121));

        var result = store.LoadAndValidateAsync(path, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.Valid);
        Assert.Equal(ErrorCodes.StaleObservation, result.FailureCode);
        Assert.Contains("120s TTL", result.FailureMessage);
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

    private static ObservationGuard CreateValidGuard(IntPtr hwnd, DateTimeOffset capturedAt)
    {
        var pid = Environment.ProcessId;
        var rect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);

        return new ObservationGuard
        {
            Hwnd = UiaAutomationBackend.FormatHwnd(hwnd),
            Pid = pid,
            ProcessStartTime = Kagami.Utilities.ProcessHelper.GetProcessStartTime(pid)!,
            ForegroundHwnd = "0x0",
            WindowRect = rect,
            RootRuntimeId = "1.2",
            CapturedAt = capturedAt.UtcDateTime.ToString("O")
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }

    private sealed class NativeTestWindow : IDisposable
    {
        private NativeTestWindow(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public static NativeTestWindow Create()
        {
            var handle = CreateWindowEx(
                0,
                "STATIC",
                "Kagami guard TTL test",
                0,
                0,
                0,
                100,
                100,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            Assert.NotEqual(IntPtr.Zero, handle);
            return new NativeTestWindow(handle);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyWindow(Handle);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint exStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }
}
