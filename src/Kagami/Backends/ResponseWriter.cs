using System.Diagnostics;
using System.Text.Json;
using Kagami.Protocol;

namespace Kagami.Backends;

/// <summary>
/// Writes structured JSON responses to stdout and logs diagnostic info to stderr.
/// </summary>
public class ResponseWriter
{
    private readonly Stopwatch _sw;
    private readonly string _command;

    public ResponseWriter(string command)
    {
        _sw = Stopwatch.StartNew();
        _command = command;
    }

    public int Success(object? data = null, List<JsonWarning>? warnings = null)
    {
        Write(new JsonResponse
        {
            Success = true,
            Command = _command,
            ElapsedMs = _sw.ElapsedMilliseconds,
            Data = data,
            Warnings = warnings ?? new List<JsonWarning>()
        });
        return 0;
    }

    public int Fail(string code, string message, bool retryable = false, int? nativeCode = null,
                     Dictionary<string, object?>? details = null, int exitCode = 1)
    {
        Write(new JsonResponse
        {
            Success = false,
            Command = _command,
            ElapsedMs = _sw.ElapsedMilliseconds,
            Error = new JsonError
            {
                Code = code,
                Message = message,
                Retryable = retryable,
                NativeCode = nativeCode,
                Details = details ?? new Dictionary<string, object?>()
            }
        });
        return exitCode;
    }

    public int FatalException(Exception ex)
    {
        Write(new JsonResponse
        {
            Success = false,
            Command = _command,
            ElapsedMs = _sw.ElapsedMilliseconds,
            Error = new JsonError
            {
                Code = ErrorCodes.InternalError,
                Message = ex.Message,
                Retryable = false,
                Diagnostics = ex.StackTrace
            }
        });
        return 2;
    }

    private static void Write(JsonResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonConfig.Options);
        Console.Out.WriteLine(json);
        Console.Out.Flush();
    }
}
