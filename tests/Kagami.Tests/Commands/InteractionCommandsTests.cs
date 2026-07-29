using System.Text.Json;
using Kagami.Backends;
using Kagami.Commands;
using Kagami.Protocol;

namespace Kagami.Tests.Commands;

[CollectionDefinition("Interaction command console", DisableParallelization = true)]
public sealed class InteractionCommandConsoleCollection
{
    public const string Name = "Interaction command console";
}

[Collection(InteractionCommandConsoleCollection.Name)]
public class InteractionCommandsTests
{
    [Fact]
    public async Task Click_WithoutHwndOrGuard_FailsWithoutCallingBackend()
    {
        var input = new RecordingInputBackend();
        var commands = CreateCommands(input);

        var (exitCode, output) = await CaptureOutputAsync(
            () => commands.ClickAsync(100, 200, false, null, null));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.ClickCalls);
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Click_WithValidatedGuard_DerivesTargetHwnd()
    {
        var input = new RecordingInputBackend();
        var guardStore = new StubGuardStore
        {
            Result = ValidGuard("0x1234")
        };
        var commands = CreateCommands(input, guardStore);

        var exitCode = await commands.ClickAsync(100, 200, false, null, "guard.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(new IntPtr(0x1234), input.ClickTargetHwnd);
        Assert.Equal(1, input.ClickCalls);
    }

    [Fact]
    public async Task Click_WithConflictingExplicitAndGuardHwnd_FailsWithoutCallingBackend()
    {
        var input = new RecordingInputBackend();
        var guardStore = new StubGuardStore
        {
            Result = ValidGuard("0x1234")
        };
        var commands = CreateCommands(input, guardStore);

        var (exitCode, output) = await CaptureOutputAsync(
            () => commands.ClickAsync(100, 200, false, "0x5678", "guard.json"));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.ClickCalls);
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            ErrorCodes.StaleObservation,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("double-click")]
    [InlineData("scroll")]
    [InlineData("drag")]
    public async Task PhysicalMouseCommand_WithoutHwndOrGuard_FailsWithoutCallingBackend(string command)
    {
        var input = new RecordingInputBackend();
        var commands = CreateCommands(input);

        var (exitCode, output) = await CaptureOutputAsync(
            () => ExecutePhysicalMouseCommandAsync(commands, command, null, null));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.GetMouseCalls(command));
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("double-click")]
    [InlineData("scroll")]
    [InlineData("drag")]
    public async Task PhysicalMouseCommand_WithValidatedGuard_DerivesTargetHwnd(string command)
    {
        var input = new RecordingInputBackend();
        var guardStore = new StubGuardStore { Result = ValidGuard("0x1234") };
        var commands = CreateCommands(input, guardStore);

        var exitCode = await ExecutePhysicalMouseCommandAsync(commands, command, null, "guard.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, input.GetMouseCalls(command));
        Assert.Equal(new IntPtr(0x1234), input.GetMouseTarget(command));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("double-click")]
    [InlineData("scroll")]
    [InlineData("drag")]
    public async Task PhysicalMouseCommand_WithConflictingExplicitAndGuardHwnd_FailsWithoutCallingBackend(string command)
    {
        var input = new RecordingInputBackend();
        var guardStore = new StubGuardStore { Result = ValidGuard("0x1234") };
        var commands = CreateCommands(input, guardStore);

        var (exitCode, output) = await CaptureOutputAsync(
            () => ExecutePhysicalMouseCommandAsync(commands, command, "0x5678", "guard.json"));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.GetMouseCalls(command));
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            ErrorCodes.StaleObservation,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Key_WithoutHwnd_FailsWithoutCallingBackend()
    {
        var input = new RecordingInputBackend();
        var commands = CreateCommands(input);

        var exitCode = await commands.KeyAsync("CTRL+L", null, null);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.KeyCalls);
    }

    [Fact]
    public async Task TypeText_KeyboardWithoutHwnd_FailsWithoutCallingBackend()
    {
        var input = new RecordingInputBackend();
        var commands = CreateCommands(input);

        var exitCode = await commands.TypeTextAsync("hello", "keyboard", false, null, null, null);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(0, input.TypeTextCalls);
    }

    [Fact]
    public async Task TypeText_ValueWithoutHwnd_ReachesSemanticBackend()
    {
        var input = new RecordingInputBackend();
        var commands = CreateCommands(input);

        var exitCode = await commands.TypeTextAsync("hello", "value", false, null, null, null);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, input.TypeTextCalls);
        Assert.Equal(InteractionMode.Semantic, input.LastTypeTextOptions!.Mode);
        Assert.Null(input.LastTypeTextOptions.Hwnd);
    }

    private static InteractionCommands CreateCommands(
        RecordingInputBackend input,
        StubGuardStore? guardStore = null) =>
        new(input, null!, guardStore ?? new StubGuardStore());

    private static GuardValidationResult ValidGuard(string hwnd) => new()
    {
        Valid = true,
        Guard = new ObservationGuard { Hwnd = hwnd }
    };

    private static Task<int> ExecutePhysicalMouseCommandAsync(
        InteractionCommands commands,
        string command,
        string? hwnd,
        string? expectedStatePath) => command switch
    {
        "move" => commands.MoveAsync(100, 200, hwnd, expectedStatePath),
        "double-click" => commands.DoubleClickAsync(100, 200, false, hwnd, expectedStatePath),
        "scroll" => commands.ScrollAsync(100, 200, -2, hwnd, expectedStatePath),
        "drag" => commands.DragAsync(100, 200, 300, 400, hwnd, expectedStatePath),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };

    private static async Task<(int ExitCode, string Output)> CaptureOutputAsync(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        try
        {
            using var output = new StringWriter();
            Console.SetOut(output);
            var exitCode = await action();
            return (exitCode, output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed class RecordingInputBackend : IInputBackend
    {
        public int ClickCalls { get; private set; }
        public int KeyCalls { get; private set; }
        public int TypeTextCalls { get; private set; }
        public IntPtr? ClickTargetHwnd { get; private set; }
        public int MoveCalls { get; private set; }
        public int DoubleClickCalls { get; private set; }
        public int ScrollCalls { get; private set; }
        public int DragCalls { get; private set; }
        public IntPtr? MoveTargetHwnd { get; private set; }
        public IntPtr? DoubleClickTargetHwnd { get; private set; }
        public IntPtr? ScrollTargetHwnd { get; private set; }
        public IntPtr? DragTargetHwnd { get; private set; }
        public TypeTextOptions? LastTypeTextOptions { get; private set; }

        public int GetMouseCalls(string command) => command switch
        {
            "move" => MoveCalls,
            "double-click" => DoubleClickCalls,
            "scroll" => ScrollCalls,
            "drag" => DragCalls,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        public IntPtr? GetMouseTarget(string command) => command switch
        {
            "move" => MoveTargetHwnd,
            "double-click" => DoubleClickTargetHwnd,
            "scroll" => ScrollTargetHwnd,
            "drag" => DragTargetHwnd,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };

        public Task<InvokeResult> InvokeAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ClickResult> ClickAsync(
            IntPtr targetHwnd,
            int x,
            int y,
            bool rightButton,
            CancellationToken ct)
        {
            ClickCalls++;
            ClickTargetHwnd = targetHwnd;
            return Task.FromResult(new ClickResult
            {
                X = x,
                Y = y,
                RightButton = rightButton,
                Interaction = new InteractionResult()
            });
        }

        public Task<MoveResult> MoveAsync(IntPtr targetHwnd, int x, int y, CancellationToken ct)
        {
            MoveCalls++;
            MoveTargetHwnd = targetHwnd;
            return Task.FromResult(new MoveResult { X = x, Y = y, Interaction = new InteractionResult() });
        }

        public Task<DoubleClickResult> DoubleClickAsync(
            IntPtr targetHwnd, int x, int y, bool rightButton, CancellationToken ct)
        {
            DoubleClickCalls++;
            DoubleClickTargetHwnd = targetHwnd;
            return Task.FromResult(new DoubleClickResult
            {
                X = x,
                Y = y,
                RightButton = rightButton,
                Interaction = new InteractionResult()
            });
        }

        public Task<ScrollResult> ScrollAsync(IntPtr targetHwnd, int x, int y, int delta, CancellationToken ct)
        {
            ScrollCalls++;
            ScrollTargetHwnd = targetHwnd;
            return Task.FromResult(new ScrollResult
            {
                X = x,
                Y = y,
                Delta = delta,
                Interaction = new InteractionResult()
            });
        }

        public Task<DragResult> DragAsync(
            IntPtr targetHwnd, int fromX, int fromY, int toX, int toY, CancellationToken ct)
        {
            DragCalls++;
            DragTargetHwnd = targetHwnd;
            return Task.FromResult(new DragResult
            {
                FromX = fromX,
                FromY = fromY,
                ToX = toX,
                ToY = toY,
                Interaction = new InteractionResult()
            });
        }

        public Task<TypeTextResult> TypeTextAsync(TypeTextOptions options, CancellationToken ct)
        {
            TypeTextCalls++;
            LastTypeTextOptions = options;
            return Task.FromResult(new TypeTextResult
            {
                Text = options.Text,
                Interaction = new InteractionResult
                {
                    ModeRequested = options.Mode.ToString().ToLowerInvariant(),
                    ModeActual = "uia-value-pattern",
                    PhysicalInputGenerated = false
                }
            });
        }

        public Task<KeyResult> KeyAsync(KeyOptions options, CancellationToken ct)
        {
            KeyCalls++;
            return Task.FromResult(new KeyResult
            {
                Keys = options.Keys,
                Interaction = new InteractionResult()
            });
        }

        public Task<ActivateResult> ActivateWindowAsync(IntPtr hwnd, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StubGuardStore : IObservationGuardStore
    {
        public GuardValidationResult Result { get; init; } = new()
        {
            Valid = false,
            FailureCode = ErrorCodes.StaleObservation,
            FailureMessage = "Guard unavailable."
        };

        public Task<string> SaveAsync(ObservationGuard guard, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, CancellationToken ct) =>
            Task.FromResult(Result);

        public Task CleanupExpiredAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
