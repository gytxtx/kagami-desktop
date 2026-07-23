using System.Text.Json;
using Kagami.Backends;
using Kagami.Commands;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Commands;

[Collection(InteractionCommandConsoleCollection.Name)]
public class TreeQueryCommandTests
{
    [Theory]
    [InlineData("none", false, false)]
    [InlineData("interactive", true, false)]
    [InlineData("all", true, true)]
    public void LocatorPolicy_MatchesRequestedMode(
        string mode,
        bool interactiveHasLocator,
        bool textHasLocator)
    {
        var interactive = new TreeNode { Patterns = new List<string> { "invoke" } };
        var text = new TreeNode { ControlType = "Text" };

        Assert.Equal(
            interactiveHasLocator,
            TreeOutputPolicy.ShouldIncludeLocator(mode, interactive));
        Assert.Equal(
            textHasLocator,
            TreeOutputPolicy.ShouldIncludeLocator(mode, text));
    }

    [Theory]
    [InlineData("invoke", true)]
    [InlineData("value", true)]
    [InlineData("toggle", true)]
    [InlineData("expand_collapse", true)]
    [InlineData("selection_item", true)]
    [InlineData("scroll", false)]
    [InlineData("text", false)]
    public void InteractivePolicy_UsesOnlyApprovedPatterns(string pattern, bool expected)
    {
        var node = new TreeNode { Patterns = new List<string> { pattern } };

        Assert.Equal(expected, TreeOutputPolicy.IsInteractive(node));
    }

    [Fact]
    public void InteractivePolicy_TreatsKeyboardFocusableNodeAsInteractive()
    {
        var node = new TreeNode { IsKeyboardFocusable = true };

        Assert.True(TreeOutputPolicy.IsInteractive(node));
    }

    [Fact]
    public void Apply_InteractiveOnly_PreservesAncestorsAndResponseChildCounts()
    {
        var interactiveLocator = new Locator();
        var root = new TreeNode
        {
            Locator = new Locator(),
            ChildrenCount = 2,
            ChildrenTruncated = true,
            Children = new List<TreeNode>
            {
                new()
                {
                    Name = "ancestor",
                    Locator = new Locator(),
                    ChildrenCount = 1,
                    Children = new List<TreeNode>
                    {
                        new()
                        {
                            Name = "action",
                            Patterns = new List<string> { "invoke" },
                            Locator = interactiveLocator
                        }
                    }
                },
                new() { Name = "plain text", ControlType = "Text", Locator = new Locator() }
            }
        };

        var result = TreeOutputPolicy.Apply(root, interactiveOnly: true, includeLocators: "interactive");

        Assert.Single(result.Children);
        Assert.Equal(1, result.ChildrenCount);
        Assert.True(result.ChildrenTruncated);
        Assert.Null(result.Locator);

        var ancestor = Assert.Single(result.Children);
        Assert.Equal("ancestor", ancestor.Name);
        Assert.Equal(1, ancestor.ChildrenCount);
        Assert.Null(ancestor.Locator);

        var action = Assert.Single(ancestor.Children);
        Assert.Same(interactiveLocator, action.Locator);
    }

    [Fact]
    public void Apply_WithoutLocators_OmitsLocatorPropertyFromJson()
    {
        var result = TreeOutputPolicy.Apply(
            new TreeNode { Locator = new Locator() },
            interactiveOnly: false,
            includeLocators: "none");

        var json = JsonSerializer.Serialize(result, JsonConfig.Options);

        Assert.DoesNotContain("\"locator\"", json);
    }

