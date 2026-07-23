using System.Text.Json;
using Kagami.Protocol;

namespace Kagami.Tests.Backends;

public class ResponseWriterTests
{
    [Fact]
    public void Success_ReturnsExitCodeZero()
    {
        var writer = new Kagami.Backends.ResponseWriter("test-command");
        var code = writer.Success(new { result = "ok" });
        Assert.Equal(0, code);
    }

    [Fact]
    public void Success_ProducesValidJsonOutput()
    {
        var originalOut = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);

            var writer = new Kagami.Backends.ResponseWriter("screenshot");
            writer.Success(new { path = "/tmp/img.png" });

            var output = sw.ToString().Trim();
            Assert.NotEmpty(output);

            var deserialized = JsonSerializer.Deserialize<JsonResponse>(output, JsonConfig.Options);
            Assert.NotNull(deserialized);
            Assert.True(deserialized!.Success);
            Assert.Equal("screenshot", deserialized.Command);
            Assert.True(deserialized.ElapsedMs >= 0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Fail_ReturnsSpecifiedExitCode()
    {
        var writer = new Kagami.Backends.ResponseWriter("fail-test");
        var code = writer.Fail("ERR", "msg", exitCode: 42);
        Assert.Equal(42, code);
    }

    [Fact]
    public void Fail_IncludesErrorDetails()
    {
        var originalOut = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);

            var writer = new Kagami.Backends.ResponseWriter("click");
            writer.Fail("LOCATOR_NOT_FOUND", "Could not resolve locator.",
                retryable: true, nativeCode: 123);

            var output = sw.ToString().Trim();
            using var document = JsonDocument.Parse(output);
            var error = document.RootElement.GetProperty("error");

            Assert.Equal("LOCATOR_NOT_FOUND", error.GetProperty("code").GetString());
            Assert.True(error.GetProperty("retryable").GetBoolean());
            Assert.Equal(123, error.GetProperty("native_code").GetInt32());
            Assert.Equal(JsonValueKind.Object, error.GetProperty("details").ValueKind);
            Assert.False(error.TryGetProperty("detais", out _));
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void FatalException_ReturnsSafeJsonAndWritesDiagnosticsToStderr()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var writer = new Kagami.Backends.ResponseWriter("crash");
            var code = writer.FatalException(new InvalidOperationException("Something broke"));

            Assert.Equal(2, code);
            using var document = JsonDocument.Parse(stdout.ToString());
            var error = document.RootElement.GetProperty("error");
            Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            Assert.False(error.TryGetProperty("diagnostics", out _));
            Assert.DoesNotContain("Something broke", stdout.ToString());
            Assert.Contains("Something broke", stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void ResponseWriter_ReportsElapsedTime()
    {
        var originalOut = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);

            var writer = new Kagami.Backends.ResponseWriter("timed-op");
            Thread.Sleep(10);
            writer.Success();

            var output = sw.ToString().Trim();
            var deserialized = JsonSerializer.Deserialize<JsonResponse>(output, JsonConfig.Options);

            Assert.NotNull(deserialized);
            Assert.True(deserialized!.ElapsedMs >= 10,
                $"Elapsed time {deserialized.ElapsedMs}ms should be at least ~10ms");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
