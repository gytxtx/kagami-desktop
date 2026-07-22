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

    [Theory]
    [InlineData("control")]
    [InlineData("content")]
    [InlineData("raw")]
    public void ResolveLocator_AllReturnedNonRootInteractiveNodes_RoundTripToSameRuntimeId(string view)
    {
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var trees = windows
            .Where(w => w.Title.Length > 0 && w.Rect.W > 100 && w.Rect.H > 100)
            .Select(w => UiaAutomationBackend.ParseHwnd(w.Hwnd))
            .Where(hwnd => hwnd != IntPtr.Zero)
            .Select(hwnd => _backend.GetTreeAsync(new GetTreeOptions
            {
                Hwnd = hwnd,
                MaxDepth = 2,
                MaxNodes = 100,
                View = view
            }, CancellationToken.None).GetAwaiter().GetResult())
            .Where(tree => tree is not null)
            .Cast<TreeNode>()
            .ToList();

        var nonRootNodes = trees
            .SelectMany(Flatten)
            .Where(node => node.Locator is not null && node.Locator.Path.Count > 0)
            .ToList();
        Assert.NotEmpty(nonRootNodes);

        var interactiveNodes = nonRootNodes
            .Where(node => node.Patterns.Count > 0)
            .ToList();
        var nodesToVerify = interactiveNodes.Count > 0
            ? interactiveNodes
            : new List<TreeNode> { nonRootNodes[0] };

        Assert.NotEmpty(nodesToVerify);
        foreach (var node in nodesToVerify)
        {
            Assert.Equal(view, node.Locator!.View);
            Assert.NotEmpty(node.Locator.Path);

            var resolved = _backend.ResolveLocatorAsync(node.Locator, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.NotNull(resolved);
            Assert.Equal(node.RuntimeId, resolved!.Node.RuntimeId);
        }
    }

    [Theory]
    [InlineData("control")]
    [InlineData("content")]
    [InlineData("raw")]
    public void Find_StartLocatorWithZeroHwnd_UsesLocatorWindowAndViewForReturnedLocators(string view)
    {
        var (hwnd, startNode) = FindNonRootNode(view);

        var results = _backend.FindAsync(new FindOptions
        {
            Hwnd = IntPtr.Zero,
            StartLocator = startNode.Locator,
            ControlType = startNode.ControlType,
            MaxResults = 5
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.NotNull(result.Locator);
            Assert.Equal(hwnd, UiaAutomationBackend.ParseHwnd(result.Locator!.Window.Hwnd));
            Assert.Equal(view, result.Locator.View);

            var resolved = _backend.ResolveLocatorAsync(result.Locator, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.NotNull(resolved);
            Assert.Equal(result.RuntimeId, resolved!.Node.RuntimeId);
        });
    }

    [Fact]
    public void Find_StartLocatorWithConflictingHwnd_RejectsInvalidArgument()
    {
        var (hwnd, startNode) = FindNonRootNode("control");
        var conflictingHwnd = new IntPtr(hwnd.ToInt64() ^ 1);
        if (conflictingHwnd == IntPtr.Zero)
            conflictingHwnd = new IntPtr(1);

        var exception = Assert.Throws<CommandException>(() =>
            _backend.FindAsync(new FindOptions
            {
                Hwnd = conflictingHwnd,
                StartLocator = startNode.Locator,
                ControlType = startNode.ControlType,
                MaxResults = 5
            }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(ErrorCodes.InvalidArgument, exception.ErrorCode);
    }

    [Theory]
    [InlineData("control")]
    [InlineData("content")]
    [InlineData("raw")]
    public void Find_NonRootResultTreePath_RoundTripsThroughGetTreePath(string view)
    {
        var (hwnd, startNode) = FindNonRootNode(view);

        var result = Assert.Single(_backend.FindAsync(new FindOptions
        {
            StartLocator = startNode.Locator,
            ControlType = startNode.ControlType,
            MaxResults = 1
        }, CancellationToken.None).GetAwaiter().GetResult());

        Assert.Equal(startNode.RuntimeId, result.RuntimeId);
        Assert.False(string.IsNullOrEmpty(result.TreePath));
        Assert.Equal(startNode.TreePath, result.TreePath);

        var expanded = _backend.GetTreeAsync(new GetTreeOptions
        {
            Hwnd = hwnd,
            Path = result.TreePath,
            MaxDepth = 0,
            View = view
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(expanded);
        Assert.Equal(result.RuntimeId, expanded!.RuntimeId);
        Assert.Equal(result.TreePath, expanded.TreePath);
    }

    [Fact]
    public void Find_ContainerResult_MarksOmittedChildrenAsTruncated()
    {
        var (_, container) = FindTreeNode(
            "control",
            node => node.Children.Count > 0,
            "non-root container");

        var result = FindStartingNode(container);

        Assert.Equal(0, result.ChildrenCount);
        Assert.Empty(result.Children);
        Assert.True(result.ChildrenTruncated);
    }

    [Fact]
    public void Find_LeafResult_DoesNotMarkChildrenAsTruncated()
    {
        var (_, leaf) = FindTreeNode(
            "control",
            node => node.Children.Count == 0 && !node.ChildrenTruncated,
            "non-root leaf");

        var result = FindStartingNode(leaf);

        Assert.Equal(0, result.ChildrenCount);
        Assert.Empty(result.Children);
        Assert.False(result.ChildrenTruncated);
    }

    [Fact]
    public void GetTree_PathRuntimeIdAndLocatorStarts_ReturnSameNodeAndTreePath()
    {
        var (hwnd, startNode) = FindNonRootNode("control");
        Assert.False(string.IsNullOrEmpty(startNode.TreePath));

        var starts = new[]
        {
            new GetTreeOptions
            {
                Hwnd = hwnd,
                Path = startNode.TreePath,
                MaxDepth = 0,
                View = "control"
            },
            new GetTreeOptions
            {
                Hwnd = hwnd,
                RuntimeId = startNode.RuntimeId,
                MaxDepth = 0,
                View = "control"
            },
            new GetTreeOptions
            {
                Hwnd = hwnd,
                StartLocator = startNode.Locator,
                MaxDepth = 0,
                View = "control"
            }
        };

        foreach (var options in starts)
        {
            var result = _backend.GetTreeAsync(options, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.NotNull(result);
            Assert.Equal(startNode.RuntimeId, result!.RuntimeId);
            Assert.Equal(startNode.TreePath, result.TreePath);
        }
    }

    [Theory]
    [InlineData("control")]
    [InlineData("content")]
    [InlineData("raw")]
    public void Find_RequestedViewAndMaxResults_AreApplied(string view)
    {
        var (hwnd, _) = FindNonRootNode(view);
        var results = _backend.FindAsync(new FindOptions
        {
            Hwnd = hwnd,
            ControlType = "Window",
            MaxResults = 1,
            View = view
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.Single(results);
        Assert.Equal(view, results[0].Locator!.View);
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

    private static IEnumerable<TreeNode> Flatten(TreeNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    private (IntPtr Hwnd, TreeNode Node) FindNonRootNode(string view)
        => FindTreeNode(view, _ => true, $"non-root {view} UIA node");

    private (IntPtr Hwnd, TreeNode Node) FindTreeNode(
        string view,
        Func<TreeNode, bool> predicate,
        string description)
    {
        var windows = _backend.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        foreach (var window in windows.Where(w => w.Title.Length > 0 && w.Rect.W > 100 && w.Rect.H > 100))
        {
            var hwnd = UiaAutomationBackend.ParseHwnd(window.Hwnd);
            if (hwnd == IntPtr.Zero)
                continue;

            var tree = _backend.GetTreeAsync(new GetTreeOptions
            {
                Hwnd = hwnd,
                MaxDepth = 3,
                MaxNodes = 300,
                View = view
            }, CancellationToken.None).GetAwaiter().GetResult();
            var node = tree is null
                ? null
                : Flatten(tree).FirstOrDefault(candidate =>
                    candidate.Locator is not null &&
                    candidate.Locator.Path.Count > 0 &&
                    predicate(candidate));
            if (node is not null)
                return (hwnd, node);
        }

        throw new Xunit.Sdk.XunitException($"No {description} was available for integration testing.");
    }

    private TreeNode FindStartingNode(TreeNode startNode) => Assert.Single(
        _backend.FindAsync(new FindOptions
        {
            StartLocator = startNode.Locator,
            ControlType = startNode.ControlType,
            MaxResults = 1
        }, CancellationToken.None).GetAwaiter().GetResult());
}