    [Fact]
    public async Task GetTree_MultipleStartSelectors_FailsBeforeCallingBackend()
    {
        var automation = new RecordingAutomationBackend();
        var command = new GetTreeCommand(automation);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            "not-a-window",
            depth: 1,
            maxNodes: 20,
            view: "control",
            path: "0",
            runtimeId: "42.1",
            locatorJson: null,
            interactiveOnly: false,
            includeLocators: "all"));

        Assert.NotEqual(0, exitCode);
        Assert.Null(automation.TreeOptions);
        AssertErrorCode(output, ErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task GetTree_LocatorStart_ForwardsOutputOptions()
    {
        var automation = new RecordingAutomationBackend { TreeResult = new TreeNode() };
        var command = new GetTreeCommand(automation);
        var locator = new Locator
        {
            View = "raw",
            Window = new WindowRef { Hwnd = ExistingHwnd() }
        };

        var (exitCode, _) = await CaptureOutputAsync(() => command.RunAsync(
            locator.Window.Hwnd,
            depth: 3,
            maxNodes: 40,
            view: "raw",
            path: null,
            runtimeId: null,
            locatorJson: JsonSerializer.Serialize(locator, JsonConfig.Options),
            interactiveOnly: true,
            includeLocators: "none"));

        Assert.Equal(0, exitCode);
        Assert.NotNull(automation.TreeOptions);
        Assert.Equal("raw", automation.TreeOptions!.View);
        Assert.True(automation.TreeOptions.InteractiveOnly);
        Assert.Equal("none", automation.TreeOptions.IncludeLocators);
        Assert.Equal(locator.Window.Hwnd, automation.TreeOptions.StartLocator!.Window.Hwnd);
    }

    [Fact]
    public async Task GetTree_CommandFailure_PreservesStructuredDetails()
    {
        var automation = new RecordingAutomationBackend
        {
            TreeException = new CommandException(
                ErrorCodes.LocatorAmbiguous,
                "Locator matched multiple elements.",
                details: new Dictionary<string, object?>
                {
                    ["segment_index"] = 2,
                    ["candidate_count"] = 3
                })
        };
        var command = new GetTreeCommand(automation);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            ExistingHwnd(),
            depth: 3,
            maxNodes: 40,
            view: "control",
            path: null,
            runtimeId: null,
            locatorJson: null,
            interactiveOnly: false,
            includeLocators: "all"));

        Assert.NotEqual(0, exitCode);
        using var response = JsonDocument.Parse(output);
        var details = response.RootElement.GetProperty("error").GetProperty("details");
        Assert.Equal(2, details.GetProperty("segment_index").GetInt32());
        Assert.Equal(3, details.GetProperty("candidate_count").GetInt32());
    }

    [Fact]
    public async Task Find_WithoutFilter_FailsBeforeCallingBackend()
    {
        var automation = new RecordingAutomationBackend();
        var command = new FindCommand(automation);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            "not-a-window",
            name: null,
            automationId: null,
            controlType: null,
            className: null,
            maxResults: 20,
            view: "control"));

        Assert.NotEqual(0, exitCode);
        Assert.Null(automation.FindOptions);
        AssertErrorCode(output, ErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task Find_WithNameFilter_ForwardsViewAndMaxResults()
    {
        var automation = new RecordingAutomationBackend
        {
            FindResult = new List<TreeNode> { new() { Name = "Save" } }
        };
        var command = new FindCommand(automation);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            ExistingHwnd(),
            name: "Save",
            automationId: null,
            controlType: "Button",
            className: null,
            maxResults: 7,
            view: "content"));

        Assert.Equal(0, exitCode);
        Assert.NotNull(automation.FindOptions);
        Assert.Equal("Save", automation.FindOptions!.Name);
        Assert.Equal("Button", automation.FindOptions.ControlType);
        Assert.Equal(7, automation.FindOptions.MaxResults);
        Assert.Equal("content", automation.FindOptions.View);

        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            "Save",
            response.RootElement.GetProperty("data")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Observe_InvalidLocatorMode_FailsBeforeWindowOrCaptureWork()
    {
        var command = new ObserveCommand(null!, null!, null!);

        var (exitCode, output) = await CaptureOutputAsync(() => command.RunAsync(
            "not-a-window",
            depth: 1,
            maxNodes: 20,
            view: "control",
            interactiveOnly: false,
            includeLocators: "unknown",
            captureMode: "auto",
            allowSemanticFallback: false,
            outputPath: null));

        Assert.NotEqual(0, exitCode);
        using var response = JsonDocument.Parse(output);
        Assert.Contains(
            "--include-locators",
            response.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    private static string ExistingHwnd()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        Assert.NotEqual(IntPtr.Zero, hwnd);
        Assert.True(NativeMethods.IsWindow(hwnd));
        return UiaAutomationBackend.FormatHwnd(hwnd);
    }

    private static void AssertErrorCode(string output, string expected)
    {
        using var response = JsonDocument.Parse(output);
        Assert.Equal(
            expected,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

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

    private sealed class RecordingAutomationBackend : IAutomationBackend
    {
        public GetTreeOptions? TreeOptions { get; private set; }
        public FindOptions? FindOptions { get; private set; }
        public TreeNode? TreeResult { get; init; }
        public CommandException? TreeException { get; init; }
        public List<TreeNode> FindResult { get; init; } = new();

        public Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct)
        {
            TreeOptions = options;
            if (TreeException is not null)
                throw TreeException;

            return Task.FromResult(TreeResult);
        }

        public Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct)
        {
            FindOptions = options;
            return Task.FromResult(FindResult);
        }

        public Task<List<WindowInfo>> ListWindowsAsync(
            bool visibleOnly,
            string? processName,
            string? title,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> FocusAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
