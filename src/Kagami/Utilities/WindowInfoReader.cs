using System.Runtime.InteropServices;
using Kagami.Protocol;

namespace Kagami.Utilities;

internal static class WindowInfoReader
{
    public static WindowInfo Read(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var foreground = NativeMethods.GetForegroundWindow();

        NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CLOAKED,
            out bool cloaked,
            Marshal.SizeOf<bool>());

        return new WindowInfo
        {
            Hwnd = $"0x{hwnd:x}",
            Pid = (int)pid,
            ProcessName = ProcessHelper.GetProcessName((int)pid) ?? "",
            Title = ReadWindowTitle(hwnd),
            ClassName = ReadClassName(hwnd),
            Visible = NativeMethods.IsWindowVisible(hwnd),
            Cloaked = cloaked,
            Minimized = NativeMethods.IsIconic(hwnd),
            Foreground = hwnd == foreground,
            Rect = GetExtendedFrameBounds(hwnd)
        };
    }

    public static Rect GetExtendedFrameBounds(IntPtr hwnd)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT rect,
            Marshal.SizeOf<RECT>());

        if (result != 0)
            NativeMethods.GetWindowRect(hwnd, out rect);

        return new Rect
        {
            X = rect.Left,
            Y = rect.Top,
            W = rect.Right - rect.Left,
            H = rect.Bottom - rect.Top
        };
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        var buffer = new char[length + 1];
        var charactersRead = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return charactersRead > 0 ? new string(buffer, 0, charactersRead) : "";
    }

    private static string ReadClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var charactersRead = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return charactersRead > 0 ? new string(buffer, 0, charactersRead) : "";
    }
}
