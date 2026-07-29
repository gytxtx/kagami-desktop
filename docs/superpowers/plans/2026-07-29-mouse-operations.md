# 完整鼠标操作 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Kagami 提供经既有窗口安全校验保护的移动、双击、滚轮和拖拽命令。

**Architecture:** 在 `IInputBackend` 中定义四个独立手势，由 `Win32InputBackend` 构造 `SendInput` 鼠标序列。`InteractionCommands` 将 click 的 guard/HWND 绑定抽为共享流程；CLI 仅绑定参数。

**Tech Stack:** C#、.NET 10 Windows、System.CommandLine、Win32 `SendInput`、xUnit。

## Global Constraints

- 必须提供 `--hwnd` 或有效 `--expected-state`，二者同时存在时必须一致。
- 操作前目标必须在前台且坐标命中目标窗口族；拖拽的起点、终点都必须命中。
- `scroll --delta 0` 在输入注入前以 `INVALID_ARGUMENT` 拒绝。
- 不修改既有 `click`、`key` 和 `type-text` 行为，不隐式激活窗口，不支持脚本/宏。

---

## 文件结构

- `Utilities/NativeMethods.cs`：滚轮标志和单位。
- `Backends/IInputBackend.cs`：四种手势的选项、结果和接口方法。
- `Backends/Win32InputBackend.cs`：所有坐标验证后注入事件。
- `Commands/InteractionCommands.cs`：共享的 guard/HWND 安全解析和命令委派。
- `Program.cs`：四个 CLI 命令注册。
- 三个现有测试文件：后端序列、命令目标绑定、CLI 参数。
- `README.md`、`docs/DESIGN.md`、文档契约测试：公开并锁定行为。

### Task 1: 定义后端手势和事件序列

**Files:**
- Modify: `src/Kagami/Utilities/NativeMethods.cs:97-109`
- Modify: `src/Kagami/Backends/IInputBackend.cs:32-66`
- Modify: `src/Kagami/Backends/Win32InputBackend.cs:129-185,382-414`
- Test: `tests/Kagami.Tests/Backends/Win32InputBackendTests.cs`

**Interfaces:** Produces `MoveAsync`, `DoubleClickAsync`, `ScrollAsync`, `DragAsync`，均接收 `IntPtr targetHwnd`、所需坐标和 `CancellationToken`；返回包含手势数据和 `InteractionResult` 的强类型结果。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void DoubleClick_WithValidTarget_InjectsFiveEvents()
{
    var target = new IntPtr(100);
    var injector = new RecordingInputInjector();
    using var fixture = CreateFixture(ValidTargetWindows(target), injector);

    fixture.Input.DoubleClickAsync(target, 100, 100, false, CancellationToken.None).GetAwaiter().GetResult();

    Assert.Equal([5], injector.InputCounts);
}

[Fact]
public void Drag_WhenDestinationDoesNotHitTarget_DoesNotInject()
{
    var target = new IntPtr(100);
    var windows = ValidTargetWindows(target);
    windows.WindowAtPoint = new IntPtr(200);
    windows.ProcessIds[new IntPtr(200)] = 20;
    var injector = new RecordingInputInjector();
    using var fixture = CreateFixture(windows, injector);

    var error = Assert.Throws<CommandException>(() => fixture.Input.DragAsync(target, 10, 10, 20, 20, CancellationToken.None).GetAwaiter().GetResult());

    Assert.Equal(ErrorCodes.PointNotInTarget, error.ErrorCode);
    Assert.Equal(0, injector.Calls);
}
```

- [ ] **Step 2: 确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~Win32InputBackendTests"`

Expected: 编译失败，`DoubleClickAsync`、`DragAsync` 尚未存在。

- [ ] **Step 3: 最小实现**

```csharp
public Task<MouseMoveResult> MoveAsync(IntPtr targetHwnd, int x, int y, CancellationToken ct);
public Task<MouseClickResult> DoubleClickAsync(IntPtr targetHwnd, int x, int y, bool rightButton, CancellationToken ct);
public Task<MouseScrollResult> ScrollAsync(IntPtr targetHwnd, int x, int y, int delta, CancellationToken ct);
public Task<MouseDragResult> DragAsync(IntPtr targetHwnd, int fromX, int fromY, int toX, int toY, CancellationToken ct);
```

新增共享的虚拟桌面坐标检查。每个方法先检查所有坐标，再调用 `ValidatePointerTarget`；drag 对两个点都检查后才注入。注入数组：move `[MOVE]`、双击 `[MOVE, DOWN, UP, DOWN, UP]`、滚轮 `[MOVE, WHEEL]`、拖拽 `[MOVE(from), LEFTDOWN(from), MOVE(to), LEFTUP(to)]`。新增 `MOUSEEVENTF_WHEEL = 0x0800` 与 `WHEEL_DELTA = 120`；滚轮先以宽整数计算 `delta * WHEEL_DELTA`，零值或无法装入 Win32 32 位有符号 `mouseData` 的值抛出 `InvalidArgument`，之后再窄化并注入。`SendInput` 返回数必须等于数组长度，否则 `InputInjectionFailed`。

- [ ] **Step 4: 确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~Win32InputBackendTests"`

Expected: PASS；新增成功、失焦/命中拒绝、零 delta 和不完整注入场景通过。

- [ ] **Step 5: 提交**

```powershell
git add src/Kagami/Utilities/NativeMethods.cs src/Kagami/Backends/IInputBackend.cs src/Kagami/Backends/Win32InputBackend.cs tests/Kagami.Tests/Backends/Win32InputBackendTests.cs
git commit -m "feat: 增加物理鼠标手势后端"
```

### Task 2: 复用安全目标绑定并实现命令

