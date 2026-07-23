using System.Text.Json;
using Kagami.Protocol;

namespace Kagami.Tests.Protocol;

public class JsonResponseTests
{
    [Fact]
    public void Serialize_NullData_ProducesValidJson()
    {
        var response = new JsonResponse
        {
            SchemaVersion = "1.0",
            Success = true,
            Command = "test",
            ElapsedMs = 42,
            Data = null
        };

        var json = JsonSerializer.Serialize(response, JsonConfig.Options);
        Assert.Contains("\"success\"", json);
        Assert.Contains("\"elapsed_ms\"", json);
        Assert.Contains("\"data\":null", json);
    }

    [Fact]
    public void Serialize_WithError_IncludesAllErrorFields()
    {
        var response = new JsonResponse
        {
            Success = false,
            Command = "click",
            ElapsedMs = 124,
            Error = new JsonError
            {
                Code = ErrorCodes.InputInjectionFailed,
                Message = "SendInput failed",
                Retryable = true,
                NativeCode = 5,
                Details = new Dictionary<string, object?> { ["x"] = 100, ["y"] = 200 }
            }
        };

        var json = JsonSerializer.Serialize(response, JsonConfig.Options);
        using var document = JsonDocument.Parse(json);
        var error = document.RootElement.GetProperty("error");

        Assert.Equal("INPUT_INJECTION_FAILED", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(5, error.GetProperty("native_code").GetInt32());
        Assert.Equal(100, error.GetProperty("details").GetProperty("x").GetInt32());
        Assert.False(error.TryGetProperty("detais", out _));
    }

    [Fact]
    public void Serialize_WithWarnings_IncludesWarningsArray()
    {
        var response = new JsonResponse
        {
            Success = true,
            Command = "screenshot",
            ElapsedMs = 500,
            Warnings = new List<JsonWarning>
            {
                new() { Code = "capture_fallback", Message = "Fell back to DXGI." }
            }
        };

        var json = JsonSerializer.Serialize(response, JsonConfig.Options);
        Assert.Contains("\"warnings\"", json);
        Assert.Contains("capture_fallback", json);
    }
}
