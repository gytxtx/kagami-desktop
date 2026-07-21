using System.Text.Json;

namespace Kagami;

/// <summary>
/// Shared JSON serializer options used across all commands.
/// Uses relaxed casing, indentation, and camelCase for protocol compatibility.
/// </summary>
internal static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}
