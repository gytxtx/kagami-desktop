using Kagami.Protocol;

namespace Kagami.Backends;

public class GetTreeOptions
{
    public IntPtr Hwnd { get; init; }
    public int MaxDepth { get; init; } = 1;
    public int MaxNodes { get; init; } = 200;
    public string View { get; init; } = "control";
    public string? Path { get; init; }
    public string? RuntimeId { get; init; }
}

public class FindOptions
{
    public IntPtr Hwnd { get; init; }
    public Locator? StartLocator { get; init; }
    public string? ControlType { get; init; }
    public string? AutomationId { get; init; }
    public string? Name { get; init; }
    public string? ClassName { get; init; }
    public int MaxResults { get; init; } = 10;
}

public interface IAutomationBackend
{
    /// <summary>Enumerate all top-level windows on the desktop.</summary>
    Task<List<WindowInfo>> ListWindowsAsync(bool visibleOnly, string? processName, string? title, CancellationToken ct);

    /// <summary>
    /// Get a UIA element tree starting from the specified window or subtree node.
    /// Each node carries a re-resolvable Locator.
    /// </summary>
    Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct);

    /// <summary>
    /// Find elements matching criteria within a window or subtree.
    /// </summary>
    Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct);

    /// <summary>
    /// Resolve a Locator back to a UIA element and return its current state.
    /// Returns null if the locator cannot be resolved.
    /// </summary>
    Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct);

    /// <summary>
    /// Try to set focus to a UIA element identified by locator.
    /// </summary>
    Task<bool> FocusAsync(Locator locator, CancellationToken ct);
}

public class LocatorResolution
{
    public required TreeNode Node { get; init; }
    public bool MatchedRuntimeId { get; init; }
    public required string ResolutionMethod { get; init; }
}
