using System.Text.Json;
using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Commands;

[Collection(InteractionCommandConsoleCollection.Name)]
public class CommandLineContractTests
{
    [Theory]
    [InlineData("wait-for", "element", "--locator", "{}")]
    [InlineData("wait-for", "--condition", "element", "--locator", "{}")]
    [InlineData("wait-for", "element", "--condition", "element", "--locator", "{}")]
    public async Task WaitFor_AcceptsBothConditionForms(params string[] args)
    {
        var result = await InvokeCli(args);

        Assert.Equal(0, result.ExitCode);
        using var response = JsonDocument.Parse(result.Stdout);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task WaitFor_ConflictingConditionForms_ReturnsInvalidArgument()
    {
        var result = await InvokeCli(
            "wait-for", "element", "--condition", "window", "--locator", "{}");

        Assert.Equal(1, result.ExitCode);
        using var response = JsonDocument.Parse(result.Stdout);
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("wait-for", "element", "unexpected-token")]
    [InlineData("wait-for", "element", "--unknown-option")]
    [InlineData("wait-for", "element", "--locator")]
    public async Task ParseError_WritesSinglePlainJsonResponse(params string[] args)
    {
        var result = await InvokeCli(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(-1, result.Stdout.IndexOf('\u001b'));
        Assert.Single(result.Stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));

        using var response = JsonDocument.Parse(result.Stdout);
        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("parse", response.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            ErrorCodes.InvalidArgument,
            response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.NotEmpty(result.Stderr);
    }

    [Fact]
    public void WindowInfoReader_ReadsCompleteWindowIdentity()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        Assert.NotEqual(IntPtr.Zero, hwnd);

        var info = WindowInfoReader.Read(hwnd);

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        Assert.Equal(UiaAutomationBackend.FormatHwnd(hwnd), info.Hwnd);
        Assert.Equal((int)pid, info.Pid);
        Assert.Equal(ProcessHelper.GetProcessName((int)pid) ?? "", info.ProcessName);
        Assert.False(string.IsNullOrWhiteSpace(info.Title));
        Assert.False(string.IsNullOrWhiteSpace(info.ClassName));
        Assert.Equal(NativeMethods.IsWindowVisible(hwnd), info.Visible);
        Assert.Equal(NativeMethods.IsIconic(hwnd), info.Minimized);

        var rect = UiaAutomationBackend.GetExtendedFrameBounds(hwnd);
        Assert.Equal((rect.X, rect.Y, rect.W, rect.H),
            (info.Rect.X, info.Rect.Y, info.Rect.W, info.Rect.H));
    }

    private static async Task<CliResult> InvokeCli(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var rootCommand = Kagami.Program.BuildRootCommand(
                new StubAutomationBackend(),
                new StubInputBackend(),
                new CaptureService(),
                new StubObservationGuardStore());
            var exitCode = await Kagami.Program.InvokeAsync(rootCommand, args);
            return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    private sealed class StubAutomationBackend : IAutomationBackend
    {
        public Task<List<WindowInfo>> ListWindowsAsync(
            bool visibleOnly,
            string? processName,
            string? title,
            CancellationToken ct) => Task.FromResult(new List<WindowInfo>());

        public Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct) =>
            Task.FromResult<TreeNode?>(new TreeNode());

        public Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct) =>
            Task.FromResult(new List<TreeNode>());

        public Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct) =>
            Task.FromResult<LocatorResolution?>(new LocatorResolution
            {
                Node = new TreeNode(),
                ResolutionMethod = "test"
            });

        public Task<bool> FocusAsync(Locator locator, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class StubInputBackend : IInputBackend
    {
        public Task<InvokeResult> InvokeAsync(Locator locator, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ClickResult> ClickAsync(
            IntPtr targetHwnd,
            int x,
            int y,
            bool rightButton,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<TypeTextResult> TypeTextAsync(TypeTextOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<KeyResult> KeyAsync(KeyOptions options, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ActivateResult> ActivateWindowAsync(IntPtr hwnd, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class StubObservationGuardStore : IObservationGuardStore
    {
        public Task<string> SaveAsync(ObservationGuard guard, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<GuardValidationResult> LoadAndValidateAsync(string guardPath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CleanupExpiredAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
