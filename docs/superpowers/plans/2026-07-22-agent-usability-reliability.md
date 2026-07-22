# Kagami Desktop Agent Usability Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复物理输入假成功、locator 无法 round-trip、CLI 契约漂移和高成本树遍历，使 Kagami 能被 AI Agent 安全、稳定、低成本地用于真实 Windows 桌面应用。

**Architecture:** 保持 one-shot CLI 和现有 JSON schema，通过共享的 `PhysicalInputTargetValidator` 统一物理输入安全策略，通过同一 UIA TreeWalker 和匹配器统一 locator 构造/解析。CLI 以向后兼容方式增加 find、runtime-id、tree_path 和精简输出，不引入 daemon 或应用特例。

**Tech Stack:** C# 13、.NET 10 Windows、System.CommandLine beta4、FlaUI UIA3、xUnit、Win32 User32。

## Global Constraints

- 安全优先：没有明确目标或目标不在前台时，不得调用 `SendInput`。
- 已提供有效目标且目标位于前台的旧调用保持兼容。
- 不引入常驻服务、新运行时依赖或 Cafe Launcher 特例。
- 所有生产行为变更先写失败测试并观察 RED，再写最小实现。
- 保留本轮开始前已有 tracked/untracked 修改，不重置、不覆盖。
- stdout 成功和失败均保持单个 JSON 文档；诊断写 stderr。

---

### Task 1: 可测试的窗口族与物理输入目标校验

**Files:**
- Create: `src/Kagami/Backends/IWindowSystem.cs`
- Create: `src/Kagami/Backends/PhysicalInputTargetValidator.cs`
- Modify: `src/Kagami/Utilities/NativeMethods.cs`
- Modify: `src/Kagami/Protocol/ErrorCodes.cs`
- Test: `tests/Kagami.Tests/Backends/PhysicalInputTargetValidatorTests.cs`

**Interfaces:**
- Produces: `PhysicalInputTargetValidator.ValidateKeyboardTarget(IntPtr)`。
- Produces: `PhysicalInputTargetValidator.ValidatePointerTarget(IntPtr, int, int)`。
- Produces: `PhysicalInputTargetValidation`，包含 `TargetHwnd`、`ForegroundVerified`、`DeliveryVerified`。
- Produces: `ErrorCodes.PointNotInTarget = "POINT_NOT_IN_TARGET"`。

- [ ] **Step 1: 写目标缺失和非前台失败测试**

```csharp
[Fact]
public void ValidateKeyboardTarget_ZeroTarget_IsRejected()
{
    var validator = new PhysicalInputTargetValidator(new FakeWindowSystem());
    var ex = Assert.Throws<CommandException>(() => validator.ValidateKeyboardTarget(IntPtr.Zero));
    Assert.Equal(ErrorCodes.InvalidArgument, ex.ErrorCode);
}

[Fact]
public void ValidateKeyboardTarget_DifferentForegroundProcess_IsRejected()
{
    var windows = new FakeWindowSystem()
        .Window((IntPtr)0x10, pid: 10)
        .Window((IntPtr)0x20, pid: 20)
        .Foreground((IntPtr)0x20);
    var validator = new PhysicalInputTargetValidator(windows);

    var ex = Assert.Throws<CommandException>(() => validator.ValidateKeyboardTarget((IntPtr)0x10));

    Assert.Equal(ErrorCodes.ForegroundActivationDenied, ex.ErrorCode);
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~PhysicalInputTargetValidatorTests`

Expected: FAIL，因为 `IWindowSystem` 与 `PhysicalInputTargetValidator` 尚不存在。

- [ ] **Step 3: 写坐标命中、同进程 popup 和错误进程测试**

