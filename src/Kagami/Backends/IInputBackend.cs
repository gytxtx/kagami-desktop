using Kagami.Protocol;

namespace Kagami.Backends;

public enum InteractionMode
{
    Semantic,
    Physical,
    Auto
}

public class TypeTextOptions
{
    public required string Text { get; init; }
    public InteractionMode Mode { get; init; } = InteractionMode.Auto;
    public bool AllowClipboard { get; init; }
    public Locator? Locator { get; init; }
    public IntPtr? Hwnd { get; init; }
}

public class KeyOptions
{
    public required string Keys { get; init; }
    public IntPtr? Hwnd { get; init; }
}

public class InvokeResult
{
    public required InteractionResult Interaction { get; init; }
}

public class ClickResult
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool RightButton { get; init; }
    public required InteractionResult Interaction { get; init; }
}

public class TypeTextResult
{
    public required string Text { get; init; }
    public bool ClipboardUsed { get; init; }
    public bool ClipboardSequenceChanged { get; init; }
    public required InteractionResult Interaction { get; init; }
}

public class KeyResult
{
    public required string Keys { get; init; }
    public required InteractionResult Interaction { get; init; }
}

public interface IInputBackend
{
    /// <summary>Invoke a UIA element via InvokePattern (semantic click).</summary>
    Task<InvokeResult> InvokeAsync(Locator locator, CancellationToken ct);

    /// <summary>Perform a physical mouse click at screen coordinates.</summary>
    Task<ClickResult> ClickAsync(int x, int y, bool rightButton, CancellationToken ct);

    /// <summary>Type text using the specified mode.</summary>
    Task<TypeTextResult> TypeTextAsync(TypeTextOptions options, CancellationToken ct);

    /// <summary>Send a key combination (e.g. "CTRL+L").</summary>
    Task<KeyResult> KeyAsync(KeyOptions options, CancellationToken ct);

    /// <summary>Try to bring a window to the foreground.</summary>
    Task<ActivateResult> ActivateWindowAsync(IntPtr hwnd, CancellationToken ct);
}

public class ActivateResult
{
    public bool Activated { get; init; }
    public string ForegroundHwnd { get; init; } = "";
}
