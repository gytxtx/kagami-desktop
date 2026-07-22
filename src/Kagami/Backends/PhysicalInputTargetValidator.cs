using Kagami.Protocol;

namespace Kagami.Backends;

internal sealed record PhysicalInputTargetValidation(
    IntPtr TargetHwnd,
    bool ForegroundVerified,
    bool DeliveryVerified);

internal sealed class PhysicalInputTargetValidator
{
    private readonly IWindowSystem _windowSystem;

    public PhysicalInputTargetValidator(IWindowSystem windowSystem)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
    }

    public PhysicalInputTargetValidation ValidateKeyboardTarget(IntPtr targetHwnd)
    {
        EnsureTarget(targetHwnd);

        if (!IsInTargetFamily(targetHwnd, _windowSystem.GetForegroundWindow()))
        {
            throw new CommandException(
                ErrorCodes.ForegroundActivationDenied,
                "The requested target window is not in the foreground window family.");
        }

        return new PhysicalInputTargetValidation(targetHwnd, ForegroundVerified: true, DeliveryVerified: true);
    }

    public PhysicalInputTargetValidation ValidatePointerTarget(IntPtr targetHwnd, int x, int y)
    {
        ValidateKeyboardTarget(targetHwnd);

        if (!IsInTargetFamily(targetHwnd, _windowSystem.WindowFromPoint(x, y)))
        {
            throw new CommandException(
                ErrorCodes.PointNotInTarget,
                "The requested point does not resolve to the target window family.");
        }

        return new PhysicalInputTargetValidation(targetHwnd, ForegroundVerified: true, DeliveryVerified: true);
    }

    private void EnsureTarget(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero)
        {
            throw new CommandException(ErrorCodes.InvalidArgument, "A target window handle is required.");
        }
    }

    private bool IsInTargetFamily(IntPtr targetHwnd, IntPtr candidateHwnd)
    {
        if (candidateHwnd == IntPtr.Zero)
        {
            return false;
        }

        var targetProcessId = _windowSystem.GetProcessId(targetHwnd);
        if (targetProcessId == 0 || _windowSystem.GetProcessId(candidateHwnd) != targetProcessId)
        {
            return false;
        }

        var targetRoot = NormalizeOwnerRoot(targetHwnd, targetProcessId);
        var candidateRoot = NormalizeOwnerRoot(candidateHwnd, targetProcessId);
        return targetRoot != IntPtr.Zero && targetRoot == candidateRoot;
    }

    private IntPtr NormalizeOwnerRoot(IntPtr hwnd, int expectedProcessId)
    {
        var visited = new HashSet<IntPtr>();
        var current = hwnd;

        while (current != IntPtr.Zero && visited.Add(current))
        {
            if (_windowSystem.GetProcessId(current) != expectedProcessId)
            {
                return IntPtr.Zero;
            }

            var owner = _windowSystem.GetOwner(current);
            if (owner == IntPtr.Zero)
            {
                return current;
            }

            current = owner;
        }

        return IntPtr.Zero;
    }
}
