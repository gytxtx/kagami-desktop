using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Utilities;

public static class HwndHelper
{
    public static IntPtr ParseExisting(string value)
    {
        var hwnd = UiaAutomationBackend.ParseHwnd(value);
        if (hwnd == IntPtr.Zero)
            throw new CommandException(ErrorCodes.InvalidArgument, $"Invalid HWND: {value}");

        if (!NativeMethods.IsWindow(hwnd))
            throw new CommandException(ErrorCodes.WindowDestroyed, $"Window handle {value} does not exist or is no longer available.");

        return hwnd;
    }
}
