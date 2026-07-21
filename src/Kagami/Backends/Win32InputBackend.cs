using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Kagami.Protocol;
using Kagami.Utilities;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using static Kagami.Utilities.NativeMethods;

namespace Kagami.Backends;

public class Win32InputBackend : IInputBackend, IDisposable
{
    private readonly IAutomationBackend _automation;
    private readonly UIA3Automation _rawAutomation;
    private readonly IObservationGuardStore _guardStore;

    public Win32InputBackend(IAutomationBackend automation, IObservationGuardStore guardStore)
    {
        _automation = automation;
        _guardStore = guardStore;
        _rawAutomation = new UIA3Automation();
    }

    public Task<InvokeResult> InvokeAsync(Locator locator, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var element = ((UiaAutomationBackend)_automation).ResolveLocatorInternal(locator, ct);
            if (element is null)
                throw new CommandException(ErrorCodes.LocatorNotFound, "Locator could not be resolved.");

            bool semanticSucceeded = false;

            try
            {
                if (element.Patterns.Invoke.IsSupported)
                {
                    element.Patterns.Invoke.Pattern.Invoke();
                    semanticSucceeded = true;
                }
            }
            catch (Exception ex)
            {
                throw new CommandException(ErrorCodes.PatternNotSupported, $"InvokePattern failed: {ex.Message}");
            }

            if (!semanticSucceeded)
            {
                throw new CommandException(ErrorCodes.PatternNotSupported, "Element does not support InvokePattern.");
            }

            return new InvokeResult
            {
                Interaction = new InteractionResult
                {
                    ModeRequested = "semantic",
                    ModeActual = "uia-invoke-pattern",
                    PhysicalInputGenerated = false
                }
            };
        }, ct);
    }

    public Task<ClickResult> ClickAsync(int x, int y, bool rightButton, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            // Validate coordinates are within the virtual desktop
            int vScreenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vScreenY = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vScreenW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vScreenH = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (x < vScreenX || x >= vScreenX + vScreenW ||
                y < vScreenY || y >= vScreenY + vScreenH)
            {
                throw new CommandException(ErrorCodes.InvalidArgument,
                    $"Coordinate ({x},{y}) is outside the virtual desktop ({vScreenX},{vScreenY})–({vScreenX + vScreenW},{vScreenY + vScreenH}).");
            }

            // Move cursor to position with VIRTUALDESK for correct multi-monitor handling
            var inputs = new INPUT[3];

            // Move
            inputs[0] = CreateMouseInput(x, y, (uint)(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE), 0);

            // Button down
            int downFlag = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            inputs[1] = CreateMouseInput(x, y, (uint)(downFlag | MOUSEEVENTF_ABSOLUTE), 0);

            // Button up
            int upFlag = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
            inputs[2] = CreateMouseInput(x, y, (uint)(upFlag | MOUSEEVENTF_ABSOLUTE), 0);

            uint result = SendInput(3, inputs, Marshal.SizeOf<INPUT>());
            if (result == 0)
                throw new CommandException(ErrorCodes.InputInjectionFailed, $"SendInput failed. GetLastError: {Marshal.GetLastWin32Error()}");

            return new ClickResult
            {
                X = x,
                Y = y,
                RightButton = rightButton,
                Interaction = new InteractionResult
                {
                    ModeRequested = "physical",
                    ModeActual = "sendinput-mouse",
                    PhysicalInputGenerated = true
                }
            };
        }, ct);
    }

    public Task<TypeTextResult> TypeTextAsync(TypeTextOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var mode = options.Mode;
            string? actualMode = null;
            bool clipboardChanged = false;

            // --- Try semantic (ValuePattern.SetValue) ---
            if (mode is InteractionMode.Auto or InteractionMode.Semantic)
            {
                AutomationElement? element = null;
                if (options.Locator is not null)
                {
                    element = ((UiaAutomationBackend)_automation).ResolveLocatorInternal(options.Locator, ct);
                }
                else if (options.Hwnd.HasValue)
                {
                    element = _rawAutomation.FromHandle(options.Hwnd.Value);
                }

                if (element is not null)
                {
                    var focused = GetFocusedElement(element);
                    if (focused is not null && focused.Patterns.Value.IsSupported)
                    {
                        try
                        {
                            focused.Patterns.Value.Pattern.SetValue(options.Text);
                            actualMode = "uia-value-pattern";
                            return new TypeTextResult
                            {
                                Text = options.Text,
                                ClipboardUsed = false,
                                ClipboardSequenceChanged = false,
                                Interaction = new InteractionResult
                                {
                                    ModeRequested = mode.ToString().ToLowerInvariant(),
                                    ModeActual = actualMode,
                                    PhysicalInputGenerated = false
                                }
                            };
                        }
                        catch { }
                    }
                }
            }

            // --- Try Unicode SendInput ---
            if (mode is InteractionMode.Auto or InteractionMode.Physical)
            {
                try
                {
                    SimulateUnicodeText(options.Text);
                    actualMode = "sendinput-unicode";
                    return new TypeTextResult
                    {
                        Text = options.Text,
                        ClipboardUsed = false,
                        ClipboardSequenceChanged = false,
                        Interaction = new InteractionResult
                        {
                            ModeRequested = mode.ToString().ToLowerInvariant(),
                            ModeActual = actualMode,
                            PhysicalInputGenerated = true
                        }
                    };
                }
                catch { }
            }

            // --- Try clipboard paste (only with explicit opt-in) ---
            if (mode is InteractionMode.Auto or InteractionMode.Physical && options.AllowClipboard)
            {
                try
                {
                    clipboardChanged = ClipboardPaste(options.Text);
                    actualMode = "clipboard-paste";
                    return new TypeTextResult
                    {
                        Text = options.Text,
                        ClipboardUsed = true,
                        ClipboardSequenceChanged = clipboardChanged,
                        Interaction = new InteractionResult
                        {
                            ModeRequested = mode.ToString().ToLowerInvariant(),
                            ModeActual = actualMode,
                            PhysicalInputGenerated = false
                        }
                    };
                }
                catch { }
            }

            throw new CommandException(ErrorCodes.InputInjectionFailed,
                "All text input methods failed. Check target window accessibility and permissions.");
        }, ct);
    }

    public Task<KeyResult> KeyAsync(KeyOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var vkCodes = ParseKeyCombination(options.Keys);
            var inputs = new List<INPUT>();

            foreach (var (vk, isDown, isExtended) in GenerateInputSequence(vkCodes))
            {
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new INPUT_UNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)vk,
                            wScan = 0,
                            dwFlags = isDown ? (isExtended ? KEYEVENTF_EXTENDEDKEY : 0) : KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                });
            }

            uint result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            if (result == 0)
                throw new CommandException(ErrorCodes.InputInjectionFailed, $"SendInput failed. GetLastError: {Marshal.GetLastWin32Error()}");

            return new KeyResult
            {
                Keys = options.Keys,
                Interaction = new InteractionResult
                {
                    ModeRequested = "physical",
                    ModeActual = "sendinput-keyboard",
                    PhysicalInputGenerated = true
                }
            };
        }, ct);
    }

    public Task<ActivateResult> ActivateWindowAsync(IntPtr hwnd, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            // Restore if minimized
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            NativeMethods.BringWindowToTop(hwnd);
            bool result = NativeMethods.SetForegroundWindow(hwnd);

            var foreground = NativeMethods.GetForegroundWindow();

            return new ActivateResult
            {
                Activated = result && foreground == hwnd,
                ForegroundHwnd = FormatHwnd(foreground)
            };
        }, ct);
    }

    // ── Internal ──

    private static string FormatHwnd(IntPtr hwnd) => $"0x{hwnd:x}";

    private static INPUT CreateMouseInput(int x, int y, uint flags, int data)
    {
        // Convert screen coordinates to 0..65535 absolute range using the VIRTUAL DESKTOP
        // (not just the primary monitor). Requires MOUSEEVENTF_VIRTUALDESK flag.
        int screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);

        // Normalize: (coord - origin) / (total width - 1) * 65535
        int virtX = screenWidth > 0 ? (int)((long)(x - screenX) * 65535 / (screenWidth - 1)) : 0;
        int virtY = screenHeight > 0 ? (int)((long)(y - screenY) * 65535 / (screenHeight - 1)) : 0;

        // Clamp to valid range
        virtX = Math.Clamp(virtX, 0, 65535);
        virtY = Math.Clamp(virtY, 0, 65535);

        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new INPUT_UNION
            {
                mi = new MOUSEINPUT
                {
                    dx = virtX,
                    dy = virtY,
                    mouseData = data,
                    dwFlags = (int)(flags | MOUSEEVENTF_VIRTUALDESK),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void SimulateUnicodeText(string text)
    {
        var inputs = new List<INPUT>();

        foreach (char c in text)
        {
            // Key down with Unicode scan code
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });

            // Key up with Unicode scan code
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        uint result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (result == 0)
            throw new InvalidOperationException($"Unicode SendInput failed. GetLastError: {Marshal.GetLastWin32Error()}");
    }

    private const int KEYEVENTF_UNICODE = 0x0004;

    private static bool ClipboardPaste(string text)
    {
        uint seqBefore = NativeMethods.GetClipboardSequenceNumber();

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            throw new InvalidOperationException("Cannot open clipboard.");

        try
        {
            NativeMethods.EmptyClipboard();

            int size = (text.Length + 1) * 2; // UTF-16
            IntPtr hMem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(Encoding.Unicode.GetBytes(text + '\0'), 0, hMem, size);
                NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hMem);
            }
            finally
            {
                // Don't free — clipboard owns this memory
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }

        // Send Ctrl+V
        var inputs = new INPUT[4];
        inputs[0] = CreateKeyInput((ushort)VK_CONTROL, true);
        inputs[1] = CreateKeyInput((ushort)VK_V, true);
        inputs[2] = CreateKeyInput((ushort)VK_V, false);
        inputs[3] = CreateKeyInput((ushort)VK_CONTROL, false);

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());

        // Check if clipboard was modified during our operation
        uint seqAfter = NativeMethods.GetClipboardSequenceNumber();
        return seqAfter != seqBefore + 1;
    }

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    private static INPUT CreateKeyInput(ushort vk, bool isDown)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = isDown ? 0 : KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private List<ushort> ParseKeyCombination(string keys)
    {
        var vkCodes = new List<ushort>();
        var parts = keys.Split('+', StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
            {
                // Map A-Z, 0-9 to virtual key codes
                char c = char.ToUpperInvariant(part[0]);
                if (c >= 'A' && c <= 'Z')
                    vkCodes.Add((ushort)(0x41 + (c - 'A')));
                else if (c >= '0' && c <= '9')
                    vkCodes.Add((ushort)(0x30 + (c - '0')));
            }
            else
            {
                vkCodes.Add(part.ToUpperInvariant() switch
                {
                    "CTRL" or "CONTROL" => (ushort)VK_CONTROL,
                    "ALT" or "MENU" => (ushort)0x12,
                    "SHIFT" => (ushort)0x10,
                    "ENTER" or "RETURN" => (ushort)0x0D,
                    "TAB" => (ushort)0x09,
                    "ESC" or "ESCAPE" => (ushort)0x1B,
                    "SPACE" => (ushort)0x20,
                    "BACK" or "BACKSPACE" => (ushort)0x08,
                    "DELETE" or "DEL" => (ushort)0x2E,
                    "HOME" => (ushort)0x24,
                    "END" => (ushort)0x23,
                    "PGUP" or "PAGEUP" => (ushort)0x21,
                    "PGDN" or "PAGEDOWN" => (ushort)0x22,
                    "LEFT" => (ushort)0x25,
                    "RIGHT" => (ushort)0x27,
                    "UP" => (ushort)0x26,
                    "DOWN" => (ushort)0x28,
                    "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
                    "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
                    "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                    "WIN" or "LWIN" or "WINDOWS" => (ushort)0x5B,
                    "RWIN" => (ushort)0x5C,
                    "APPS" or "MENUKEY" => (ushort)0x5D,
                    _ => throw new CommandException(ErrorCodes.InvalidArgument, $"Unknown key: {part}")
                });
            }
        }

        return vkCodes;
    }

    private IEnumerable<(ushort vk, bool isDown, bool isExtended)> GenerateInputSequence(List<ushort> vkCodes)
    {
        // Modifiers first (press each)
        var modifiers = new HashSet<ushort> { 0x11, 0x12, 0x10, 0x5B, 0x5C };
        foreach (var vk in vkCodes.Where(v => modifiers.Contains(v)))
        {
            yield return (vk, true, vk == 0x5B || vk == 0x5C); // Win keys need extended flag
        }

        // Main keys press and release
        foreach (var vk in vkCodes.Where(v => !modifiers.Contains(v)))
        {
            yield return (vk, true, false);
            yield return (vk, false, false);
        }

        // Modifiers release in reverse order
        foreach (var vk in vkCodes.Where(v => modifiers.Contains(v)).Reverse())
        {
            yield return (vk, false, vk == 0x5B || vk == 0x5C);
        }
    }

    private static AutomationElement? GetFocusedElement(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.IsSupported)
                return element;

            // Try to find the focused child
            var focused = element.FindFirstChild(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            return focused ?? element;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _rawAutomation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
