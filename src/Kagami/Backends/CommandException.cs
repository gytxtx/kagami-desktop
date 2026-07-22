using System.Collections.ObjectModel;

namespace Kagami.Backends;

/// <summary>
/// Exception raised within commands that should translate to a structured JSON error response.
/// </summary>
public class CommandException : Exception
{
    public string ErrorCode { get; }
    public int ExitCode { get; }
    public bool Retryable { get; }
    public int? NativeCode { get; }
    public IReadOnlyDictionary<string, object?> Details { get; }

    public CommandException(
        string code,
        string message,
        bool retryable = false,
        int? nativeCode = null,
        int exitCode = 1,
        IReadOnlyDictionary<string, object?>? details = null)
        : base(message)
    {
        ErrorCode = code;
        ExitCode = exitCode;
        Retryable = retryable;
        NativeCode = nativeCode;
        Details = new ReadOnlyDictionary<string, object?>(
            details is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(details));
    }
}
