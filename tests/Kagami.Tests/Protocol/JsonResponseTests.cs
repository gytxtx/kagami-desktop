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
        Assert.Contains("\"code\":\"INPUT_INJECTION_FAILED\"", json);
        Assert.Contains("\"retryable\":true", json);
        Assert.Contains("\"native_code\":5", json);
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
