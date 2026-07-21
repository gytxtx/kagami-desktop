using Kagami.Backends;
using Kagami.Protocol;

namespace Kagami.Commands;

public class ListWindowsCommand
{
    private readonly IAutomationBackend _automation;

    public ListWindowsCommand(IAutomationBackend automation)
    {
        _automation = automation;
    }

    public async Task<int> RunAsync(bool visibleOnly, string? processName, string? title)
    {
        var writer = new ResponseWriter("list-windows");

        try
        {
            var windows = await _automation.ListWindowsAsync(visibleOnly, processName, title, CancellationToken.None);
            return writer.Success(windows);
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
