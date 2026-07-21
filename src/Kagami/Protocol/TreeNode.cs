using System.Text.Json.Serialization;

namespace Kagami.Protocol;

public class TreeNode
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = "";

    [JsonPropertyName("runtime_id")]
    public string RuntimeId { get; init; } = "";

    [JsonPropertyName("control_type")]
    public string ControlType { get; init; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; init; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; init; }

    [JsonPropertyName("framework_id")]
    public string? FrameworkId { get; init; }

    [JsonPropertyName("process_id")]
    public int ProcessId { get; init; }

    [JsonPropertyName("native_window_handle")]
    public string? NativeWindowHandle { get; init; }

    [JsonPropertyName("rect")]
    public DetailedRect? Rect { get; init; }

    [JsonPropertyName("clickable_point")]
    public Point? ClickablePoint { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("is_offscreen")]
    public bool IsOffscreen { get; init; }

    [JsonPropertyName("is_keyboard_focusable")]
    public bool IsKeyboardFocusable { get; init; }

    [JsonPropertyName("has_keyboard_focus")]
    public bool HasKeyboardFocus { get; init; }

    [JsonPropertyName("is_virtualized")]
    public bool IsVirtualized { get; init; }

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; init; } = new();

    [JsonPropertyName("children_count")]
    public int ChildrenCount { get; init; }

    [JsonPropertyName("children_truncated")]
    public bool ChildrenTruncated { get; init; }

    [JsonPropertyName("children")]
    public List<TreeNode> Children { get; init; } = new();

    [JsonPropertyName("locator")]
    public Locator? Locator { get; init; }
}
