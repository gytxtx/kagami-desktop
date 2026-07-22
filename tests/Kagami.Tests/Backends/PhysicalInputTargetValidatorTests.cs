using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class PhysicalInputTargetValidatorTests
{
    [Fact]
    public void ValidateKeyboardTarget_WithZeroTarget_ThrowsInvalidArgument()
    {
        var validator = new PhysicalInputTargetValidator(new FakeWindowSystem());

        var exception = Assert.Throws<CommandException>(() => validator.ValidateKeyboardTarget(IntPtr.Zero));

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
    }

    [Fact]
    public void ValidateKeyboardTarget_WithForegroundInDifferentProcess_ThrowsForegroundActivationDenied()
    {
        var target = new IntPtr(100);
        var foreground = new IntPtr(200);
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = foreground
        };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[foreground] = 20;
        var validator = new PhysicalInputTargetValidator(windows);

        var exception = Assert.Throws<CommandException>(() => validator.ValidateKeyboardTarget(target));

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
    }

    [Fact]
    public void ValidateKeyboardTarget_WithOwnedForegroundPopupInTargetProcess_AcceptsTargetFamily()
    {
        var target = new IntPtr(100);
        var popup = new IntPtr(101);
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = popup
        };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[popup] = 10;
        windows.Owners[popup] = target;
        var validator = new PhysicalInputTargetValidator(windows);

        var validation = validator.ValidateKeyboardTarget(target);

        Assert.Equal(target, validation.TargetHwnd);
        Assert.True(validation.ForegroundVerified);
        Assert.True(validation.DeliveryVerified);
    }

    [Fact]
    public void ValidatePointerTarget_WithOwnedForegroundAndHitPopupInTargetProcess_AcceptsTargetFamily()
    {
        var target = new IntPtr(100);
        var popup = new IntPtr(101);
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = popup,
            WindowAtPoint = popup
        };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[popup] = 10;
        windows.Owners[popup] = target;
        var validator = new PhysicalInputTargetValidator(windows);

        var validation = validator.ValidatePointerTarget(target, 300, 400);

        Assert.Equal(target, validation.TargetHwnd);
        Assert.True(validation.ForegroundVerified);
        Assert.True(validation.DeliveryVerified);
    }

    [Fact]
    public void ValidatePointerTarget_WithHitInDifferentProcess_ThrowsPointNotInTarget()
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
        var validator = new PhysicalInputTargetValidator(windows);

        var exception = Assert.Throws<CommandException>(() => validator.ValidatePointerTarget(target, 300, 400));

        Assert.Equal(ErrorCodes.PointNotInTarget, exception.ErrorCode);
    }

    [Fact]
    public void ValidateKeyboardTarget_WithOwnedPopupInDifferentProcess_ThrowsForegroundActivationDenied()
    {
        var target = new IntPtr(100);
        var popup = new IntPtr(101);
        var windows = new FakeWindowSystem
        {
            ForegroundWindow = popup
        };
        windows.ProcessIds[target] = 10;
        windows.ProcessIds[popup] = 20;
        windows.Owners[popup] = target;
        var validator = new PhysicalInputTargetValidator(windows);

        var exception = Assert.Throws<CommandException>(() => validator.ValidateKeyboardTarget(target));

        Assert.Equal(ErrorCodes.ForegroundActivationDenied, exception.ErrorCode);
    }

    private sealed class FakeWindowSystem : IWindowSystem
    {
        public IntPtr ForegroundWindow { get; init; }
        public IntPtr WindowAtPoint { get; init; }
        public Dictionary<IntPtr, IntPtr> Owners { get; } = [];
        public Dictionary<IntPtr, int> ProcessIds { get; } = [];

        public IntPtr GetForegroundWindow() => ForegroundWindow;

        public IntPtr WindowFromPoint(int x, int y) => WindowAtPoint;

        public IntPtr GetOwner(IntPtr hwnd) => Owners.GetValueOrDefault(hwnd);

        public int GetProcessId(IntPtr hwnd) => ProcessIds.GetValueOrDefault(hwnd);
    }
}
