using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class DpiHelperTests
{
    [Fact]
    public void GetDpiScaleForWindow_ForDesktopWindow_ReturnsPositiveValue()
    {
        // Desktop HWND is always valid
        var desktopHwnd = NativeMethods.GetForegroundWindow();
        if (desktopHwnd == IntPtr.Zero)
            return; // skip if no foreground window

        var scale = DpiHelper.GetDpiScaleForWindow(desktopHwnd);
        Assert.True(scale >= 0.96 && scale <= 6.0, $"DPI scale {scale} is out of reasonable range");
    }

    [Fact]
    public void LogicalToPhysical_At100Percent_ReturnsSameValues()
    {
        var logical = new Kagami.Protocol.DetailedRect
        {
            Left = 100, Top = 200, Right = 900, Bottom = 800
        };

        var physical = DpiHelper.LogicalToPhysical(logical, 1.0);

        Assert.Equal(100, physical.Left);
        Assert.Equal(200, physical.Top);
        Assert.Equal(900, physical.Right);
        Assert.Equal(800, physical.Bottom);
    }

    [Fact]
    public void LogicalToPhysical_At150Percent_ScalesCorrectly()
    {
        var logical = new Kagami.Protocol.DetailedRect
        {
            Left = 100, Top = 200, Right = 300, Bottom = 400
        };

        var physical = DpiHelper.LogicalToPhysical(logical, 1.5);

        Assert.Equal(150, physical.Left);
        Assert.Equal(300, physical.Top);
        Assert.Equal(450, physical.Right);
        Assert.Equal(600, physical.Bottom);
    }

    [Fact]
    public void LogicalToPhysical_At200Percent_RoundsCorrectly()
    {
        var logical = new Kagami.Protocol.DetailedRect
        {
            Left = 1, Top = 1, Right = 101, Bottom = 101
        };

        var physical = DpiHelper.LogicalToPhysical(logical, 2.0);

        Assert.Equal(2, physical.Left);
        Assert.Equal(202, physical.Right);
    }
}
