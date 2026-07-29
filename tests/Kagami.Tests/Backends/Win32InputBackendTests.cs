using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Backends;

public class Win32InputBackendTests
{
    [Fact]
    public void Click_WhenPointerTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var otherWindow = new IntPtr(200);
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = target,
            WindowAtPoint = otherWindow
        };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[otherWindow] = 20;
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.ClickAsync(target, 100, 100, false, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.PointNotInTarget, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void Click_WithValidTarget_ReportsTargetVerification()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.ClickAsync(target, 100, 100, false, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal("0x64", result.Interaction.TargetHwnd);
        Assert.True(result.Interaction.TargetForegroundVerified);
        Assert.True(result.Interaction.TargetDeliveryVerified);
    }

    [Fact]
    public void Move_WithValidTarget_InjectsSingleMoveAndReportsVerification()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.MoveAsync(target, 100, 100, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal([1], injector.InputCounts);
        Assert.Equal(NativeMethods.MOUSEEVENTF_MOVE, injector.Inputs[0][0].u.mi.dwFlags & NativeMethods.MOUSEEVENTF_MOVE);
        Assert.Equal(100, result.X);
        Assert.Equal(100, result.Y);
        Assert.True(result.Interaction.TargetForegroundVerified);
        Assert.True(result.Interaction.TargetDeliveryVerified);
    }

    [Fact]
    public void DoubleClick_WithValidTarget_InjectsFiveEvents()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.DoubleClickAsync(target, 100, 100, true, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal([5], injector.InputCounts);
        Assert.True(result.RightButton);
        Assert.True(result.Interaction.PhysicalInputGenerated);
    }

    [Fact]
    public void Scroll_WithValidTarget_InjectsMoveAndWheel()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.ScrollAsync(target, 100, 100, -2, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal([2], injector.InputCounts);
        Assert.Equal(-2, result.Delta);
        Assert.Equal(-2 * NativeMethods.WHEEL_DELTA, injector.Inputs[0][1].u.mi.mouseData);
        Assert.Equal(NativeMethods.MOUSEEVENTF_WHEEL, injector.Inputs[0][1].u.mi.dwFlags & NativeMethods.MOUSEEVENTF_WHEEL);
    }

    [Fact]
    public void Drag_WithValidTarget_InjectsFourEvents()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.DragAsync(target, 100, 100, 200, 200, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal([4], injector.InputCounts);
        Assert.Equal((100, 100, 200, 200), (result.FromX, result.FromY, result.ToX, result.ToY));
    }

    [Fact]
    public void DoubleClick_WhenPointerTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(DifferentForegroundWindows(target), injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.DoubleClickAsync(target, 100, 100, false, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void Scroll_WithZeroDelta_FailsBeforeInjection()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.ScrollAsync(target, 100, 100, 0, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void Drag_WhenEndPointerTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var otherWindow = new IntPtr(200);
        var windows = ValidTargetWindows(target);
        windows.WindowAtPoints[(200, 200)] = otherWindow;
        windows.ProcessIds[otherWindow] = 20;
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.DragAsync(target, 100, 100, 200, 200, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.PointNotInTarget, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    public void Drag_WithPartialInjection_Fails(uint injectedCount)
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector(injectedCount);
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.DragAsync(target, 100, 100, 200, 200, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InputInjectionFailed, exception.ErrorCode);
        Assert.Equal(1, injector.Calls);
        Assert.Equal([4], injector.InputCounts);
    }

    [Fact]
    public void Key_WhenKeyboardTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var windows = DifferentForegroundWindows(target);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.KeyAsync(new KeyOptions { Keys = "CTRL+L", Hwnd = target }, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void Key_WithoutTarget_DoesNotInject()
    {
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(new FakeWindowSystem(), injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.KeyAsync(new KeyOptions { Keys = "CTRL+L" }, CancellationToken.None)
                .GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void TypeText_KeyboardWhenTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var windows = DifferentForegroundWindows(target);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.TypeTextAsync(new TypeTextOptions
            {
                Text = "hello",
                Mode = InteractionMode.Physical,
                Hwnd = target
            }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void TypeText_AutoFallbackWhenTargetValidationFails_DoesNotInject()
    {
        var target = new IntPtr(100);
        var windows = DifferentForegroundWindows(target);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.TypeTextAsync(new TypeTextOptions
            {
                Text = "hello",
                Mode = InteractionMode.Auto,
                Hwnd = target
            }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Theory]
    [InlineData(InteractionMode.Physical)]
    [InlineData(InteractionMode.Auto)]
    public void TypeText_KeyboardSuccess_ReportsTargetVerification(InteractionMode mode)
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var result = fixture.Input.TypeTextAsync(new TypeTextOptions
        {
            Text = "hello",
            Mode = mode,
            Hwnd = target
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal("sendinput-unicode", result.Interaction.ModeActual);
        Assert.True(result.Interaction.PhysicalInputGenerated);
        Assert.Equal("0x64", result.Interaction.TargetHwnd);
        Assert.True(result.Interaction.TargetForegroundVerified);
        Assert.True(result.Interaction.TargetDeliveryVerified);
    }

    [Fact]
    public void TypeText_ClipboardFallbackSuccess_ReportsPhysicalInputAndTargetVerification()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector(0, 4);
        var clipboard = new FakeClipboardAdapter();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector, clipboard);

        var result = fixture.Input.TypeTextAsync(new TypeTextOptions
        {
            Text = "hello",
            Mode = InteractionMode.Physical,
            AllowClipboard = true,
            Hwnd = target
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(2, injector.Calls);
        Assert.Equal([10, 4], injector.InputCounts);
        Assert.Equal(1, clipboard.SetTextCalls);
        Assert.Equal("clipboard-paste", result.Interaction.ModeActual);
        Assert.False(result.ClipboardSequenceChanged);
        Assert.True(result.Interaction.PhysicalInputGenerated);
        Assert.Equal("0x64", result.Interaction.TargetHwnd);
        Assert.True(result.Interaction.TargetForegroundVerified);
        Assert.True(result.Interaction.TargetDeliveryVerified);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    public void TypeText_ClipboardFallbackWithIncompleteInjection_Fails(uint injectedCount)
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector(0, injectedCount);
        var clipboard = new FakeClipboardAdapter();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector, clipboard);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.TypeTextAsync(new TypeTextOptions
            {
                Text = "hello",
                Mode = InteractionMode.Physical,
                AllowClipboard = true,
                Hwnd = target
            }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InputInjectionFailed, exception.ErrorCode);
        Assert.Equal(2, injector.Calls);
        Assert.Equal([10, 4], injector.InputCounts);
        Assert.Equal(1, clipboard.SetTextCalls);
    }

    [Fact]
    public void Key_WithValidTarget_InjectsThroughAdapterAndReportsVerification()
    {
        var target = new IntPtr(100);
        var windows = ValidTargetWindows(target);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(windows, injector);

        var result = fixture.Input.KeyAsync(
            new KeyOptions { Keys = "CTRL+L", Hwnd = target },
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(1, injector.Calls);
        Assert.Equal("0x64", result.Interaction.TargetHwnd);
        Assert.True(result.Interaction.TargetForegroundVerified);
        Assert.True(result.Interaction.TargetDeliveryVerified);
    }

    [Fact]
    public void Key_WithInvalidKey_FailsBeforeInjection()
    {
        var target = new IntPtr(100);
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(ValidTargetWindows(target), injector);

        var exception = Assert.Throws<CommandException>(() =>
            fixture.Input.KeyAsync(
                new KeyOptions { Keys = "NONEXISTENT_KEY", Hwnd = target },
                CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
        Assert.Equal(0, injector.Calls);
    }

    [Fact]
    public void ActivateWindow_WithZeroHwnd_Fails()
    {
        var injector = new RecordingInputInjector();
        using var fixture = CreateFixture(new FakeWindowSystem(), injector);

        var result = fixture.Input.ActivateWindowAsync(IntPtr.Zero, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.Activated);
    }

    private static FakeWindowSystem DifferentForegroundWindows(IntPtr target)
    {
        var foreground = new IntPtr(200);
        var windows = new FakeWindowSystem { ForegroundWindow = foreground };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[foreground] = 20;
        return windows;
    }

    private static FakeWindowSystem ValidTargetWindows(IntPtr target)
    {
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = target,
            WindowAtPoint = target
        };
        windows.ProcessIds[target] = 10;
        return windows;
    }

    private static InputFixture CreateFixture(
        FakeWindowSystem windows,
        RecordingInputInjector injector,
        IClipboardAdapter? clipboard = null)
    {
        var guardStore = new TempFileObservationGuardStore();
        var automation = new UiaAutomationBackend(guardStore);
        var input = new Win32InputBackend(
            automation,
            guardStore,
            new PhysicalInputTargetValidator(windows),
            injector,
            clipboard ?? new FakeClipboardAdapter());
        return new InputFixture(automation, input);
    }

    private sealed class InputFixture(UiaAutomationBackend automation, Win32InputBackend input) : IDisposable
    {
        public Win32InputBackend Input { get; } = input;

        public void Dispose()
        {
            Input.Dispose();
            automation.Dispose();
        }
    }

    private sealed class RecordingInputInjector : IInputInjector
    {
        private readonly Queue<uint> _results;

        public RecordingInputInjector(params uint[] results)
        {
            _results = new Queue<uint>(results);
        }

        public int Calls { get; private set; }
        public List<int> InputCounts { get; } = [];
        public List<NativeMethods.INPUT[]> Inputs { get; } = [];

        public uint SendInput(NativeMethods.INPUT[] inputs)
        {
            Calls++;
            InputCounts.Add(inputs.Length);
            Inputs.Add(inputs);
            return _results.TryDequeue(out var result) ? result : (uint)inputs.Length;
        }
    }

    private sealed class FakeClipboardAdapter : IClipboardAdapter
    {
        private uint _sequenceNumber = 100;

        public int SetTextCalls { get; private set; }

        public uint GetSequenceNumber() => _sequenceNumber;

        public void SetText(string text)
        {
            SetTextCalls++;
            _sequenceNumber++;
        }
    }

    private sealed class FakeWindowSystem : IWindowSystem
    {
        public IntPtr ForegroundWindow { get; init; }
        public IntPtr WindowAtPoint { get; init; }
        public Dictionary<(int X, int Y), IntPtr> WindowAtPoints { get; } = [];
        public Dictionary<IntPtr, IntPtr> Parents { get; } = [];
        public Dictionary<IntPtr, IntPtr> Owners { get; } = [];
        public Dictionary<IntPtr, int> ProcessIds { get; } = [];

        public IntPtr GetForegroundWindow() => ForegroundWindow;
        public IntPtr WindowFromPoint(int x, int y) =>
            WindowAtPoints.TryGetValue((x, y), out var hwnd) ? hwnd : WindowAtPoint;
        public IntPtr GetParent(IntPtr hwnd) => Parents.GetValueOrDefault(hwnd);
        public IntPtr GetOwner(IntPtr hwnd) => Owners.GetValueOrDefault(hwnd);
        public int GetProcessId(IntPtr hwnd) => ProcessIds.GetValueOrDefault(hwnd);
    }
}
