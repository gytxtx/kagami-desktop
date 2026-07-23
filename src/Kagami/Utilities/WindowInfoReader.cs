using System.Runtime.InteropServices;
using Kagami.Protocol;

namespace Kagami.Utilities;

internal interface IWindowInfoReader
{
    WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd);
    WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd, Rect rect);
}

internal interface IWindowInfoSource
{
    int GetProcessId(IntPtr hwnd);
    string? GetProcessName(int pid);
    string GetWindowTitle(IntPtr hwnd);
    string GetClassName(IntPtr hwnd);
    bool IsVisible(IntPtr hwnd);
    bool IsCloaked(IntPtr hwnd);
    bool IsMinimized(IntPtr hwnd);
    Rect GetExtendedFrameBounds(IntPtr hwnd);
}

internal sealed class WindowInfoReader : IWindowInfoReader
{
    private readonly IWindowInfoSource _source;

    public WindowInfoReader()
        : this(new NativeWindowInfoSource())
    {
    }

    internal WindowInfoReader(IWindowInfoSource source)
    {
        _source = source;
    }

    public WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd) =>
        Read(hwnd, foregroundHwnd, _source.GetExtendedFrameBounds(hwnd));

    public WindowInfo Read(IntPtr hwnd, IntPtr foregroundHwnd, Rect rect)
    {
        var pid = _source.GetProcessId(hwnd);

        return new WindowInfo
        {
            Hwnd = $"0x{hwnd:x}",
            Pid = pid,
            ProcessName = _source.GetProcessName(pid) ?? "",
            Title = _source.GetWindowTitle(hwnd),
            ClassName = _source.GetClassName(hwnd),
            Visible = _source.IsVisible(hwnd),
            Cloaked = _source.IsCloaked(hwnd),
            Minimized = _source.IsMinimized(hwnd),
            Foreground = hwnd == foregroundHwnd,
            Rect = rect
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

}

internal sealed class NativeWindowInfoSource : IWindowInfoSource
{
    public int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public string? GetProcessName(int pid) => ProcessHelper.GetProcessName(pid);

    public string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";

        var buffer = new char[length + 1];
        var charactersRead = NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return charactersRead > 0 ? new string(buffer, 0, charactersRead) : "";
    }

    public string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var charactersRead = NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return charactersRead > 0 ? new string(buffer, 0, charactersRead) : "";
    }

    public bool IsVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    public bool IsCloaked(IntPtr hwnd)
    {
        NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CLOAKED,
            out bool cloaked,
            Marshal.SizeOf<bool>());
        return cloaked;
    }

    public bool IsMinimized(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);

    public Rect GetExtendedFrameBounds(IntPtr hwnd) =>
        WindowInfoReader.GetExtendedFrameBounds(hwnd);
}