```csharp
[Fact]
public void ValidatePointerTarget_OwnedPopupInSameProcess_IsAccepted()
{
    var windows = new FakeWindowSystem()
        .Window((IntPtr)0x10, pid: 10)
        .Window((IntPtr)0x11, pid: 10, owner: (IntPtr)0x10)
        .Foreground((IntPtr)0x11)
        .PointHit(100, 200, (IntPtr)0x11);

    var result = new PhysicalInputTargetValidator(windows)
        .ValidatePointerTarget((IntPtr)0x10, 100, 200);

    Assert.True(result.ForegroundVerified);
    Assert.True(result.DeliveryVerified);
}

[Fact]
public void ValidatePointerTarget_PointOverDifferentProcess_IsRejected()
{
    var windows = new FakeWindowSystem()
        .Window((IntPtr)0x10, pid: 10)
        .Window((IntPtr)0x20, pid: 20)
        .Foreground((IntPtr)0x10)
        .PointHit(100, 200, (IntPtr)0x20);

    var ex = Assert.Throws<CommandException>(() =>
        new PhysicalInputTargetValidator(windows).ValidatePointerTarget((IntPtr)0x10, 100, 200));

    Assert.Equal(ErrorCodes.PointNotInTarget, ex.ErrorCode);
}
```

- [ ] **Step 4: 实现最小窗口系统抽象和校验器**

```csharp
internal interface IWindowSystem
{
    IntPtr GetForegroundWindow();
    IntPtr WindowFromPoint(int x, int y);
    IntPtr GetOwner(IntPtr hwnd);
    int GetProcessId(IntPtr hwnd);
}

internal sealed record PhysicalInputTargetValidation(
    IntPtr TargetHwnd,
    bool ForegroundVerified,
    bool DeliveryVerified);
```

`NativeWindowSystem` 仅包装 `GetForegroundWindow`、`WindowFromPoint`、`GetWindow(GW_OWNER)` 和 `GetWindowThreadProcessId`。校验器通过 PID 与 owner 链识别同一窗口族，不修改输入 backend。

- [ ] **Step 5: 运行目标测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~PhysicalInputTargetValidatorTests`

Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add src/Kagami/Backends/IWindowSystem.cs src/Kagami/Backends/PhysicalInputTargetValidator.cs src/Kagami/Utilities/NativeMethods.cs src/Kagami/Protocol/ErrorCodes.cs tests/Kagami.Tests/Backends/PhysicalInputTargetValidatorTests.cs
git commit -m "fix(input): 校验物理输入目标窗口"
```

### Task 2: 在 click、key 和 keyboard type-text 中强制安全策略

**Files:**
- Modify: `src/Kagami/Backends/IInputBackend.cs`
- Modify: `src/Kagami/Backends/IObservationGuardStore.cs`
- Modify: `src/Kagami/Backends/TempFileObservationGuardStore.cs`
- Modify: `src/Kagami/Backends/Win32InputBackend.cs`
- Modify: `src/Kagami/Commands/InteractionCommands.cs`
- Modify: `src/Kagami/Program.cs`
- Modify: `src/Kagami/Protocol/DataTypes.cs`
- Test: `tests/Kagami.Tests/Commands/InteractionCommandsTests.cs`
- Test: `tests/Kagami.Tests/Protocol/DataTypesTests.cs`

**Interfaces:**
- Consumes: Task 1 `PhysicalInputTargetValidator`。
- Changes: `ClickAsync(IntPtr targetHwnd, int x, int y, bool rightButton, ...)`。
- Changes: CLI `click --hwnd <HWND>`；也允许从 `--expected-state` guard 推导。
- Produces: `InteractionResult.TargetHwnd`、`TargetForegroundVerified`、`TargetDeliveryVerified`。
- Changes: `GuardValidationResult` 在验证成功时携带已反序列化的 `ObservationGuard`，避免命令层再次读取 guard 文件。

- [ ] **Step 1: 写命令层失败测试，证明缺目标和非前台不会调用 backend**

```csharp
[Fact]
public async Task Click_WithoutHwndOrGuard_FailsBeforeInput()
{
    var input = new RecordingInputBackend();
    var command = CreateCommands(input, new FakeGuardStore());

    var code = await command.ClickAsync(null, 10, 20, false, null);

    Assert.Equal(1, code);
    Assert.Equal(0, input.ClickCalls);
}

[Fact]
public async Task Key_TargetValidationFailure_DoesNotInject()
{
    var input = new RecordingInputBackend { KeyException =
        new CommandException(ErrorCodes.ForegroundActivationDenied, "target is not foreground") };
    var command = CreateCommands(input, new FakeGuardStore());

    var code = await command.KeyAsync("ESC", "0x10", null);

    Assert.Equal(1, code);
    Assert.Equal(0, input.SendInputCalls);
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~InteractionCommandsTests`

