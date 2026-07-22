using Kagami.Protocol;

namespace Kagami.Backends;

public static class TreeOutputPolicy
{
    private static readonly HashSet<string> InteractivePatterns = new(StringComparer.Ordinal)
    {
        "invoke",
        "value",
        "toggle",
        "expand_collapse",
        "selection_item"
    };

    public static bool IsInteractive(TreeNode node) =>
        node.IsKeyboardFocusable || node.Patterns.Any(InteractivePatterns.Contains);

    public static bool ShouldIncludeLocator(string mode, TreeNode node) =>
        mode.ToLowerInvariant() switch
        {
            "all" => true,
            "interactive" => IsInteractive(node),
            "none" => false,
            _ => throw new ArgumentException(
                "Locator mode must be one of: all, interactive, none.",
                nameof(mode))
        };

    public static bool IsSupportedLocatorMode(string mode) =>
        mode.Equals("all", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("interactive", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("none", StringComparison.OrdinalIgnoreCase);

    public static TreeNode Apply(TreeNode root, bool interactiveOnly, string includeLocators) =>
        ApplyNode(root, interactiveOnly, includeLocators, preserveNode: true)!;

    private static TreeNode? ApplyNode(
        TreeNode node,
        bool interactiveOnly,
        string includeLocators,
        bool preserveNode)
    {
        var children = node.Children
            .Select(child => ApplyNode(child, interactiveOnly, includeLocators, preserveNode: false))
            .Where(child => child is not null)
            .Cast<TreeNode>()
            .ToList();

        if (interactiveOnly && !preserveNode && !IsInteractive(node) && children.Count == 0)
            return null;

        return new TreeNode
        {
            NodeId = node.NodeId,
            RuntimeId = node.RuntimeId,
            TreePath = node.TreePath,
            ControlType = node.ControlType,
            Name = node.Name,
            AutomationId = node.AutomationId,
            ClassName = node.ClassName,
            FrameworkId = node.FrameworkId,
            ProcessId = node.ProcessId,
            NativeWindowHandle = node.NativeWindowHandle,
            Rect = node.Rect,
            ClickablePoint = node.ClickablePoint,
            IsEnabled = node.IsEnabled,
            IsOffscreen = node.IsOffscreen,
            IsKeyboardFocusable = node.IsKeyboardFocusable,
            HasKeyboardFocus = node.HasKeyboardFocus,
            IsVirtualized = node.IsVirtualized,
            Patterns = node.Patterns,
            ChildrenCount = children.Count,
            ChildrenTruncated = node.ChildrenTruncated,
            Children = children,
            Locator = ShouldIncludeLocator(includeLocators, node) ? node.Locator : null
        };
    }
}
