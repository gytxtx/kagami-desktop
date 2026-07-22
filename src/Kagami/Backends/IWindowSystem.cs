using Kagami.Utilities;

namespace Kagami.Backends;

internal interface IWindowSystem
{
    IntPtr GetForegroundWindow();
    IntPtr WindowFromPoint(int x, int y);
    IntPtr GetOwner(IntPtr hwnd);
    int GetProcessId(IntPtr hwnd);
}

internal sealed class NativeWindowSystem : IWindowSystem
{
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public IntPtr WindowFromPoint(int x, int y) => NativeMethods.WindowFromPoint(new POINT { X = x, Y = y });

    public IntPtr GetOwner(IntPtr hwnd) => NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);

    public int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return unchecked((int)processId);
    }
}