Expected: FAIL，因为 `click` 还没有 HWND，backend 也未接入校验器。

- [ ] **Step 3: 修改输入接口和命令绑定**

```csharp
Task<ClickResult> ClickAsync(IntPtr targetHwnd, int x, int y, bool rightButton, CancellationToken ct);
```

`InteractionCommands.ClickAsync` 按以下顺序确定目标：显式 `--hwnd` → 已验证 guard 中的 `ObservationGuard.Hwnd` → `INVALID_ARGUMENT`。`key` 和 physical keyboard typing 必须有 HWND，并由 backend 在调用 `SendInput` 前校验。

`IObservationGuardStore.LoadAndValidateAsync` 的成功结果设置 `Guard`；命令层只使用该已验证实例推导 HWND，不直接读取路径。

- [ ] **Step 4: 扩展 InteractionResult 并写序列化测试**

```csharp
[JsonPropertyName("target_hwnd")]
public string? TargetHwnd { get; init; }

[JsonPropertyName("target_foreground_verified")]
public bool TargetForegroundVerified { get; init; }

[JsonPropertyName("target_delivery_verified")]
public bool TargetDeliveryVerified { get; init; }
```

- [ ] **Step 5: 运行命令和协议测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~InteractionCommandsTests|FullyQualifiedName~DataTypesTests"`

Expected: PASS，且失败路径的 recording backend 未记录输入注入。

- [ ] **Step 6: 提交**

```powershell
git add src/Kagami/Backends/IInputBackend.cs src/Kagami/Backends/IObservationGuardStore.cs src/Kagami/Backends/TempFileObservationGuardStore.cs src/Kagami/Backends/Win32InputBackend.cs src/Kagami/Commands/InteractionCommands.cs src/Kagami/Program.cs src/Kagami/Protocol/DataTypes.cs tests/Kagami.Tests/Commands/InteractionCommandsTests.cs tests/Kagami.Tests/Protocol/DataTypesTests.cs
git commit -m "fix(input): 拒绝不安全的后台输入"
```

### Task 3: 统一 locator TreeWalker 与 segment 匹配规则

**Files:**
- Create: `src/Kagami/Backends/LocatorSegmentMatcher.cs`
- Modify: `src/Kagami/Backends/UiaAutomationBackend.cs`
- Modify: `src/Kagami/Protocol/Locator.cs`
- Modify: `src/Kagami/Backends/CommandException.cs`
- Test: `tests/Kagami.Tests/Backends/LocatorSegmentMatcherTests.cs`
- Test: `tests/Kagami.Tests/Backends/UiaAutomationBackendIntegrationTests.cs`

**Interfaces:**
- Produces: `Locator.View`，默认 `control`。
- Produces: `LocatorSegmentMatcher.Select(IReadOnlyList<LocatorCandidate>, LocatorSegment)`。
- Changes: locator 构造和解析显式使用同一 view TreeWalker。

- [ ] **Step 1: 写空 name 与 ordinal 一致性失败测试**

```csharp
[Fact]
public void Select_EmptyName_UsesClassAndOrdinal()
{
    var candidates = new[]
    {
        Candidate("Group", "", "Panel"),
        Candidate("Group", "", "Grid"),
        Candidate("Group", "", "Panel")
    };
    var segment = new LocatorSegment
    {
        ControlType = "Group", Name = "", ClassName = "Panel", Ordinal = 1
    };

    var selected = LocatorSegmentMatcher.Select(candidates, segment);

    Assert.Same(candidates[2], selected);
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~LocatorSegmentMatcherTests`

Expected: FAIL，因为共享匹配器尚不存在。

- [ ] **Step 3: 实现共享匹配器**

匹配优先级保持：AutomationId → 非空 name + class → class + ordinal。构造 ordinal 与解析必须调用同一个候选过滤函数；空字符串按未提供处理。

