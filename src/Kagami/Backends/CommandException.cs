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

    public CommandException(string code, string message, bool retryable = false, int? nativeCode = null, int exitCode = 1)
        : base(message)
    {
        ErrorCode = code;
        ExitCode = exitCode;
        Retryable = retryable;
        NativeCode = nativeCode;
    }
}
