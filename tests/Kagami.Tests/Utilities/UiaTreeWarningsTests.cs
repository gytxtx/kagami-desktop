using Kagami.Commands;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Utilities;

public class UiaTreeWarningsTests
{
    [Fact]
    public void ForEmptyRoot_ReturnsWarning()
    {
        var warning = UiaTreeWarnings.ForEmptyRoot(new TreeNode { ControlType = "Window" });

        Assert.NotNull(warning);
        Assert.Equal("uia_tree_empty", warning!.Code);
        Assert.NotEmpty(warning.Message);
    }

    [Fact]
    public void ForEmptyRoot_WithChild_ReturnsNull()
    {
        var tree = new TreeNode
        {
            ControlType = "Window",
            Children = new List<TreeNode> { new() { ControlType = "Button" } }
        };

        Assert.Null(UiaTreeWarnings.ForEmptyRoot(tree));
    }

    [Fact]
    public void ForEmptyRoot_WithNullTree_ReturnsNull()
    {
        Assert.Null(UiaTreeWarnings.ForEmptyRoot(null));
    }

    [Fact]
    public void ForAmbiguousVisibility_OverlappingVisibleCustomRoots_ReturnsHonestWarning()
    {
        var tree = Root(
            Custom(Rect(0, 0, 1000, 700)),
            Custom(Rect(0, 0, 1000, 700)));

        var warning = UiaTreeWarnings.ForAmbiguousVisibility(tree);

        Assert.NotNull(warning);
        Assert.Equal("uia_visibility_ambiguous", warning!.Code);
        Assert.Contains("is_offscreen", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screenshot", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForAmbiguousVisibility_OverlapAtEightyPercent_ReturnsWarning()
    {
        var tree = Root(
            Custom(Rect(0, 0, 100, 100)),
            Custom(Rect(20, 0, 120, 100)));

        Assert.NotNull(UiaTreeWarnings.ForAmbiguousVisibility(tree));
    }

    [Fact]
    public void ForAmbiguousVisibility_OverlapBelowEightyPercent_ReturnsNull()
    {
        var tree = Root(
            Custom(Rect(0, 0, 100, 100)),
            Custom(Rect(21, 0, 121, 100)));

        Assert.Null(UiaTreeWarnings.ForAmbiguousVisibility(tree));
    }

    [Fact]
    public void ForAmbiguousVisibility_NestedCustomChildren_AreIgnored()
    {
        var tree = Root(Custom(
            Rect(0, 0, 100, 100),
            children: new List<TreeNode>
            {
                Custom(Rect(0, 0, 100, 100)),
                Custom(Rect(0, 0, 100, 100))
            }));

        Assert.Null(UiaTreeWarnings.ForAmbiguousVisibility(tree));
    }

    [Fact]
    public void ForAmbiguousVisibility_OffscreenAndInvalidRectangles_AreIgnored()
    {
        var tree = Root(
            Custom(Rect(0, 0, 100, 100)),
            Custom(Rect(0, 0, 100, 100), isOffscreen: true),
            Custom(null),
            Custom(Rect(0, 0, 0, 100)),
            Custom(Rect(0, 0, 100, 0)));

        Assert.Null(UiaTreeWarnings.ForAmbiguousVisibility(tree));
    }

    [Fact]
    public void ForAmbiguousVisibility_OverlappingNonCustomChildren_AreIgnored()
    {
        var tree = Root(
            Node("Button", Rect(0, 0, 100, 100)),
            Node("Pane", Rect(0, 0, 100, 100)));

        Assert.Null(UiaTreeWarnings.ForAmbiguousVisibility(tree));
    }

    [Fact]
    public void ObserveWarnings_PreserveExistingWarningsWhenAddingVisibilityWarning()
    {
        var warnings = new List<JsonWarning>
        {
            new() { Code = "capture_fallback", Message = "Existing warning." }
        };
        var tree = Root(
            Custom(Rect(0, 0, 100, 100)),
            Custom(Rect(0, 0, 100, 100)));

        ObserveCommand.AddTreeWarnings(warnings, tree);

        Assert.Equal(
            new[] { "capture_fallback", "uia_visibility_ambiguous" },
            warnings.Select(warning => warning.Code));
    }

    private static TreeNode Root(params TreeNode[] children) => new()
    {
        ControlType = "Window",
        Children = children.ToList()
    };

    private static TreeNode Custom(
        DetailedRect? rect,
        bool isOffscreen = false,
        List<TreeNode>? children = null) =>
        Node("Custom", rect, isOffscreen, children);

    private static TreeNode Node(
        string controlType,
        DetailedRect? rect,
        bool isOffscreen = false,
        List<TreeNode>? children = null) => new()
    {
        ControlType = controlType,
        Rect = rect,
        IsOffscreen = isOffscreen,
        Children = children ?? new List<TreeNode>()
    };

    private static DetailedRect Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom
    };
}
