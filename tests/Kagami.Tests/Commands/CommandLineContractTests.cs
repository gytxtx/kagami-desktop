using System.Text.Json;
using Kagami.Backends;
using Kagami.Commands;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Commands;

[Collection(InteractionCommandConsoleCollection.Name)]
public class CommandLineContractTests
{
    [Theory]
    [InlineData("wait-for", "element", "--locator", "{}")]
    [InlineData("wait-for", "--condition", "element", "--locator", "{}")]
    [InlineData("wait-for", "element", "--condition", "element", "--locator", "{}")]
    public async Task WaitFor_AcceptsBothConditionForms(params string[] args)
    {
        var result = await InvokeCli(args);

        Assert.Equal(0, result.ExitCode);
        using var response = JsonDocument.Parse(result.Stdout);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task WaitFor_ConflictingConditionForms_ReturnsInvalidArgument()
    {
        var result = await InvokeCli(
            "wait-for", "element", "--condition", "window", "--locator", "{}");

        Assert.Equal(1, result.ExitCode);
        using var response = JsonDocument.Parse(result.Stdout);
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("wait-for", "element", "unexpected-token")]
    [InlineData("wait-for", "element", "--unknown-option")]
    [InlineData("wait-for", "element", "--locator")]
    public async Task ParseError_WritesSinglePlainJsonResponse(params string[] args)
    {
        var result = await InvokeCli(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(-1, result.Stdout.IndexOf('\u001b'));
        Assert.Single(result.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));

        using var response = JsonDocument.Parse(result.Stdout);
        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("parse", response.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.NotEmpty(result.Stderr);
    }

    [Fact]
    public void WindowInfoReader_ReadsEveryFieldFromOneSnapshot()
    {
        var hwnd = (IntPtr)0x1234;
        var rect = new Rect { X = 10, Y = 20, W = 800, H = 600 };
        var source = new StubWindowInfoSource(rect);
        var reader = new WindowInfoReader(source);

        var info = reader.Read(hwnd, foregroundHwnd: hwnd);

        Assert.Equal(UiaAutomationBackend.FormatHwnd(hwnd), info.Hwnd);
        Assert.Equal(42, info.Pid);
        Assert.Equal("sample.exe", info.ProcessName);
        Assert.Equal("Sample window", info.Title);
        Assert.Equal("SampleClass", info.ClassName);
        Assert.True(info.Visible);
        Assert.True(info.Cloaked);
        Assert.True(info.Minimized);
        Assert.True(info.Foreground);
        Assert.Equal((rect.X, rect.Y, rect.W, rect.H),
            (info.Rect.X, info.Rect.Y, info.Rect.W, info.Rect.H));
        Assert.Equal(1, source.RectReadCount);
    }

    [Fact]
    public async Task ListWindows_UsesOneForegroundSnapshotForEveryWindow()
    {
        var reader = new RecordingWindowInfoReader();
        using var automation = new UiaAutomationBackend(new TempFileObservationGuardStore(), reader);

        var windows = await automation.ListWindowsAsync(false, null, null, CancellationToken.None);

        Assert.NotEmpty(windows);
        Assert.Equal(reader.Returned, windows);
        Assert.All(reader.Calls, call => Assert.Null(call.ExplicitRect));
        var foreground = reader.Calls[0].ForegroundHwnd;
        Assert.All(reader.Calls, call => Assert.Equal(foreground, call.ForegroundHwnd));
    }

    [Fact]
    public async Task Observe_UsesOneRectAndForegroundSnapshotAcrossResponse()
    {
        var reader = new RecordingWindowInfoReader();
        var backendGuardStore = new TempFileObservationGuardStore();
        using var automation = new UiaAutomationBackend(backendGuardStore, reader);

        await automation.ListWindowsAsync(false, null, null, CancellationToken.None);
        var hwnd = reader.Calls
            .Select(call => call.Hwnd)
            .First(candidate =>
                NativeMethods.IsWindow(candidate) &&
                NativeMethods.IsWindowVisible(candidate) &&
                !NativeMethods.IsIconic(candidate));
        reader.Clear();

        var capture = new CaptureService(new[] { new StubCaptureBackend() });
        var command = new ObserveCommand(
            automation,
            capture,
            new StubObservationGuardStore(),
            reader);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            UiaAutomationBackend.FormatHwnd(hwnd),
            depth: 0,
            maxNodes: 1,
            view: "control",
            interactiveOnly: false,
            includeLocators: "none",
            captureMode: "window",
            allowSemanticFallback: false,
            outputPath: null));

        Assert.Equal(0, exitCode);
        var call = Assert.Single(reader.Calls);
        Assert.NotNull(call.ExplicitRect);

        using var response = JsonDocument.Parse(output);
        var data = response.RootElement.GetProperty("data");
        var rectAfter = data.GetProperty("window_rect_after");
        var window = data.GetProperty("window");
        var windowRect = window.GetProperty("rect");

        Assert.Equal(rectAfter.GetProperty("left").GetInt32(), windowRect.GetProperty("x").GetInt32());
        Assert.Equal(rectAfter.GetProperty("top").GetInt32(), windowRect.GetProperty("y").GetInt32());
        Assert.Equal(
            rectAfter.GetProperty("right").GetInt32() - rectAfter.GetProperty("left").GetInt32(),
            windowRect.GetProperty("w").GetInt32());
        Assert.Equal(
            rectAfter.GetProperty("bottom").GetInt32() - rectAfter.GetProperty("top").GetInt32(),
            windowRect.GetProperty("h").GetInt32());

        var foregroundHwnd = data.GetProperty("foreground_hwnd").GetString();
        Assert.Equal(UiaAutomationBackend.FormatHwnd(call.ForegroundHwnd), foregroundHwnd);
        Assert.Equal(
            string.Equals(window.GetProperty("hwnd").GetString(), foregroundHwnd, StringComparison.OrdinalIgnoreCase),
            window.GetProperty("foreground").GetBoolean());
    }

