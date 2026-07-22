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

    private static InputFixture CreateFixture(FakeWindowSystem windows, RecordingInputInjector injector)
    {
        var guardStore = new TempFileObservationGuardStore();
        var automation = new UiaAutomationBackend(guardStore);
        var input = new Win32InputBackend(
            automation,
            guardStore,
            new PhysicalInputTargetValidator(windows),
            injector);
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
        public int Calls { get; private set; }

        public uint SendInput(NativeMethods.INPUT[] inputs)
        {
            Calls++;
            return (uint)inputs.Length;
        }
    }

    private sealed class FakeWindowSystem : IWindowSystem
    {
        public IntPtr ForegroundWindow { get; init; }
        public IntPtr WindowAtPoint { get; init; }
        public Dictionary<IntPtr, IntPtr> Parents { get; } = [];
        public Dictionary<IntPtr, IntPtr> Owners { get; } = [];
        public Dictionary<IntPtr, int> ProcessIds { get; } = [];

        public IntPtr GetForegroundWindow() => ForegroundWindow;
        public IntPtr WindowFromPoint(int x, int y) => WindowAtPoint;
        public IntPtr GetParent(IntPtr hwnd) => Parents.GetValueOrDefault(hwnd);
        public IntPtr GetOwner(IntPtr hwnd) => Owners.GetValueOrDefault(hwnd);
        public int GetProcessId(IntPtr hwnd) => ProcessIds.GetValueOrDefault(hwnd);
    }
}
