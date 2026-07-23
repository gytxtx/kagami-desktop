using System.Text.Json.Serialization;

namespace Kagami.Protocol;

public class JsonError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("retryable")]
    public bool Retryable { get; init; }

    [JsonPropertyName("native_code")]
    public int? NativeCode { get; init; }

    [JsonPropertyName("details")]
    public Dictionary<string, object?> Details { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("diagnostics")]
    public string? Diagnostics { get; init; }
}

public class JsonWarning
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public class JsonResponse
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; init; }

    [JsonPropertyName("data")]
    public object? Data { get; init; }

    [JsonPropertyName("warnings")]
    public List<JsonWarning> Warnings { get; init; } = new();

    [JsonPropertyName("error")]
    public JsonError? Error { get; init; }
}
