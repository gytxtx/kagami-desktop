using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Commands;

public class FindCommand
{
    private readonly IAutomationBackend _automation;

    public FindCommand(IAutomationBackend automation)
    {
        _automation = automation;
    }

    public async Task<int> RunAsync(
        string hwndStr,
        string? name,
        string? automationId,
        string? controlType,
        string? className,
        int maxResults,
        string view)
    {
        var writer = new ResponseWriter("find");

        try
        {
            if (new[] { name, automationId, controlType, className }
                .All(string.IsNullOrWhiteSpace))
            {
                return writer.Fail(
                    ErrorCodes.InvalidArgument,
                    "At least one of --name, --automation-id, --control-type, or --class-name is required.");
            }

            if (maxResults <= 0)
                return writer.Fail(ErrorCodes.InvalidArgument, "--max-results must be greater than zero.");

            var hwnd = UiaAutomationBackend.ParseHwnd(hwndStr);
            if (hwnd == IntPtr.Zero)
                return writer.Fail(ErrorCodes.InvalidArgument, $"Invalid HWND: {hwndStr}");
            var results = await _automation.FindAsync(new FindOptions
            {
                Hwnd = hwnd,
                Name = name,
                AutomationId = automationId,
                ControlType = controlType,
                ClassName = className,
                MaxResults = maxResults,
                View = view
            }, CancellationToken.None);

            return writer.Success(results);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode, exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }
}
