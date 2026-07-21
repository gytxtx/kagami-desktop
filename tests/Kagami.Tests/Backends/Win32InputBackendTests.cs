using Kagami.Backends;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Tests.Backends;

/// <summary>
/// Tests for Win32InputBackend that don't require actual mouse/keyboard injection.
/// Tests that require physical input are marked as manual or skipped.
/// </summary>
public class Win32InputBackendTests
{
    private readonly UiaAutomationBackend _automation;
    private readonly TempFileObservationGuardStore _guardStore;
    private readonly Win32InputBackend _input;

    public Win32InputBackendTests()
    {
        _guardStore = new TempFileObservationGuardStore();
        _automation = new UiaAutomationBackend(_guardStore);
        _input = new Win32InputBackend(_automation, _guardStore);
    }

    [Fact]
    public void ActivateWindow_WithVisibleWindow_Succeeds()
    {
        var windows = _automation.ListWindowsAsync(true, null, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var target = windows.FirstOrDefault(w => w.Title.Length > 0 && w.Rect.W > 100);
        if (target is null) return;

        var hwnd = UiaAutomationBackend.ParseHwnd(target.Hwnd);
        var result = _input.ActivateWindowAsync(hwnd, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Result should indicate whether activation succeeded
        Assert.NotNull(result);
        Assert.NotEmpty(result.ForegroundHwnd);
    }

    [Fact]
    public void ActivateWindow_WithZeroHwnd_Fails()
    {
        var result = _input.ActivateWindowAsync(IntPtr.Zero, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.Activated);
    }

    [Fact]
    public void Key_WithValidCombination_DoesNotThrow()
    {
        // This test verifies that key combination parsing works.
        // We don't actually send input — we just ensure the parsing is correct
        // by checking that the command runs.

        // Note: actual SendInput will happen, but with harmless keys
        var ex = Record.Exception(() =>
        {
            _input.KeyAsync(new KeyOptions { Keys = "CTRL+L" }, CancellationToken.None)
                .GetAwaiter().GetResult();
        });

        // May fail if no window has focus to receive input — but should not
        // fail with InvalidArgument (parsing errors)
        Assert.Null(ex);
    }

    [Fact]
    public void Key_WithInvalidKey_Fails()
    {
        var ex = Assert.Throws<CommandException>(() =>
        {
            _input.KeyAsync(new KeyOptions { Keys = "NONEXISTENT_KEY" }, CancellationToken.None)
                .GetAwaiter().GetResult();
        });

        Assert.Equal(ErrorCodes.InvalidArgument, ex.ErrorCode);
    }

    [Fact]
    public void Key_WithSingleLetter_ParsesCorrectly()
    {
        var ex = Record.Exception(() =>
        {
            _input.KeyAsync(new KeyOptions { Keys = "A" }, CancellationToken.None)
                .GetAwaiter().GetResult();
        });

        // Should not produce parsing error (may fail on actual injection)
        Assert.Null(ex);
    }

    private void Dispose()
    {
        _input.Dispose();
        _automation.Dispose();
    }
}