- [ ] **Step 4: 将 BuildLocator 和 ResolveLocatorInternal 改为同一 TreeWalker**

为 `control`、`content`、`raw` 提供 `GetWalker(view)`；构造使用 walker.GetParent，解析使用 walker.GetFirstChild/GetNextSibling。不得混用 `element.Parent` 和默认 `FindAllChildren()`。

- [ ] **Step 5: 强化 round-trip 集成测试**

```csharp
[Fact]
public async Task ResolveLocator_AllReturnedInteractiveChildren_RoundTripToSameRuntimeId()
{
    var target = FindSuitableWindow();
    var tree = await _backend.GetTreeAsync(new GetTreeOptions
    {
        Hwnd = target, MaxDepth = 2, MaxNodes = 100, View = "control"
    }, CancellationToken.None);

    foreach (var node in Flatten(tree!).Where(n => n.Locator is not null && n.Patterns.Count > 0))
    {
        var resolved = await _backend.ResolveLocatorAsync(node.Locator!, CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal(node.RuntimeId, resolved!.Node.RuntimeId);
    }
}
```

- [ ] **Step 6: 在失败 details 中返回 segment 诊断**

`CommandException` 增加只读 details；locator 失败至少包含 `segment_index`、`segment`、`candidate_count`、`candidates`。`ResponseWriter.Fail` 将其写入 `JsonError.Details`。

- [ ] **Step 7: 运行 locator 目标测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~LocatorSegmentMatcherTests|FullyQualifiedName~UiaAutomationBackendIntegrationTests"`

Expected: PASS。

- [ ] **Step 8: 提交**

```powershell
git add src/Kagami/Backends/LocatorSegmentMatcher.cs src/Kagami/Backends/UiaAutomationBackend.cs src/Kagami/Protocol/Locator.cs src/Kagami/Backends/CommandException.cs tests/Kagami.Tests/Backends/LocatorSegmentMatcherTests.cs tests/Kagami.Tests/Backends/UiaAutomationBackendIntegrationTests.cs
git commit -m "fix(uia): 统一 locator 构造与解析视图"
```

### Task 4: 完成渐进式树遍历、find 和精简输出

**Files:**
- Modify: `src/Kagami/Protocol/TreeNode.cs`
- Create: `src/Kagami/Backends/TreeOutputPolicy.cs`
- Modify: `src/Kagami/Backends/IAutomationBackend.cs`
- Modify: `src/Kagami/Backends/UiaAutomationBackend.cs`
- Modify: `src/Kagami/Commands/GetTreeCommand.cs`
- Create: `src/Kagami/Commands/FindCommand.cs`
- Modify: `src/Kagami/Commands/ObserveCommand.cs`
- Modify: `src/Kagami/Program.cs`
- Test: `tests/Kagami.Tests/Commands/TreeQueryCommandTests.cs`
- Test: `tests/Kagami.Tests/Protocol/DataTypesTests.cs`

**Interfaces:**
- Produces: `TreeNode.TreePath`。
- Changes: `GetTreeOptions` 增加 `StartLocator`、`InteractiveOnly`、`IncludeLocators`。
- Produces: CLI `find`。

- [ ] **Step 1: 写 TreePath 和 locator 输出策略失败测试**

```csharp
[Fact]
public void TreeNode_SerializesTreePath()
{
    var json = JsonSerializer.Serialize(new TreeNode { TreePath = "0/2" }, JsonConfig.Options);
    Assert.Contains("\"tree_path\":\"0/2\"", json);
}

[Theory]
[InlineData("none", false, false)]
[InlineData("interactive", true, false)]
[InlineData("all", true, true)]
public void LocatorPolicy_MatchesRequestedMode(string mode, bool interactiveHasLocator, bool textHasLocator)
{
    var interactive = new TreeNode { Patterns = new List<string> { "invoke" } };
    var text = new TreeNode { ControlType = "Text" };

    Assert.Equal(interactiveHasLocator,
        TreeOutputPolicy.ShouldIncludeLocator(mode, interactive));
    Assert.Equal(textHasLocator,
        TreeOutputPolicy.ShouldIncludeLocator(mode, text));
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~TreeQueryCommandTests|FullyQualifiedName~DataTypesTests"`

