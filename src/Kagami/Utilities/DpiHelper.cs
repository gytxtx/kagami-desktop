using Kagami.Protocol;

namespace Kagami.Utilities;

internal static class DpiHelper
{
    /// <summary>
    /// Get the DPI scale factor for a window as a ratio of physical to logical pixels.
    /// e.g. 1.5 for 150% scaling.
    /// </summary>
    public static double GetDpiScaleForWindow(IntPtr hwnd)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>
    /// Convert a logical rectangle to physical screen pixels using the window's DPI.
    /// </summary>
    public static DetailedRect LogicalToPhysical(DetailedRect rect, double dpiScale)
    {
        return new DetailedRect
        {
            Left = (int)Math.Round(rect.Left * dpiScale),
            Top = (int)Math.Round(rect.Top * dpiScale),
            Right = (int)Math.Round(rect.Right * dpiScale),
            Bottom = (int)Math.Round(rect.Bottom * dpiScale)
        };
    }
}
