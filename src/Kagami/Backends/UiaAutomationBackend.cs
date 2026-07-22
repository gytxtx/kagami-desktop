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
                startElement = NavigatePath(options.Hwnd, options.Path, options.View);
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
                GetWalker(options.View),
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
            var view = options.StartLocator?.View ?? "control";
            FindRecursive(start, options, results, 20, options.Hwnd, view, GetWalker(view), ct);
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
            var node = BuildSingleNode(element, hwnd, locator, locator.View);
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

        var walker = GetWalker(locator.View);
        for (var segmentIndex = 0; segmentIndex < locator.Path.Count; segmentIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var segment = locator.Path[segmentIndex];
            var children = GetChildren(current, walker);
            var candidates = ToCandidates(children);
            var selected = LocatorSegmentMatcher.Select(candidates, segment, segmentIndex);
            current = (AutomationElement)selected.Value;
        }

        return current;
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
            return FindByRuntimeIdRecursive(root, rtId, 50, GetWalker("raw"));
        }
        catch
        {
            return null;
        }
    }

    private AutomationElement? FindByRuntimeIdRecursive(
        AutomationElement element,
        int[] targetRtId,
        int maxDepth,
        ITreeWalker walker)
    {
        if (maxDepth <= 0) return null;

        var currentRtId = element.Properties.RuntimeId.ValueOrDefault;
        if (currentRtId is not null && currentRtId.SequenceEqual(targetRtId))
            return element;

        foreach (var child in GetChildren(element, walker))
        {
            var result = FindByRuntimeIdRecursive(child, targetRtId, maxDepth - 1, walker);
            if (result is not null) return result;
        }

        return null;
    }

    private AutomationElement? NavigatePath(IntPtr hwnd, string path, string view)
    {
        var root = _automation.FromHandle(hwnd);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        var walker = GetWalker(view);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out int childIndex))
                return null;

            var children = GetChildren(current, walker);
            if (childIndex < 0 || childIndex >= children.Count)
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
        ITreeWalker walker,
        CancellationToken ct)
    {
        if (remainingDepth < 0 || maxNodesBudget <= 0) return null;
        ct.ThrowIfCancellationRequested();

        try
        {
            var node = BuildSingleNode(element, rootHwnd, null, view);
            if (node is null) return null;

            // remainingDepth=0 means: return just this node, no children.
            // remainingDepth>=1 means: recurse one level deeper.
            bool childrenTruncated = false;
            int childrenCount = 0;

            if (remainingDepth >= 1)
            {
                var visibleChildren = GetChildren(element, walker);
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
                        childBudget, rootHwnd, childPath, walker, ct);
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

    private TreeNode? BuildSingleNode(
        AutomationElement element,
        IntPtr rootHwnd,
        Locator? existingLocator,
        string view)
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

            var locator = existingLocator ?? BuildLocator(rootHwnd, element, view);

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

    private Locator BuildLocator(IntPtr rootHwnd, AutomationElement target, string view)
    {
        view = NormalizeView(view);
        var segments = new List<LocatorSegment>();
        var root = _automation.FromHandle(rootHwnd);
        AutomationElement? current = target;
        var walker = GetWalker(view);

        while (current is not null && !current.Equals(root))
        {
            var parent = walker.GetParent(current)
                ?? throw new InvalidOperationException("Target is not reachable from the locator root in the selected UIA view.");
            var siblings = GetChildren(parent, walker);
            var candidates = ToCandidates(siblings);
            var targetCandidate = candidates.FirstOrDefault(candidate =>
                ((AutomationElement)candidate.Value).Equals(current));
            if (targetCandidate is null)
                throw new InvalidOperationException("Target is missing from its UIA sibling candidate set.");

            segments.Insert(0, LocatorSegmentMatcher.CreateSegment(candidates, targetCandidate));
            current = parent;
        }

        if (current is null)
            throw new InvalidOperationException("Target is not reachable from the locator root in the selected UIA view.");

        return new Locator
        {
            View = view,
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
        string view,
        ITreeWalker walker,
        CancellationToken ct)
    {
        if (maxDepth <= 0 || results.Count >= options.MaxResults) return;
        ct.ThrowIfCancellationRequested();

        try
        {
            if (MatchesFind(element, options))
            {
                var node = BuildSingleNode(element, rootHwnd, null, view);
                if (node is not null)
                    results.Add(node);
            }

            if (results.Count >= options.MaxResults) return;

            foreach (var child in GetChildren(element, walker))
            {
                FindRecursive(child, options, results, maxDepth - 1, rootHwnd, view, walker, ct);
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

    private ITreeWalker GetWalker(string? view) => NormalizeView(view) switch
    {
        "raw" => _automation.TreeWalkerFactory.GetRawViewWalker(),
        "content" => _automation.TreeWalkerFactory.GetContentViewWalker(),
        _ => _automation.TreeWalkerFactory.GetControlViewWalker()
    };

    private static string NormalizeView(string? view) => view?.ToLowerInvariant() switch
    {
        "raw" => "raw",
        "content" => "content",
        _ => "control"
    };

    private static List<AutomationElement> GetChildren(AutomationElement parent, ITreeWalker walker)
    {
        var children = new List<AutomationElement>();
        var child = walker.GetFirstChild(parent);
        while (child is not null)
        {
            children.Add(child);
            child = walker.GetNextSibling(child);
        }

        return children;
    }

    private static IReadOnlyList<LocatorCandidate> ToCandidates(IEnumerable<AutomationElement> elements) =>
        elements.Select(element => new LocatorCandidate(
            GetControlTypeName(element),
            element.Properties.AutomationId.ValueOrDefault,
            element.Properties.Name.ValueOrDefault,
            element.Properties.ClassName.ValueOrDefault,
            element)).ToList();

    private static string? DescribeResolutionMethod(Locator locator)
    {
        if (locator.Path.Count == 0) return "root";
        var last = locator.Path[^1];
        if (!string.IsNullOrWhiteSpace(last.AutomationId)) return "automation_id";
        if (!string.IsNullOrWhiteSpace(last.Name)) return "control_type+name";
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