Expected: FAIL，因为字段和选项尚不存在。

- [ ] **Step 3: 在 BuildTree 中传播 tree_path 并增加启动入口**

`get-tree` 参数互斥规则：`--path`、`--runtime-id`、`--locator` 最多一个。三种入口最终得到 start element，并使用请求的 view 构建树。

- [ ] **Step 4: 注册 find 命令**

`FindCommand.RunAsync` 验证至少一个筛选条件，构造 `FindOptions`，调用 `FindAsync`，返回数组。增加 `--view` 和 `--max-results`，并使 backend 的递归遍历遵循 view。

- [ ] **Step 5: 实现 interactive-only 和 include-locators**

交互节点判定为：`Patterns` 包含 invoke/value/toggle/expand_collapse/selection_item，或 `IsKeyboardFocusable=true`。interactive-only 保留匹配节点及其祖先；`children_count` 仍表示响应中返回的直接子节点数，截断用 `children_truncated` 表达。

- [ ] **Step 6: 运行目标测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~TreeQueryCommandTests|FullyQualifiedName~DataTypesTests|FullyQualifiedName~UiaAutomationBackendIntegrationTests"`

Expected: PASS。

- [ ] **Step 7: 提交**

```powershell
git add src/Kagami/Protocol/TreeNode.cs src/Kagami/Backends/TreeOutputPolicy.cs src/Kagami/Backends/IAutomationBackend.cs src/Kagami/Backends/UiaAutomationBackend.cs src/Kagami/Commands/GetTreeCommand.cs src/Kagami/Commands/FindCommand.cs src/Kagami/Commands/ObserveCommand.cs src/Kagami/Program.cs tests/Kagami.Tests/Commands/TreeQueryCommandTests.cs tests/Kagami.Tests/Protocol/DataTypesTests.cs
git commit -m "feat(uia): 增加渐进式树查询与查找"
```

### Task 5: 统一 CLI 语法、解析错误和窗口身份

**Files:**
- Create: `src/Kagami/Utilities/ProcessNameMatcher.cs`
- Create: `src/Kagami/Utilities/WindowInfoReader.cs`
- Modify: `src/Kagami/Backends/UiaAutomationBackend.cs`
- Modify: `src/Kagami/Commands/ObserveCommand.cs`
- Modify: `src/Kagami/Commands/WaitForCommand.cs`
- Modify: `src/Kagami/Program.cs`
- Test: `tests/Kagami.Tests/Utilities/ProcessNameMatcherTests.cs`
- Test: `tests/Kagami.Tests/Commands/CommandLineContractTests.cs`

**Interfaces:**
- Produces: `ProcessNameMatcher.EqualsIgnoringExe(string, string)`。
- Produces: `Program.BuildRootCommand(...)` internal factory，供 CLI 合约测试调用。
- Changes: wait-for 同时接受位置 condition 与 `--condition`。

- [ ] **Step 1: 写进程名规范化和 wait-for 双语法失败测试**

```csharp
[Theory]
[InlineData("Cafe.Launcher.Avalonia", "Cafe.Launcher.Avalonia.exe")]
[InlineData("Cafe.Launcher.Avalonia.exe", "Cafe.Launcher.Avalonia")]
public void EqualsIgnoringExe_AcceptsOptionalExe(string expected, string actual)
    => Assert.True(ProcessNameMatcher.EqualsIgnoringExe(expected, actual));

[Theory]
[InlineData("wait-for element --locator {}")]
[InlineData("wait-for --condition element --locator {}")]
public async Task WaitFor_AcceptsBothConditionForms(string commandLine)
{
    var result = await InvokeForParseOnly(commandLine);
    Assert.NotEqual("parse_error", result.ErrorCode);
}
```

- [ ] **Step 2: 写非 JSON 解析错误回归测试**

```csharp
[Fact]
public async Task ParseError_WritesSingleJsonResponse()
{
    var result = await InvokeCli("wait-for element unexpected-token");
    using var document = JsonDocument.Parse(result.Stdout);
    Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    Assert.Equal(2, result.ExitCode);
    Assert.DoesNotContain("\u001b[", result.Stdout);
}
```

