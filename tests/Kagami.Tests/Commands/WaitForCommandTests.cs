using System.Text.Json;
using Kagami.Backends;
using Kagami.Commands;
using Kagami.Protocol;

namespace Kagami.Tests.Commands;

[Collection(InteractionCommandConsoleCollection.Name)]
public class WaitForCommandTests
{
    [Fact]
    public async Task Element_NotFoundThenResolved_ContinuesPollingAndSucceeds()
    {
        var automation = new SequencedAutomationBackend(
            () => throw new CommandException(ErrorCodes.LocatorNotFound, "not found"),
            Resolved);
        var command = new WaitForCommand(automation, new CaptureService());

        var (exitCode, _) = await CaptureOutputAsync(() => RunAsync(command, "element"));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, automation.ResolveCalls);
    }

    [Fact]
    public async Task ElementGone_NotFoundImmediately_Succeeds()
    {
        var automation = new SequencedAutomationBackend(
            () => throw new CommandException(ErrorCodes.LocatorNotFound, "not found"));
        var command = new WaitForCommand(automation, new CaptureService());

        var (exitCode, _) = await CaptureOutputAsync(() => RunAsync(command, "element-gone"));

        Assert.Equal(0, exitCode);
        Assert.Equal(1, automation.ResolveCalls);
    }

    [Fact]
    public async Task Element_Ambiguous_PropagatesStructuredFailure()
    {
        var automation = new SequencedAutomationBackend(
            () => throw new CommandException(ErrorCodes.LocatorAmbiguous, "ambiguous"));
        var command = new WaitForCommand(automation, new CaptureService());

        var (exitCode, output) = await CaptureOutputAsync(() => RunAsync(command, "element"));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(1, automation.ResolveCalls);
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            ErrorCodes.LocatorAmbiguous,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Property_NotFoundThenResolved_ContinuesPollingAndSucceeds()
    {
        var automation = new SequencedAutomationBackend(
            () => throw new CommandException(ErrorCodes.LocatorNotFound, "not found"),
            () => new LocatorResolution
            {
                Node = new TreeNode { IsEnabled = true },
                ResolutionMethod = "test"
            });
        var command = new WaitForCommand(automation, new CaptureService());

        var (exitCode, _) = await CaptureOutputAsync(() =>
            RunAsync(command, "property", property: "is_enabled", equalsValue: "true"));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, automation.ResolveCalls);
    }

    private static Task<int> RunAsync(
        WaitForCommand command,
        string condition,
        string? property = null,
        string? equalsValue = null) =>
        command.RunAsync(
            condition,
            hwndStr: null,
            processName: null,
            title: null,
            locatorJson: JsonSerializer.Serialize(new Locator(), JsonConfig.Options),
            property,
            equalsValue,
            timeoutMs: 1_000,
            pollIntervalMs: 1,
            consecutiveCount: 1,
            screenshotThreshold: 0,
            screenshotRegion: null,
            expectedStatePath: null);

    private static LocatorResolution? Resolved() => new()
    {
        Node = new TreeNode(),
        ResolutionMethod = "test"
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

    private sealed class SequencedAutomationBackend(params Func<LocatorResolution?>[] resolutions)
        : IAutomationBackend
    {
        private readonly Queue<Func<LocatorResolution?>> _resolutions = new(resolutions);

        public int ResolveCalls { get; private set; }

        public Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct)
        {
            ResolveCalls++;
            return Task.FromResult(_resolutions.Dequeue()());
        }

        public Task<List<WindowInfo>> ListWindowsAsync(
            bool visibleOnly,
            string? processName,
            string? title,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> FocusAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
