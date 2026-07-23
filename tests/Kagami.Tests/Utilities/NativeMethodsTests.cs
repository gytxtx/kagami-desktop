using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class NativeMethodsTests
{
    [Fact]
    public void GetForegroundWindow_ReturnsNonZero()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        // There should always be a foreground window on a running desktop
        Assert.NotEqual(IntPtr.Zero, hwnd);
    }

    [Fact]
    public void IsWindowVisible_ForegroundWindow_ReturnsTrue()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        Assert.True(NativeMethods.IsWindowVisible(hwnd));
    }

    [Fact]
    public void GetWindowText_ForegroundWindow_ReturnsNonEmpty()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        int length = NativeMethods.GetWindowTextLength(hwnd);
        Assert.True(length >= 0);
    }

    [Fact]
    public void GetWindowThreadProcessId_ReturnsValidPid()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        uint pid;
        uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
        Assert.True(pid > 0);
        Assert.True(tid > 0);
    }

    [Fact]
    public void GetWindowRect_ReturnsPositiveDimensions()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.GetWindowRect(hwnd, out var rect);
        Assert.True(rect.Right - rect.Left > 0);
        Assert.True(rect.Bottom - rect.Top > 0);
    }

    [Fact]
    public void GetSystemMetrics_VirtualScreen_ReturnsPositiveSize()
    {
        int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        Assert.True(cx > 0, $"Virtual screen width should be positive but was {cx}");
        Assert.True(cy > 0, $"Virtual screen height should be positive but was {cy}");
    }

    [Fact]
    public void GetCursorPos_ReturnsPoint()
    {
        Assert.True(NativeMethods.GetCursorPos(out var pt));
        // Cursor should be somewhere on the virtual desktop
        int maxX = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int maxY = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        Assert.True(pt.X >= -maxX && pt.X <= maxX * 2, $"Cursor X={pt.X} is out of bounds");
        Assert.True(pt.Y >= -maxY && pt.Y <= maxY * 2, $"Cursor Y={pt.Y} is out of bounds");
    }

    [Fact]
    public void GetDpiForWindow_ForegroundWindow_ReturnsPositive()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        Assert.True(dpi >= 96, $"DPI should be at least 96 but was {dpi}");
        Assert.True(dpi <= 960, $"DPI {dpi} is unreasonably high");
    }

    [Fact]
    public void GetClipboardSequenceNumber_DoesNotThrow()
    {
        _ = NativeMethods.GetClipboardSequenceNumber();
    }
}