- [ ] **Step 3: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~ProcessNameMatcherTests|FullyQualifiedName~CommandLineContractTests"`

Expected: FAIL，现有 parser 只接受 option 且自行写彩色文本。

- [ ] **Step 4: 实现兼容语法和统一解析错误**

`wait-for` 增加可选位置 argument；handler 用 `ResolveCondition(argument, option)`。两者不同返回 `INVALID_ARGUMENT`。在执行 handler 前检查 `ParseResult.Errors`，用 `ResponseWriter("parse")` 输出单个 JSON 并返回 2。

- [ ] **Step 5: 复用 WindowInfoReader**

`list-windows` 和 `observe` 都通过 `WindowInfoReader.Read(hwnd)` 填充 PID、process、title、class、visible、cloaked、minimized、foreground 与 rect。移除 `ObserveCommand` 中空 title/class。

- [ ] **Step 6: 运行目标测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~ProcessNameMatcherTests|FullyQualifiedName~CommandLineContractTests"`

Expected: PASS。

- [ ] **Step 7: 提交**

```powershell
git add src/Kagami/Utilities/ProcessNameMatcher.cs src/Kagami/Utilities/WindowInfoReader.cs src/Kagami/Backends/UiaAutomationBackend.cs src/Kagami/Commands/ObserveCommand.cs src/Kagami/Commands/WaitForCommand.cs src/Kagami/Program.cs tests/Kagami.Tests/Utilities/ProcessNameMatcherTests.cs tests/Kagami.Tests/Commands/CommandLineContractTests.cs
git commit -m "fix(cli): 统一命令语法与窗口身份"
```

### Task 6: UIA 可见性 warning 与 120 秒 guard

**Files:**
- Modify: `src/Kagami/Utilities/UiaTreeWarnings.cs`
- Modify: `src/Kagami/Backends/TempFileObservationGuardStore.cs`
- Modify: `src/Kagami/Commands/ObserveCommand.cs`
- Test: `tests/Kagami.Tests/Utilities/UiaTreeWarningsTests.cs`
- Test: `tests/Kagami.Tests/Backends/TempFileObservationGuardStoreTests.cs`

**Interfaces:**
- Produces: warning code `uia_visibility_ambiguous`。
- Changes: `TempFileObservationGuardStore.GuardTtl = 120 seconds`。

- [ ] **Step 1: 写重叠 Custom 根 warning 失败测试**

```csharp
[Fact]
public void ForAmbiguousVisibility_OverlappingVisibleCustomRoots_ReturnsWarning()
{
    var tree = Root(
        Custom(new DetailedRect { Left = 0, Top = 0, Right = 1000, Bottom = 700 }),
        Custom(new DetailedRect { Left = 0, Top = 0, Right = 1000, Bottom = 700 }));

    var warning = UiaTreeWarnings.ForAmbiguousVisibility(tree);

    Assert.Equal("uia_visibility_ambiguous", warning!.Code);
}
```

- [ ] **Step 2: 写 guard TTL 边界失败测试**

通过为 store 注入 `TimeProvider`，断言 31 秒仍有效、121 秒过期，错误消息包含 `120s TTL`。

- [ ] **Step 3: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~UiaTreeWarningsTests|FullyQualifiedName~TempFileObservationGuardStoreTests"`

Expected: FAIL，因为 warning 和可控时间源尚不存在，TTL 仍为 30 秒。

- [ ] **Step 4: 实现通用重叠检测和 TimeProvider**

重叠面积阈值为较小矩形面积的 80%；仅检查根的直接 Custom children。`ObserveCommand` 在树获取后追加 warning。store 默认使用 `TimeProvider.System`，测试传 fake provider。

