using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class GetTreeCommand
{
    private readonly IAutomationBackend _automation;

    public GetTreeCommand(IAutomationBackend automation)
    {
        _automation = automation;
    }

    public async Task<int> RunAsync(
        string hwndStr,
        int depth,
        int maxNodes,
        string view,
        string? path,
        string? runtimeId,
        string? locatorJson,
        bool interactiveOnly,
        string includeLocators)
    {
        var writer = new ResponseWriter("get-tree");

        try
        {
            var startSelectorCount = new[] { path, runtimeId, locatorJson }
                .Count(value => !string.IsNullOrWhiteSpace(value));
            if (startSelectorCount > 1)
            {
                return writer.Fail(
                    ErrorCodes.InvalidArgument,
                    "At most one of --path, --runtime-id, or --locator may be provided.");
            }

            if (!TreeOutputPolicy.IsSupportedLocatorMode(includeLocators))
            {
                return writer.Fail(
                    ErrorCodes.InvalidArgument,
                    "--include-locators must be one of: all, interactive, none.");
            }

            Locator? startLocator = null;
            if (!string.IsNullOrWhiteSpace(locatorJson))
            {
                startLocator = JsonSerializer.Deserialize<Locator>(locatorJson, JsonConfig.Options)
                    ?? throw new CommandException(
                        ErrorCodes.InvalidArgument,
                        "Could not parse locator JSON.");
            }

            var hwnd = ParseHwnd(hwndStr);
            if (hwnd == IntPtr.Zero)
                return writer.Fail(ErrorCodes.InvalidArgument, $"Invalid HWND: {hwndStr}");

            var options = new GetTreeOptions
            {
                Hwnd = hwnd,
                MaxDepth = depth,
                MaxNodes = maxNodes,
                View = view,
                Path = path,
                RuntimeId = runtimeId,
                StartLocator = startLocator,
                InteractiveOnly = interactiveOnly,
                IncludeLocators = includeLocators
            };

            var tree = await _automation.GetTreeAsync(options, CancellationToken.None);
            if (tree is null)
                return writer.Fail(ErrorCodes.ElementNotAvailable, "Could not retrieve UIA tree for this window.");

            return writer.Success(tree);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode, exitCode: ex.ExitCode);
        }
        catch (JsonException ex)
        {
            return writer.Fail(ErrorCodes.InvalidArgument, $"Could not parse locator JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    private static IntPtr ParseHwnd(string hwndStr)
    {
        if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hwndStr = hwndStr[2..];

        if (long.TryParse(hwndStr, System.Globalization.NumberStyles.HexNumber, null, out long val))
            return (IntPtr)val;

        return IntPtr.Zero;
    }
}
