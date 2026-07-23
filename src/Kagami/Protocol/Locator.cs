using System.Text.Json.Serialization;

namespace Kagami.Protocol;

/// <summary>
/// A re-resolvable path to a UIA element within a window.
/// Each segment narrows the search from the window root using priority matching:
///   1. AutomationId (authoritative — if specified, it alone decides)
///   2. ControlType + Name (exact) + optional ClassName
///   3. ControlType + ClassName + ordinal
///
/// If all fields in a segment are non-null, they act as AND conditions at that priority.
/// </summary>
public class Locator
{
    [JsonPropertyName("view")]
    public string View { get; init; } = "control";

    [JsonPropertyName("window")]
    public WindowRef Window { get; init; } = new();

    [JsonPropertyName("path")]
    public List<LocatorSegment> Path { get; init; } = new();
}

public class WindowRef
{
    [JsonPropertyName("hwnd")]
    public string Hwnd { get; init; } = "0x0";
}

public class LocatorSegment
{
    [JsonPropertyName("control_type")]
    public string? ControlType { get; init; }

    [JsonPropertyName("automation_id")]
    public string? AutomationId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; init; }

    /// <summary>
    /// Ordinal among siblings matching the same preceding conditions.
    /// </summary>
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; init; }
}