- [ ] **Step 5: 运行目标测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~UiaTreeWarningsTests|FullyQualifiedName~TempFileObservationGuardStoreTests"`

Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add src/Kagami/Utilities/UiaTreeWarnings.cs src/Kagami/Backends/TempFileObservationGuardStore.cs src/Kagami/Commands/ObserveCommand.cs tests/Kagami.Tests/Utilities/UiaTreeWarningsTests.cs tests/Kagami.Tests/Backends/TempFileObservationGuardStoreTests.cs
git commit -m "fix(protocol): 延长 guard 并提示可见性歧义"
```

### Task 7: 文档契约和自动检查

**Files:**
- Modify: `README.md`
- Modify: `SKILL.md`
- Modify: `references/cli-workflow.md`
- Modify: `docs/DESIGN.md`
- Create: `tests/Kagami.Tests/Documentation/DocumentationContractTests.cs`

**Interfaces:**
- Consumes: Tasks 1–6 最终 CLI。
- Produces: 可执行的 README/SKILL 示例契约。

- [ ] **Step 1: 写文档命令契约失败测试**

测试读取 README、SKILL 和 workflow，提取标记为 `kagami` 的基础命令，至少断言：

```csharp
Assert.Contains("kagami click --hwnd", allDocs);
Assert.Contains("kagami wait-for element", allDocs);
Assert.Contains("kagami get-tree --hwnd", allDocs);
Assert.Contains("--runtime-id", allDocs);
Assert.Contains("kagami find", allDocs);
Assert.DoesNotContain("30s TTL", allDocs);
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~DocumentationContractTests`

Expected: FAIL，旧文档仍描述不安全 click、旧 TTL 或缺少新命令。

- [ ] **Step 3: 更新全部用户文档**

说明：

- 物理输入必须绑定目标且目标位于前台；
- `physical_input_generated` 不等于业务后置条件完成；
- wait-for 两种兼容语法；
- find、runtime-id、tree_path、interactive-only、include-locators；
- 120 秒 guard 与 fresh observation 要求；
- `is_offscreen` 是 provider 信号，视觉可见性需截图确认。

- [ ] **Step 4: 运行文档测试并确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter FullyQualifiedName~DocumentationContractTests`

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add README.md SKILL.md references/cli-workflow.md docs/DESIGN.md tests/Kagami.Tests/Documentation/DocumentationContractTests.cs
git commit -m "docs: 同步安全输入与树查询契约"
```

### Task 8: 完整验证与 Cafe Launcher 实测

**Files:**
- Create: `tests/e2e/Test-CafeLauncher.ps1`

**Interfaces:**
- Produces: 可重复的本地 dogfood 脚本；不作为依赖 Cafe 仓库的 CI 必跑项。

- [ ] **Step 1: 编写只操作设置入口的 E2E 脚本**

脚本参数：`-KagamiPath`、`-CafeLauncherPath`。脚本启动目标，捕获 PID/HWND，验证八项规格，并在 `finally` 只终止自己启动的进程。禁止点击“开始游戏”“官方网站”或保存设置。

- [ ] **Step 2: 运行全部单元与集成测试**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj -c Debug`

Expected: 0 failed，0 skipped（环境不具备桌面窗口时允许现有早退测试，但不得新增无条件 skip）。

- [ ] **Step 3: 发布 Release**

Run: `dotnet publish src/Kagami/Kagami.csproj -c Release -r win-x64 -o artifacts/kagami-win-x64`

Expected: exit 0，生成 `artifacts/kagami-win-x64/kagami.exe`。

- [ ] **Step 4: 运行 Cafe Launcher dogfood**

```powershell
powershell -ExecutionPolicy Bypass -File tests/e2e/Test-CafeLauncher.ps1 `
  -KagamiPath artifacts/kagami-win-x64/kagami.exe `
  -CafeLauncherPath E:\Repos\Cafe.Launcher.Avalonia\bin\Debug\net10.0\Cafe.Launcher.Avalonia.exe
```

Expected: 八项检查全部 PASS；后台 click/key 被拒绝；设置 locator invoke 成功；精简输出字符数少于默认输出。

- [ ] **Step 5: 检查工作区和差异质量**

Run: `git diff --check`

Run: `git status --short`

Expected: 无空白错误；只包含计划内文件和任务开始前已有修改。

- [ ] **Step 6: 提交 E2E 脚本**

```powershell
git add tests/e2e/Test-CafeLauncher.ps1
git commit -m "test(e2e): 增加 Cafe Launcher Agent 工作流验证"
```
