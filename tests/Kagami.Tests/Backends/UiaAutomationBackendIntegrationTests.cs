using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Backends;

/// <summary>
/// Integration tests for UiaAutomationBackend.
/// These tests require a real desktop with windows available.
/// </summary>
public class UiaAutomationBackendIntegrationTests
{
    private readonly UiaAutomationBackend _backend;

    public UiaAutomationBackendIntegrationTests()
    {
        _backend = new UiaAutomationBackend(new TempFileObservationGuardStore());
    }

    [Fact]
    public void ListWindows_ReturnsNonEmpty_OnRealDesktop()
    {
        var windows = _backend.ListWindowsAsync(false, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotEmpty(windows);
        Assert.Contains(windows, w => w.ProcessName.Length > 0);
    }

    [Fact]
    public void ListWindows_VisibleOnly_ExcludesMinimized()
    {
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.All(windows, w => Assert.True(w.Visible));
    }

    [Fact]
    public void ListWindows_AllWindows_HaveValidHwndFormat()
    {
        var windows = _backend.ListWindowsAsync(false, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.All(windows, w => Assert.StartsWith("0x", w.Hwnd));
    }

    [Fact]
    public void GetTree_RootWindow_ReturnsValidTree()
    {
        // Find the first visible window with a title
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var target = windows.FirstOrDefault(w => w.Title.Length > 0 && w.Rect.W > 100 && w.Rect.H > 100);
        if (target is null)
            return; // Skip if no suitable window

        var hwnd = UiaAutomationBackend.ParseHwnd(target.Hwnd);
        if (hwnd == IntPtr.Zero)
            return;

        var tree = _backend.GetTreeAsync(new GetTreeOptions
        {
            Hwnd = hwnd,
            MaxDepth = 1,
            MaxNodes = 10,
            View = "control"
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(tree);
        Assert.NotNull(tree!.ControlType);
        Assert.True(tree.ControlType.Length > 0);
    }

    [Fact]
    public void ResolveLocator_LocatorRoundTrip_Succeeds()
    {
        // Get window + tree
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var target = windows.FirstOrDefault(w => w.Title.Length > 0 && w.Rect.W > 100 && w.Rect.H > 100);
        if (target is null) return;

        var hwnd = UiaAutomationBackend.ParseHwnd(target.Hwnd);
        if (hwnd == IntPtr.Zero) return;

        var tree = _backend.GetTreeAsync(new GetTreeOptions
        {
            Hwnd = hwnd,
            MaxDepth = 2,
            MaxNodes = 50,
            View = "control"
        }, CancellationToken.None).GetAwaiter().GetResult();

        if (tree?.Children.Count == 0 || tree!.Children[0].Locator is null)
            return; // No children to test with

        // Take the locator from the first child
        var locator = tree.Children[0].Locator!;

        // Resolve it back
        var resolved = _backend.ResolveLocatorAsync(locator, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotNull(resolved);
        Assert.NotNull(resolved!.Node);
        Assert.True(resolved.ResolutionMethod.Length > 0);
    }

    [Fact]
    public void BuildGuard_ForVisibleWindow_ReturnsValidGuard()
    {
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var target = windows.FirstOrDefault(w => w.Title.Length > 0);
        if (target is null) return;

        var hwnd = UiaAutomationBackend.ParseHwnd(target.Hwnd);
        if (hwnd == IntPtr.Zero) return;

        var guard = _backend.BuildGuard(hwnd);

        Assert.NotNull(guard);
        Assert.Equal(target.Hwnd, guard!.Hwnd);
        Assert.Equal(target.Pid, guard.Pid);
        Assert.NotNull(guard.ProcessStartTime);
        Assert.NotNull(guard.CapturedAt);
    }

    [Fact]
    public void FormatHwnd_RoundTrip_IsIdentity()
    {
        var original = new IntPtr(0x1A2B3C);
        var formatted = UiaAutomationBackend.FormatHwnd(original);
        var parsed = UiaAutomationBackend.ParseHwnd(formatted);

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ParseHwnd_InvalidInput_ReturnsZero()
    {
        Assert.Equal(IntPtr.Zero, UiaAutomationBackend.ParseHwnd("garbage"));
        Assert.Equal(IntPtr.Zero, UiaAutomationBackend.ParseHwnd(""));
    }

    [Fact]
    public void GetExtendedFrameBounds_ForVisibleWindow_ReturnsPositiveRect()
    {
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var target = windows.FirstOrDefault(w => w.Rect.W > 100 && w.Rect.H > 100);
        if (target is null) return;

        var hwnd = UiaAutomationBackend.ParseHwnd(target.Hwnd);
        if (hwnd == IntPtr.Zero) return;

        var rect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);

        Assert.True(rect.W > 0, $"Rect width should be positive but was {rect.W}");
        Assert.True(rect.H > 0, $"Rect height should be positive but was {rect.H}");
    }

    private void Dispose()
    {
        _backend.Dispose();
    }
}
