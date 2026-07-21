using System.Diagnostics;
using System.Runtime.InteropServices;
using Kagami.Protocol;
using Kagami.Utilities;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;

namespace Kagami.Backends;

public class UiaAutomationBackend : IAutomationBackend, IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly TempFileObservationGuardStore _guardStore;

    public UiaAutomationBackend(TempFileObservationGuardStore guardStore)
    {
        _automation = new UIA3Automation();
        _guardStore = guardStore;
    }

    public Task<List<WindowInfo>> ListWindowsAsync(bool visibleOnly, string? processName, string? title, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var results = new List<WindowInfo>();
            var foreground = NativeMethods.GetForegroundWindow();

            // Use FlaUI's desktop root to enumerate top-level windows for better property access
            var desktop = _automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));

            foreach (var window in windows)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
                    if (hwnd == IntPtr.Zero) continue;

                    var isVisible = NativeMethods.IsWindowVisible(hwnd);
                    var isMinimized = NativeMethods.IsIconic(hwnd);

                    if (visibleOnly && (!isVisible || isMinimized))
                        continue;

                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(hwnd, out pid);

                    var procName = ProcessHelper.GetProcessName((int)pid) ?? "";

                    if (processName is not null && !procName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var windowTitle = window.Properties.Name.ValueOrDefault ?? "";
                    if (title is not null && !windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rect = GetExtendedFrameBounds(hwnd);

                    var isCloaked = false;
                    NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out bool cloakedBool, 4);
                    isCloaked = cloakedBool;

                    results.Add(new WindowInfo
                    {
                        Hwnd = FormatHwnd(hwnd),
                        Pid = (int)pid,
                        ProcessName = procName,
                        Title = windowTitle,
                        ClassName = window.Properties.ClassName.ValueOrDefault ?? "",
                        Visible = isVisible,
                        Cloaked = isCloaked,
                        Minimized = isMinimized,
                        Foreground = hwnd == foreground,
                        Rect = rect
                    });
                }
                catch
                {
                    // Skip windows that throw on property access
                }
            }

            return results;
        }, ct);
    }

    public Task<TreeNode?> GetTreeAsync(GetTreeOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            AutomationElement? startElement;

            if (options.RuntimeId is not null)
            {
                // Convert the runtime-id string (e.g. "42.1234") back to runtime id array
                startElement = FindElementByRuntimeId(options.Hwnd, options.RuntimeId);
                if (startElement is null)
                    return null;
            }
            else if (options.Path is not null)
            {
                startElement = NavigatePath(options.Hwnd, options.Path);
                if (startElement is null)
                    return null;
            }
            else
            {
                startElement = _automation.FromHandle(options.Hwnd);
            }

            return BuildTree(
                startElement,
                options.View,
                options.MaxDepth,
                options.MaxNodes,
                options.Hwnd,
                "",
                ct);
        }, ct);
    }

    public Task<List<TreeNode>> FindAsync(FindOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            AutomationElement start;
            if (options.StartLocator is not null)
            {
                var resolved = ResolveLocatorInternal(options.StartLocator, ct);
                if (resolved is null) return new List<TreeNode>();
                start = resolved;
            }
            else
            {
                start = _automation.FromHandle(options.Hwnd);
            }

            var results = new List<TreeNode>();
            FindRecursive(start, options, results, 20, options.Hwnd, ct);
            return results;
        }, ct);
    }

    public Task<LocatorResolution?> ResolveLocatorAsync(Locator locator, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var element = ResolveLocatorInternal(locator, ct);
            if (element is null) return null;

            var hwnd = ParseHwnd(locator.Window.Hwnd);
            var node = BuildSingleNode(element, hwnd, locator);
            if (node is null) return null;

            var expectedRt = string.Join(".", element.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>());
            var resolvedRt = string.Join(".",
                node.RuntimeId.Split('.', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse));

            return new LocatorResolution
            {
                Node = node!,
                MatchedRuntimeId = expectedRt == string.Join(".", resolvedRt),
                ResolutionMethod = DescribeResolutionMethod(locator)!
            };
        }, ct);
    }

    public Task<bool> FocusAsync(Locator locator, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var element = ResolveLocatorInternal(locator, ct);
            if (element is null) return false;

            try
            {
                element.Focus();
                return true;
            }
            catch
            {
                return false;
            }
        }, ct);
    }

    public AutomationElement? ResolveLocatorInternal(Locator locator, CancellationToken ct)
    {
        var hwnd = ParseHwnd(locator.Window.Hwnd);
        if (hwnd == IntPtr.Zero) return null;

        var current = _automation.FromHandle(hwnd);
        if (current is null) return null;

        foreach (var segment in locator.Path)
        {
            ct.ThrowIfCancellationRequested();
            var resolved = ResolveSegmentWithAmbiguity(current, segment);
            if (resolved is null) return null;
            current = resolved;
        }

        return current;
    }

    /// <summary>
    /// Resolve a single locator segment with proper priority matching.
    ///
    /// Strategy (hierarchical — first match wins):
    ///   1. ControlType + AutomationId (exact) — most stable
    ///   2. ControlType + Name (exact) + ClassName (exact) — second priority
    ///   3. ControlType + ClassName + ordinal — fallback
    ///
    /// Returns null if no match is found.
    /// Throws CommandException with LOCATOR_AMBIGUOUS if multiple candidates match
    /// at the highest-priority strategy that returns anything.
    /// </summary>
    private AutomationElement? ResolveSegmentWithAmbiguity(AutomationElement parent, LocatorSegment segment)
    {
        var children = parent.FindAllChildren();

        // Strategy 1: ControlType + AutomationId
        if (segment.AutomationId is not null)
        {
            var matches = new List<AutomationElement>();
            foreach (var child in children)
            {
                try
                {
                    if (segment.ControlType is not null)
                    {
                        var ct = GetControlTypeName(child);
                        if (!ControlTypeMatches(ct, segment.ControlType))
                            continue;
                    }

                    var autoId = child.Properties.AutomationId.ValueOrDefault;
                    if (string.Equals(autoId, segment.AutomationId, StringComparison.OrdinalIgnoreCase))
                        matches.Add(child);
                }
                catch { }
            }

            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1 && segment.Ordinal < matches.Count)
                return matches[segment.Ordinal];
            if (matches.Count > 1)
                throw new CommandException(ErrorCodes.LocatorAmbiguous,
                    $"LOCATOR_AMBIGUOUS: {matches.Count} elements match automation_id='{segment.AutomationId}' " +
                    $"control_type='{segment.ControlType}', but ordinal={segment.Ordinal} is out of range.");
            // Zero matches at this priority → return null (don't fall through;
            // if AutomationId is specified, it's the authoritative key)
            return null;
        }

        // Strategy 2: ControlType + Name + ClassName
        if (segment.Name is not null)
        {
            var matches = new List<AutomationElement>();
            foreach (var child in children)
            {
                try
                {
                    if (segment.ControlType is not null)
                    {
                        var ct = GetControlTypeName(child);
                        if (!ControlTypeMatches(ct, segment.ControlType))
                            continue;
                    }

                    var name = child.Properties.Name.ValueOrDefault;
                    if (name is null || !string.Equals(name, segment.Name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (segment.ClassName is not null)
                    {
                        var cls = child.Properties.ClassName.ValueOrDefault;
                        if (!string.Equals(cls, segment.ClassName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    matches.Add(child);
                }
                catch { }
            }

            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1 && segment.Ordinal < matches.Count)
                return matches[segment.Ordinal];
            if (matches.Count > 1)
                throw new CommandException(ErrorCodes.LocatorAmbiguous,
                    $"LOCATOR_AMBIGUOUS: {matches.Count} elements match name='{segment.Name}' " +
                    $"control_type='{segment.ControlType}' class_name='{segment.ClassName}', " +
                    $"but ordinal={segment.Ordinal} is out of range.");
            return null;
        }

        // Strategy 3: ControlType + ClassName + ordinal
        {
            var matches = new List<AutomationElement>();
            foreach (var child in children)
            {
                try
                {
                    if (segment.ControlType is not null)
                    {
                        var ct = GetControlTypeName(child);
                        if (!ControlTypeMatches(ct, segment.ControlType))
                            continue;
                    }

                    if (segment.ClassName is not null)
                    {
                        var cls = child.Properties.ClassName.ValueOrDefault;
                        if (!string.Equals(cls, segment.ClassName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    matches.Add(child);
                }
                catch { }
            }

            if (matches.Count == 0) return null;
            if (segment.Ordinal >= matches.Count)
                throw new CommandException(ErrorCodes.LocatorNotFound,
                    $"LOCATOR_NOT_FOUND: {matches.Count} elements match control_type='{segment.ControlType}' " +
                    $"class_name='{segment.ClassName}', but ordinal={segment.Ordinal} is out of range.");
            return matches[segment.Ordinal];
        }
    }

    /// <summary>
    /// Check if a ControlType string matches a segment's ControlType pattern.
    /// Exact match with case-insensitivity. The segment may omit the "ControlType." prefix.
    /// </summary>
    private static bool ControlTypeMatches(string? actual, string expected)
    {
        if (actual is null) return false;
        return string.Equals(
            StripPrefix(actual, "ControlType."),
            StripPrefix(expected, "ControlType."),
            StringComparison.OrdinalIgnoreCase);
    }

    public ObservationGuard? BuildGuard(IntPtr hwnd)
    {
        try
        {
            var isMinimized = NativeMethods.IsIconic(hwnd);
            if (isMinimized) return null;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            var startTime = ProcessHelper.GetProcessStartTime((int)pid);
            if (startTime is null) return null;

            var windowRect = GetExtendedFrameBounds(hwnd);
            var foreground = NativeMethods.GetForegroundWindow();

            var element = _automation.FromHandle(hwnd);
            var rtId = element?.Properties.RuntimeId.ValueOrDefault;
            var rtIdStr = rtId is not null ? string.Join(".", rtId) : "";

            return new ObservationGuard
            {
                Hwnd = FormatHwnd(hwnd),
                Pid = (int)pid,
                ProcessStartTime = startTime,
                ForegroundHwnd = FormatHwnd(foreground),
                WindowRect = windowRect,
                RootRuntimeId = rtIdStr,
                CapturedAt = DateTime.UtcNow.ToString("O")
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get window rect via DWMWA_EXTENDED_FRAME_BOUNDS for accurate screen-space bounds.
    /// Falls back to GetWindowRect if DWM call fails.
    /// </summary>
    public static Rect GetExtendedFrameBounds(IntPtr hwnd)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out RECT rect,
            Marshal.SizeOf<RECT>());

        if (hr != 0)
        {
            NativeMethods.GetWindowRect(hwnd, out rect);
        }

        return new Rect
        {
            X = rect.Left,
            Y = rect.Top,
            W = rect.Right - rect.Left,
            H = rect.Bottom - rect.Top
        };
    }

    public static string FormatHwnd(IntPtr hwnd) =>
        $"0x{hwnd:x}";

    public static string FormatHwnd(ulong hwnd) =>
        $"0x{hwnd:x}";

    public static IntPtr ParseHwnd(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];

        if (long.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out long val))
            return (IntPtr)val;

        return IntPtr.Zero;
    }

    // ── Internal helpers ──

    private AutomationElement? FindElementByRuntimeId(IntPtr hwnd, string rtIdStr)
    {
        try
        {
            var parts = rtIdStr.Split('.');
            var rtId = parts.Select(int.Parse).ToArray();

            // Walk the tree from the window root to find the element with this runtime ID
            var root = _automation.FromHandle(hwnd);
            return FindByRuntimeIdRecursive(root, rtId, 50);
        }
        catch
        {
            return null;
        }
    }

    private AutomationElement? FindByRuntimeIdRecursive(AutomationElement element, int[] targetRtId, int maxDepth)
    {
        if (maxDepth <= 0) return null;

        var currentRtId = element.Properties.RuntimeId.ValueOrDefault;
        if (currentRtId is not null && currentRtId.SequenceEqual(targetRtId))
            return element;

        var children = element.FindAllChildren();
        foreach (var child in children)
        {
            var result = FindByRuntimeIdRecursive(child, targetRtId, maxDepth - 1);
            if (result is not null) return result;
        }

        return null;
    }

    private AutomationElement? NavigatePath(IntPtr hwnd, string path)
    {
        var root = _automation.FromHandle(hwnd);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out int childIndex))
                return null;

            var children = current.FindAllChildren();
            if (childIndex < 0 || childIndex >= children.Length)
                return null;

            current = children[childIndex];
        }

        return current;
    }

    private TreeNode? BuildTree(
        AutomationElement element,
        string view,
        int remainingDepth,
        int maxNodesBudget,
        IntPtr rootHwnd,
        string parentPath,
        CancellationToken ct)
    {
        if (remainingDepth < 0 || maxNodesBudget <= 0) return null;
        ct.ThrowIfCancellationRequested();

        try
        {
            var node = BuildSingleNode(element, rootHwnd, null);
            if (node is null) return null;

            // remainingDepth=0 means: return just this node, no children.
            // remainingDepth>=1 means: recurse one level deeper.
            bool childrenTruncated = false;
            int childrenCount = 0;

            if (remainingDepth >= 1)
            {
                var children = element.FindAllChildren();
                var visibleChildren = FilterByView(children, view);
                int availableBudget = maxNodesBudget - 1; // subtract self
                childrenCount = Math.Min(availableBudget, visibleChildren.Count);

                if (childrenCount < visibleChildren.Count)
                {
                    childrenTruncated = true;
                }

                for (int i = 0; i < childrenCount; i++)
                {
                    var childPath = string.IsNullOrEmpty(parentPath) ? i.ToString() : $"{parentPath}/{i}";
                    // Each child gets its fair share of the remaining budget
                    int childBudget = availableBudget / childrenCount;
                    var childNode = BuildTree(visibleChildren[i], view, remainingDepth - 1,
                        childBudget, rootHwnd, childPath, ct);
                    if (childNode is not null)
                        node.Children.Add(childNode);
                }
            }

            // Build a new TreeNode with ChildrenCount and ChildrenTruncated set
            return new TreeNode
            {
                NodeId = node.NodeId,
                RuntimeId = node.RuntimeId,
                ControlType = node.ControlType,
                Name = node.Name,
                AutomationId = node.AutomationId,
                ClassName = node.ClassName,
                FrameworkId = node.FrameworkId,
                ProcessId = node.ProcessId,
                NativeWindowHandle = node.NativeWindowHandle,
                Rect = node.Rect,
                ClickablePoint = node.ClickablePoint,
                IsEnabled = node.IsEnabled,
                IsOffscreen = node.IsOffscreen,
                IsKeyboardFocusable = node.IsKeyboardFocusable,
                HasKeyboardFocus = node.HasKeyboardFocus,
                IsVirtualized = node.IsVirtualized,
                Patterns = node.Patterns,
                ChildrenCount = childrenCount,
                ChildrenTruncated = childrenTruncated,
                Children = node.Children,
                Locator = node.Locator
            };
        }
        catch
        {
            return null;
        }
    }

    private TreeNode? BuildSingleNode(AutomationElement element, IntPtr rootHwnd, Locator? existingLocator)
    {
        try
        {
            var ctProgName = GetControlTypeName(element);
            var rtId = element.Properties.RuntimeId.ValueOrDefault;
            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            var clickable = element.Properties.ClickablePoint.ValueOrDefault;

            var patterns = new List<string>();

            // Check pattern availability efficiently
            if (element.Patterns.Invoke.IsSupported) patterns.Add("invoke");
            if (element.Patterns.Value.IsSupported) patterns.Add("value");
            if (element.Patterns.Toggle.IsSupported) patterns.Add("toggle");
            if (element.Patterns.ExpandCollapse.IsSupported) patterns.Add("expand_collapse");
            if (element.Patterns.Scroll.IsSupported) patterns.Add("scroll");
            if (element.Patterns.ScrollItem.IsSupported) patterns.Add("scroll_item");
            if (element.Patterns.SelectionItem.IsSupported) patterns.Add("selection_item");
            if (element.Patterns.Text.IsSupported) patterns.Add("text");
            if (element.Patterns.VirtualizedItem.IsSupported) patterns.Add("virtualized_item");

            var isVirtualized = element.Patterns.VirtualizedItem.IsSupported;
            var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;

            var nodeId = Guid.NewGuid().ToString("N")[..8];
            var rtIdStr = rtId is not null ? string.Join(".", rtId) : "";

            var locator = existingLocator ?? BuildLocator(rootHwnd, element);

            return new TreeNode
            {
                NodeId = nodeId,
                RuntimeId = rtIdStr,
                ControlType = StripPrefix(ctProgName.Length > 0 ? ctProgName : "Unknown", "ControlType."),
                Name = element.Properties.Name.ValueOrDefault,
                AutomationId = element.Properties.AutomationId.ValueOrDefault,
                ClassName = element.Properties.ClassName.ValueOrDefault,
                FrameworkId = element.Properties.FrameworkId.ValueOrDefault,
                ProcessId = element.Properties.ProcessId.ValueOrDefault,
                NativeWindowHandle = hwnd != IntPtr.Zero ? FormatHwnd(hwnd) : "0x0",
                Rect = rect.Width > 0 || rect.Height > 0 ? new DetailedRect
                {
                    Left = (int)rect.Left,
                    Top = (int)rect.Top,
                    Right = (int)rect.Right,
                    Bottom = (int)rect.Bottom
                } : null,
                ClickablePoint = clickable.X > 0 || clickable.Y > 0 ? new Point
                {
                    X = (int)clickable.X,
                    Y = (int)clickable.Y
                } : null,
                IsEnabled = element.Properties.IsEnabled.ValueOrDefault,
                IsOffscreen = element.Properties.IsOffscreen.ValueOrDefault,
                IsKeyboardFocusable = element.Properties.IsKeyboardFocusable.ValueOrDefault,
                HasKeyboardFocus = element.Properties.HasKeyboardFocus.ValueOrDefault,
                IsVirtualized = isVirtualized,
                Patterns = patterns,
                ChildrenCount = 0,
                ChildrenTruncated = false,
                Children = new List<TreeNode>(),
                Locator = locator
            };
        }
        catch
        {
            return null;
        }
    }

    private Locator BuildLocator(IntPtr rootHwnd, AutomationElement target)
    {
        var segments = new List<LocatorSegment>();
        var current = target;

        // Walk up to build the path from root to target
        while (current is not null)
        {
            try
            {
                var ctProgName2 = GetControlTypeName(current);
                var autoId = current.Properties.AutomationId.ValueOrDefault;
                var name = current.Properties.Name.ValueOrDefault;
                var cls = current.Properties.ClassName.ValueOrDefault;
                var ntHwnd = current.Properties.NativeWindowHandle.ValueOrDefault;

                // Stop when we hit the root window
                if (ntHwnd != IntPtr.Zero && ntHwnd == rootHwnd)
                    break;

                // Calculate ordinal among siblings matching same conditions
                var parent = current.Parent;
                int ordinal = 0;
                if (parent is not null)
                {
                    var siblings = parent.FindAllChildren();
                    foreach (var sib in siblings)
                    {
                        try
                        {
                            var sCt = GetControlTypeName(sib);
                            var sAutoId = sib.Properties.AutomationId.ValueOrDefault;
                            var sName = sib.Properties.Name.ValueOrDefault;
                            var sCls = sib.Properties.ClassName.ValueOrDefault;

                            bool matchesConds =
                                (autoId is null || string.Equals(sAutoId, autoId, StringComparison.OrdinalIgnoreCase)) &&
                                (name is null || (sName is not null && sName.Contains(name, StringComparison.OrdinalIgnoreCase)));

                            if (sCt == ctProgName2 && matchesConds)
                            {
                                if (sib.Equals(current))
                                    break;
                                ordinal++;
                            }
                        }
                        catch { }
                    }
                }

                segments.Insert(0, new LocatorSegment
                {
                    ControlType = StripPrefix(ctProgName2.Length > 0 ? ctProgName2 : "", "ControlType."),
                    AutomationId = autoId,
                    Name = name,
                    ClassName = cls,
                    Ordinal = ordinal
                });
            }
            catch { }

            try { current = current.Parent; }
            catch { current = null; }
        }

        return new Locator
        {
            Window = new WindowRef { Hwnd = FormatHwnd(rootHwnd) },
            Path = segments
        };
    }

    private void FindRecursive(
        AutomationElement element,
        FindOptions options,
        List<TreeNode> results,
        int maxDepth,
        IntPtr rootHwnd,
        CancellationToken ct)
    {
        if (maxDepth <= 0 || results.Count >= options.MaxResults) return;
        ct.ThrowIfCancellationRequested();

        try
        {
            if (MatchesFind(element, options))
            {
                var node = BuildSingleNode(element, rootHwnd, null);
                if (node is not null)
                    results.Add(node);
            }

            if (results.Count >= options.MaxResults) return;

            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                FindRecursive(child, options, results, maxDepth - 1, rootHwnd, ct);
            }
        }
        catch { }
    }

    private static bool MatchesFind(AutomationElement element, FindOptions options)
    {
        try
        {
            if (options.ControlType is not null)
            {
                var ct = GetControlTypeName(element);
                if (!ct.Contains(options.ControlType, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (options.AutomationId is not null)
            {
                var autoId = element.Properties.AutomationId.ValueOrDefault;
                if (!string.Equals(autoId, options.AutomationId, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (options.Name is not null)
            {
                var name = element.Properties.Name.ValueOrDefault;
                if (name is null || !name.Contains(options.Name, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (options.ClassName is not null)
            {
                var cls = element.Properties.ClassName.ValueOrDefault;
                if (!string.Equals(cls, options.ClassName, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<AutomationElement> FilterByView(AutomationElement[] children, string view) => view switch
    {
        "raw" => new List<AutomationElement>(children),
        "content" => children.Where(c =>
        {
            try { return c.Properties.IsContentElement.ValueOrDefault; }
            catch { return false; }
        }).ToList(),
        _ => children.Where(c => // "control" (default)
        {
            try { return c.Properties.IsControlElement.ValueOrDefault; }
            catch { return false; }
        }).ToList()
    };

    private static string? DescribeResolutionMethod(Locator locator)
    {
        if (locator.Path.Count == 0) return "root";
        var last = locator.Path[^1];
        if (last.AutomationId is not null) return "automation_id";
        if (last.Name is not null) return "control_type+name";
        if (last.ClassName is not null) return "class_name+ordinal";
        return "ordinal";
    }

    private static string StripPrefix(string value, string prefix)
    {
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return value[prefix.Length..];
        return value;
    }

    /// <summary>
    /// Get the ControlType programmatic name string from an AutomationElement.
    /// FlaUI 4.0 ControlType is a value type without ProgrammaticName; we use
    /// the element's ControlType property directly.
    /// </summary>
    private static string GetControlTypeName(AutomationElement element)
    {
        try
        {
            // FlaUI 4.0 ControlType is a value type — use ToString() and strip the ControlType. prefix
            var ctStr = element.Properties.ControlType.ValueOrDefault.ToString();
            return ctStr.Replace("ControlType.", "");
        }
        catch
        {
            return "Unknown";
        }
    }

    public void Dispose()
    {
        _automation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
