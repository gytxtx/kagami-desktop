using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class InteractionCommands
{
    private readonly IInputBackend _input;
    private readonly IAutomationBackend _automation;
    private readonly IObservationGuardStore _guardStore;

    public InteractionCommands(IInputBackend input, IAutomationBackend automation, IObservationGuardStore guardStore)
    {
        _input = input;
        _automation = automation;
        _guardStore = guardStore;
    }

    public async Task<int> ActivateAsync(string hwndStr)
    {
        var writer = new ResponseWriter("activate");

        try
        {
            var hwnd = ParseHwnd(hwndStr);
            if (hwnd == IntPtr.Zero)
                return writer.Fail(ErrorCodes.InvalidArgument, $"Invalid HWND: {hwndStr}");

            var result = await _input.ActivateWindowAsync(hwnd, CancellationToken.None);

            if (!result.Activated)
            {
                return writer.Fail(ErrorCodes.ForegroundActivationDenied,
                    $"Could not bring window to foreground. Foreground is now {result.ForegroundHwnd}.",
                    retryable: true);
            }

            return writer.Success(new { activated = true, foreground_hwnd = result.ForegroundHwnd });
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> InvokeAsync(string locatorJson, string? expectedStatePath)
    {
        var writer = new ResponseWriter("invoke");

        try
        {
            var locator = JsonSerializer.Deserialize<Locator>(locatorJson, JsonConfig.Options)
                          ?? throw new CommandException(ErrorCodes.InvalidArgument, "Could not parse locator JSON.");

            if (expectedStatePath is not null)
            {
                var guardResult = await _guardStore.LoadAndValidateAsync(expectedStatePath, CancellationToken.None);
                if (!guardResult.Valid)
                    return writer.Fail(guardResult.FailureCode!, guardResult.FailureMessage!);
            }

            var result = await _input.InvokeAsync(locator, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> ClickAsync(
        int x,
        int y,
        bool rightButton,
        string? hwndStr,
        string? expectedStatePath)
    {
        var writer = new ResponseWriter("click");

        try
        {
            var target = await ResolvePhysicalMouseTargetAsync(hwndStr, expectedStatePath, writer, "click");
            if (target.ExitCode.HasValue)
                return target.ExitCode.Value;

            var result = await _input.ClickAsync(target.Hwnd!.Value, x, y, rightButton, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> MoveAsync(int x, int y, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("move");

        try
        {
            var target = await ResolvePhysicalMouseTargetAsync(hwndStr, expectedStatePath, writer, "move");
            if (target.ExitCode.HasValue)
                return target.ExitCode.Value;

            var result = await _input.MoveAsync(target.Hwnd!.Value, x, y, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> DoubleClickAsync(
        int x, int y, bool rightButton, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("double-click");

        try
        {
            var target = await ResolvePhysicalMouseTargetAsync(hwndStr, expectedStatePath, writer, "double-click");
            if (target.ExitCode.HasValue)
                return target.ExitCode.Value;

            var result = await _input.DoubleClickAsync(target.Hwnd!.Value, x, y, rightButton, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> ScrollAsync(
        int x, int y, int delta, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("scroll");

        try
        {
            var target = await ResolvePhysicalMouseTargetAsync(hwndStr, expectedStatePath, writer, "scroll");
            if (target.ExitCode.HasValue)
                return target.ExitCode.Value;

            var result = await _input.ScrollAsync(target.Hwnd!.Value, x, y, delta, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> DragAsync(
        int fromX, int fromY, int toX, int toY, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("drag");

        try
        {
            var target = await ResolvePhysicalMouseTargetAsync(hwndStr, expectedStatePath, writer, "drag");
            if (target.ExitCode.HasValue)
                return target.ExitCode.Value;

            var result = await _input.DragAsync(
                target.Hwnd!.Value, fromX, fromY, toX, toY, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> TypeTextAsync(
        string text, string mode, bool allowClipboard, string? locatorJson, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("type-text");

        try
        {
            if (expectedStatePath is not null)
            {
                var guardResult = await _guardStore.LoadAndValidateAsync(expectedStatePath, CancellationToken.None);
                if (!guardResult.Valid)
                    return writer.Fail(guardResult.FailureCode!, guardResult.FailureMessage!);
            }

            var interactionMode = mode switch
            {
                "value" => InteractionMode.Semantic,
                "keyboard" => InteractionMode.Physical,
                _ => InteractionMode.Auto
            };

            Locator? locator = null;
            if (locatorJson is not null)
                locator = JsonSerializer.Deserialize<Locator>(locatorJson, JsonConfig.Options);

            IntPtr? hwnd = null;
            if (hwndStr is not null)
                hwnd = ParseRequiredHwnd(hwndStr, "HWND");

            if (interactionMode == InteractionMode.Physical && !hwnd.HasValue)
            {
                return writer.Fail(
                    ErrorCodes.InvalidArgument,
                    "A target HWND is required for keyboard text input.");
            }

            var options = new TypeTextOptions
            {
                Text = text,
                Mode = interactionMode,
                AllowClipboard = allowClipboard,
                Locator = locator,
                Hwnd = hwnd
            };

            var result = await _input.TypeTextAsync(options, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    public async Task<int> KeyAsync(string keys, string? hwndStr, string? expectedStatePath)
    {
        var writer = new ResponseWriter("key");

        try
        {
            if (expectedStatePath is not null)
            {
                var guardResult = await _guardStore.LoadAndValidateAsync(expectedStatePath, CancellationToken.None);
                if (!guardResult.Valid)
                    return writer.Fail(guardResult.FailureCode!, guardResult.FailureMessage!);
            }

            if (hwndStr is null)
                return writer.Fail(ErrorCodes.InvalidArgument, "A target HWND is required for physical key input.");

            var hwnd = ParseRequiredHwnd(hwndStr, "HWND");

            var options = new KeyOptions
            {
                Keys = keys,
                Hwnd = hwnd
            };

            var result = await _input.KeyAsync(options, CancellationToken.None);
            return writer.Success(result);
        }
        catch (CommandException ex)
        {
            return writer.Fail(ex.ErrorCode, ex.Message, ex.Retryable, ex.NativeCode,
                details: new Dictionary<string, object?>(ex.Details), exitCode: ex.ExitCode);
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

    private async Task<(IntPtr? Hwnd, int? ExitCode)> ResolvePhysicalMouseTargetAsync(
        string? hwndStr,
        string? expectedStatePath,
        ResponseWriter writer,
        string command)
    {
        ObservationGuard? guard = null;
        if (expectedStatePath is not null)
        {
            var guardResult = await _guardStore.LoadAndValidateAsync(expectedStatePath, CancellationToken.None);
            if (!guardResult.Valid)
                return (null, writer.Fail(guardResult.FailureCode!, guardResult.FailureMessage!));

            guard = guardResult.Guard
                ?? throw new CommandException(
                    ErrorCodes.StaleObservation,
                    "Validated guard did not include target window metadata.");
        }

        var explicitHwnd = ParseOptionalHwnd(hwndStr);
        IntPtr? guardHwnd = guard is null ? null : ParseRequiredHwnd(guard.Hwnd, "guard HWND");

        if (explicitHwnd.HasValue && guardHwnd.HasValue && explicitHwnd.Value != guardHwnd.Value)
        {
            return (null, writer.Fail(
                ErrorCodes.StaleObservation,
                $"Explicit HWND {FormatHwnd(explicitHwnd.Value)} does not match guard HWND {FormatHwnd(guardHwnd.Value)}."));
        }

        var targetHwnd = explicitHwnd ?? guardHwnd;
        return targetHwnd.HasValue
            ? (targetHwnd, null)
            : (null, writer.Fail(
                ErrorCodes.InvalidArgument,
                $"A target HWND or validated observation guard is required for physical {command}."));
    }

    private static IntPtr? ParseOptionalHwnd(string? hwndStr) =>
        hwndStr is null ? null : ParseRequiredHwnd(hwndStr, "HWND");

    private static IntPtr ParseRequiredHwnd(string hwndStr, string description)
    {
        var hwnd = ParseHwnd(hwndStr);
        if (hwnd == IntPtr.Zero)
            throw new CommandException(ErrorCodes.InvalidArgument, $"Invalid {description}: {hwndStr}");

        return hwnd;
    }

    private static string FormatHwnd(IntPtr hwnd) => $"0x{hwnd:x}";
}