    private static async Task<CliResult> InvokeCli(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var rootCommand = Kagami.Program.BuildRootCommand(
                new StubAutomationBackend(),
                new StubInputBackend(),
                new CaptureService(),
                new StubObservationGuardStore());
            var exitCode = await Kagami.Program.InvokeAsync(rootCommand, args);
            return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static async Task<(int ExitCode, string Output)> CaptureOutputAsync(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        try
        {
            using var output = new StringWriter();
            Console.SetOut(output);
            var exitCode = await action();
            return (exitCode, output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    private sealed class StubAutomationBackend : IAutomationBackend
    {
        public Task<List<WindowInfo>> ListWindowsAsync(
            bool visibleOnly,
            string? processName,
            string? title,
            CancellationToken ct) => Task.FromResult(new List<WindowInfo>());

        public Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct) =>
            Task.FromResult<TreeNode?>(new TreeNode());

        public Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct) =>
            Task.FromResult(new List<TreeNode>());

        public Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct) =>
            Task.FromResult<LocatorResolution?>(new LocatorResolution
            {
                Node = new TreeNode(),
                ResolutionMethod = "test"
            });

        public Task<bool> FocusAsync(Locator locator, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class StubInputBackend : IInputBackend
    {
        public Task<InvokeResult> InvokeAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ClickResult> ClickAsync(
            IntPtr targetHwnd,
            int x,
            int y,
            bool rightButton,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<TypeTextResult> TypeTextAsync(TypeTextOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<KeyResult> KeyAsync(KeyOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ActivateResult> ActivateWindowAsync(IntPtr hwnd, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StubObservationGuardStore : IObservationGuardStore
    {
        public Task<string> SaveAsync(ObservationGuard guard, CancellationToken ct) =>
            Task.FromResult("test.guard");

        public Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CleanupExpiredAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubWindowInfoSource(Rect rect) : IWindowInfoSource
    {
        public int RectReadCount { get; private set; }

        public int GetProcessId(IntPtr hwnd) => 42;
        public string? GetProcessName(int pid) => "sample.exe";
        public string GetWindowTitle(IntPtr hwnd) => "Sample window";
        public string GetClassName(IntPtr hwnd) => "SampleClass";
        public bool IsVisible(IntPtr hwnd) => true;
        public bool IsCloaked(IntPtr hwnd) => true;
        public bool IsMinimized(IntPtr hwnd) => true;

        public Rect GetExtendedFrameBounds(IntPtr hwnd)
        {
            RectReadCount++;
            return rect;
        }
    }

    private sealed class RecordingWindowInfoReader : IWindowInfoReader
    {
        public List<WindowInfoReadCall> Calls { get; } = new();
        public List<WindowInfo> Returned { get; } = new();

        public WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd) =>
            Record(hwnd, foregroundHwnd, explicitRect: null);

        public WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd, Rect rect) =>
            Record(hwnd, foregroundHwnd, rect);

        public void Clear()
        {
            Calls.Clear();
            Returned.Clear();
        }

        private WindowInfo Record(IntPtr hwnd, IntPtr foregroundHwnd, Rect? explicitRect)
        {
            var call = new WindowInfoReadCall(hwnd, foregroundHwnd, explicitRect);
            Calls.Add(call);

            var result = new WindowInfo
            {
                Hwnd = UiaAutomationBackend.FormatHwnd(hwnd),
                Pid = 42,
                ProcessName = "sample.exe",
                Title = "Sample window",
                ClassName = "SampleClass",
                Visible = true,
                Cloaked = true,
                Minimized = false,
                Foreground = hwnd == foregroundHwnd,
                Rect = explicitRect ?? new Rect { X = 1, Y = 2, W = 3, H = 4 }
            };
            Returned.Add(result);
            return result;
        }
    }

    private sealed record WindowInfoReadCall(
        IntPtr Hwnd,
        IntPtr ForegroundHwnd,
        Rect? ExplicitRect);

    private sealed class StubCaptureBackend : ICaptureBackend
    {
        public string Name => "legacy_window_capture";
        public bool IsAvailable => true;

        public Task<CaptureResult?> CaptureAsync(CaptureOptions options, CancellationToken ct) =>
            Task.FromResult<CaptureResult?>(new CaptureResult
            {
                FilePath = "test.png",
                Width = 100,
                Height = 80,
                X = 10,
                Y = 20,
                CaptureBackend = Name,
                CaptureMethod = CaptureMethod.PrintWindow,
                ActualMode = "window",
                RequestedMode = "window"
            });
    }
}