**Files:**
- Modify: `src/Kagami/Commands/InteractionCommands.cs:83-134`
- Test: `tests/Kagami.Tests/Commands/InteractionCommandsTests.cs`

**Interfaces:** Consumes Task 1 的接口；produces `MoveAsync`, `DoubleClickAsync`, `ScrollAsync`, `DragAsync` 命令方法及私有 `ResolvePhysicalMouseTargetAsync`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task Scroll_WithValidatedGuard_DerivesTargetHwnd()
{
    var input = new RecordingInputBackend();
    var commands = CreateCommands(input, new StubGuardStore { Result = ValidGuard("0x1234") });

    var exitCode = await commands.ScrollAsync(100, 200, 2, null, "guard.json");

    Assert.Equal(0, exitCode);
    Assert.Equal(new IntPtr(0x1234), input.ScrollTargetHwnd);
}
```

- [ ] **Step 2: 确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~InteractionCommandsTests"`

Expected: 编译失败，`ScrollAsync` 未定义。

- [ ] **Step 3: 最小实现**

抽取 click 的 guard 加载、HWND 解析、冲突检查和必需目标判断：

```csharp
private async Task<IntPtr> ResolvePhysicalMouseTargetAsync(string? hwndStr, string? expectedStatePath, ResponseWriter writer, string command)
```

它保留既有 `StaleObservation` 和 `InvalidArgument` 错误码。新命令各自创建正确的 `ResponseWriter`，解析成功后委派 Task 1 后端；`RecordingInputBackend` 实现并记录四个新接口。增加无目标、guard 冲突和有效 guard 的每种命令测试。

- [ ] **Step 4: 确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~InteractionCommandsTests"`

Expected: PASS，既有 click 测试不变。

- [ ] **Step 5: 提交**

```powershell
git add src/Kagami/Commands/InteractionCommands.cs tests/Kagami.Tests/Commands/InteractionCommandsTests.cs
git commit -m "feat: 增加安全鼠标操作命令"
```

### Task 3: 注册 CLI 参数契约

**Files:**
- Modify: `src/Kagami/Program.cs:237-249`
- Test: `tests/Kagami.Tests/Commands/CommandLineContractTests.cs`

**Interfaces:** Produces `move`, `double-click`, `scroll`, `drag` CLI，分别绑定 Task 2 方法。

- [ ] **Step 1: 写失败测试**

```csharp
[Theory]
[InlineData("move", "--hwnd", "0x1", "--x", "10", "--y", "20")]
[InlineData("double-click", "--hwnd", "0x1", "--x", "10", "--y", "20")]
[InlineData("scroll", "--hwnd", "0x1", "--x", "10", "--y", "20", "--delta", "1")]
[InlineData("drag", "--hwnd", "0x1", "--from-x", "1", "--from-y", "2", "--to-x", "3", "--to-y", "4")]
public async Task MouseCommands_ParseRequiredArguments(params string[] args)
{
    var result = await InvokeCli(args);
    Assert.DoesNotContain("Unrecognized command", result.Stderr);
}
```

- [ ] **Step 2: 确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~CommandLineContractTests"`

Expected: FAIL，四个命令尚未识别。

- [ ] **Step 3: 最小实现**

在 `BuildRootCommand` 和 `CreateClickCommand` 邻近处注册四个 factory。move/double-click/scroll 都有必填 `--x --y`，滚轮有必填 `--delta`，双击有可选 `--right`；drag 有必填 `--from-x --from-y --to-x --to-y`。全部提供可选 `--hwnd --expected-state`，handler 直接委派 Task 2。

- [ ] **Step 4: 确认 GREEN**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~CommandLineContractTests"`

Expected: PASS，缺失必填参数会被解析器拒绝。

- [ ] **Step 5: 提交**

```powershell
git add src/Kagami/Program.cs tests/Kagami.Tests/Commands/CommandLineContractTests.cs
git commit -m "feat: 暴露完整鼠标操作 CLI"
```

### Task 4: 更新文档并完整验证

**Files:**
- Modify: `README.md:43-47,63-68,78-85`
- Modify: `docs/DESIGN.md:140-145,270-278`
- Test: `tests/Kagami.Tests/Documentation/DocumentationContractTests.cs`

**Interfaces:** Documents Task 3 的最终 CLI 和 Task 1 的安全行为。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void Readme_DocumentsAllPhysicalMouseCommands()
{
    var readme = File.ReadAllText(FindRepositoryFile("README.md"));
    Assert.Contains("kagami move --hwnd", readme);
    Assert.Contains("kagami double-click --hwnd", readme);
    Assert.Contains("kagami scroll --hwnd", readme);
    Assert.Contains("kagami drag --hwnd", readme);
}
```

- [ ] **Step 2: 确认 RED**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj --filter "FullyQualifiedName~DocumentationContractTests"`

Expected: FAIL，README 还未列出新命令。

- [ ] **Step 3: 最小实现**

在 README 的示例、命令表和物理输入安全段增加四个命令；在 `docs/DESIGN.md` 记录语法、滚轮正负方向、右键双击和拖拽双端命中。保留输入成功并不代表业务后置条件成功的说明，建议新鲜观测验证。

- [ ] **Step 4: 完整验证**

Run: `dotnet test tests/Kagami.Tests/Kagami.Tests.csproj`

Expected: PASS。

Run: `dotnet build src/Kagami/Kagami.csproj --no-restore`

Expected: Build succeeded, 0 errors。

Run: `git diff --check`

Expected: 无输出。

- [ ] **Step 5: 提交**

```powershell
git add README.md docs/DESIGN.md tests/Kagami.Tests/Documentation/DocumentationContractTests.cs
git commit -m "docs: 说明完整鼠标操作"
```
