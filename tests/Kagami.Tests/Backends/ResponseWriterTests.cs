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
            var deserialized = JsonSerializer.Deserialize<JsonResponse>(output, JsonConfig.Options);

            Assert.NotNull(deserialized);
            Assert.False(deserialized!.Success);
            Assert.NotNull(deserialized.Error);
            Assert.Equal("LOCATOR_NOT_FOUND", deserialized.Error!.Code);
            Assert.True(deserialized.Error.Retryable);
            Assert.Equal(123, deserialized.Error.NativeCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void FatalException_ReturnsExitCodeTwo()
    {
        var writer = new Kagami.Backends.ResponseWriter("crash");
        var code = writer.FatalException(new InvalidOperationException("Something broke"));
        Assert.Equal(2, code);
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
