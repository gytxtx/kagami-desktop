# Kagami Desktop — AI Agent Windows 桌面观察与操作协议

## 概述

Kagami Desktop 是一个 CLI 工具，为 AI Agent 提供 Windows 桌面的"眼睛和手"。
它通过子进程被 Agent 调用，以结构化 JSON 输入输出，不内嵌 AI 推理逻辑。

- **API 兼容**：Windows 10 1903+
- **主动测试与支持**：Windows 11
- **尽力兼容**：Windows 10 1903–22H2 / LTSC
- **架构**：C#/.NET 10，one-shot CLI（后期可演进为 daemon）

## 核心概念

### 交互模式（Interaction Mode）

| 模式 | 说明 |
|---|---|
| **Semantic** | UIA Pattern 操作（InvokePattern、ValuePattern 等），验证控件 provider 行为 |
| **Physical** | 真实鼠标/键盘输入（SendInput），验证像素级可达和用户实际体验 |
| **Auto** | 优先 semantic，失败时按策略降级 |

协议输出必须报告实际使用的交互路径：

```json
{
  "interaction": {
    "mode_requested": "auto",
    "mode_actual": "uia-invoke-pattern",
    "physical_input_generated": false
  }
}
```

`physical_input_generated = true` 仅表示输入已注入，不代表业务后置条件完成。物理输入结果还返回 `target_hwnd`、`target_foreground_verified` 和 `target_delivery_verified`；Agent 必须继续执行条件等待和 fresh observation，才能判断用户目标是否达成。

### 截图模式（Capture Mode）

| 模式 | 说明 | 后端 |
|---|---|---|
| `window` | 请求捕获单个窗口表面 | legacy_window_capture（PrintWindow → DWM Thumbnail） |
| `visible-desktop` | 捕获桌面合成帧上指定区域 | DXGI Desktop Duplication |
| `auto` | 优先 `window`，失败时按策略降级 | 自动选择 |

> **当前状态：** WGC (`CreateForWindow`) 尚未实现。MVP 使用 `legacy_window_capture` 后端（PrintWindow + DWM Thumbnail + GDI fallback）。每次捕获的结果报告 `capture_backend` 和 `capture_method` 以透明区分实际使用的技术。

跨后端的语义降级（`window` → `visible-desktop`）默认关闭，需 `--allow-semantic-fallback` 显式允许。`legacy_window_capture` 内部仍可在 PrintWindow/DWM 失败后退化为可见桌面裁剪；该结果报告 `actual_mode = visible-desktop-crop`、`fallback_used = true`、`occlusion_possible = true`，所以 `window` 请求不等于无条件保证无遮挡。
返回 `actual_mode`、`capture_backend`、`fallback_used`、`occlusion_possible`。

### 协调快照（Coordinated Snapshot）

`observe` 命令在尽可能紧凑的时间窗口内采集截图、UIA 树和窗口状态，不保证原子性。
返回采集时间区间和稳定性评估：

```json
{
  "observation_id": "uuid",
  "guard_path": "C:\\...\\guard-uuid.json",
  "started_at": "2026-07-20T12:00:00.000Z",
  "screenshot_at": "2026-07-20T12:00:00.045Z",
  "uia_completed_at": "2026-07-20T12:00:00.089Z",
  "window_rect_before": { "x": 100, "y": 200, "w": 800, "h": 600 },
  "window_rect_after": { "x": 100, "y": 200, "w": 800, "h": 600 },
  "stable": true,
  "instability_reasons": []
}
```

### 状态 Guard（Observation Guard）

One-shot CLI 的跨调用状态一致性通过 guard 文件实现。

`observe` 写入 guard 文件（`%TEMP%\kagami\guards\<uuid>.json`），包含：

```json
{
  "hwnd": "0x1234",
  "pid": 9876,
  "process_start_time": "2026-07-20T12:00:00.000Z",
  "foreground_hwnd": "0x1234",
  "window_rect": { "x": 100, "y": 200, "w": 800, "h": 600 },
  "root_runtime_id": [42, 1],
  "captured_at": "2026-07-20T12:00:00.045Z"
}
```

状态变更命令 `invoke`、`click`、`type-text`、`key` 支持 `--expected-state`，以 `--expected-state guard-uuid.json` 传入；观察型 `wait-for` 也接受该 option，`activate` 不接受。使用 guard 的命令在继续前验证：
- HWND 仍然存在
- PID 匹配且进程启动时间一致（防止 PID 复用）
- 窗口矩形未显著变化
- 前台窗口一致

不一致返回 `STALE_OBSERVATION`。Guard 文件超过 120 秒自动视为失效；120 秒只是最长 TTL，不是界面仍新鲜的证明。每次状态变更前仍需 fresh observation/实时校验，不能因为 guard 尚未过期就重放旧操作。
命名 Mutex（`Global\Kagami-{user-session-id}`）仅用于防止并行输入冲突。

### Locator（元素定位路径）

每个 UIA 节点返回可重新解析的定位路径：

```json
{
  "locator": {
    "window": { "hwnd": "0x123456" },
    "path": [
      {
        "control_type": "Window",
        "automation_id": "MainWindow",
        "name": "Cafe Launcher",
        "class_name": "Window",
        "ordinal": 0
      },
      {
        "control_type": "Button",
        "automation_id": "BtnLogin",
        "name": "Login",
        "class_name": "Button",
        "ordinal": 2
      }
    ]
  },
  "runtime_id": [42, 5678]
}
```

解析规则：
- `automation_id` 优先匹配，`control_type + name` 次之，`class_name + ordinal` 最后
- `ordinal` 是满足前面条件的兄弟元素中的序号，非原始子节点序号
- 每段解析检测匹配数量：0 → `LOCATOR_NOT_FOUND`，>1 → `LOCATOR_AMBIGUOUS`
- `runtime_id` 仅用于验证短暂时间窗口内重新解析到的节点是否仍是原节点

## MVP 命令集（第一阶段）

```
capabilities    — 查询环境能力
list-windows    — 枚举所有顶层窗口
observe         — 协调快照（截图 + UIA 树 + 窗口状态 + 光标）
get-tree        — 逐层展开 UIA 控件树
find            — 按 UIA 属性低成本查找控件
screenshot      — 独立截图（窗口/区域/显示器）

activate        — 尝试将窗口带到前台
invoke          — 语义点击（UIA InvokePattern）
click           — 物理鼠标坐标点击
type-text       — 文本输入（--mode value|keyboard|auto，--allow-clipboard）
key             — 组合键输入

wait-for        — 条件等待（element/element-gone/property/window/window-rect-stable/screenshot-stable）
```

根命令和子命令通过 `--help` 输出实际语法；默认 help 别名为 `-h`、`/h`、`-?`、`/?`，`--version` 输出程序集版本。help/version 成功时退出码为 0。

推迟到 post-MVP：`focus`、`scroll`、`cleanup`。

## 命令详解

### `capabilities`

查询环境能力，Agent 不应盲猜功能可用性。

```json
{
  "windows_version": "10.0.22631",
  "dpi_awareness": "per-monitor-v2",
  "capture_backends": {
    "legacy_window_capture": true,
    "desktop_duplication": true,
    "legacy_gdi": true
  },
  "uia": { "version": 3 },
  "elevated": false,
  "interactive_session": true
}
```

### `list-windows`

```
list-windows [--visible-only] [--process-name X] [--title X]
```

返回窗口列表，每个条目包含 `hwnd`、`pid`、`process_name`、`title`、`class_name`、`visible`、`cloaked`、`minimized`、`foreground`、`rect`。

### `observe`

```
observe --hwnd X [--depth 1] [--max-nodes 200] [--view control]
        [--interactive-only] [--include-locators all|interactive|none]
        [--capture-mode window|visible-desktop|auto] [--allow-semantic-fallback]
```

协调采集截图 + UIA 树 + 窗口状态 + 光标，返回 observation 结果和 guard 文件路径。

### `get-tree`

```
get-tree --hwnd X [--runtime-id "42.1234" | --path "0/2" | --locator '{...}']
         [--depth 1] [--max-nodes 200] [--view control|content|raw]
         [--interactive-only] [--include-locators all|interactive|none]
```

逐层展开 UIA 控件树。默认 `ControlView`，默认展开一层，默认最多 200 子节点。
节点结构：

```json
{
  "node_id": "temporary-id",
  "runtime_id": [42, 5678],
  "tree_path": "0/2",
  "control_type": "Button",
  "name": "Login",
  "automation_id": "BtnLogin",
  "class_name": "Button",
  "framework_id": "Avalonia",
  "process_id": 1234,
  "native_window_handle": "0x0",
  "rect": { "left": 450, "top": 500, "right": 550, "bottom": 540 },
  "clickable_point": { "x": 500, "y": 520 },
  "is_enabled": true,
  "is_offscreen": false,
  "is_keyboard_focusable": true,
  "has_keyboard_focus": false,
  "is_virtualized": false,
  "patterns": ["invoke"],
  "children_count": 3,
  "children_truncated": true,
  "locator": { ... }
}
```

`children_count` 是当前响应中输出的直接子节点数量；当节点预算或深度限制导致未输出全部可见子节点时，`children_truncated` 为 `true`。

`--path`、`--runtime-id`、`--locator` 最多传一个，分别从当前 UIA view 的索引路径、短期 Runtime ID 或可重解析 locator 开始展开。`tree_path` 相对于请求的 view；Runtime ID 与 tree path 用于当前观察内的渐进发现，不作为持久 locator。`--interactive-only` 保留交互节点及其祖先，`--include-locators all|interactive|none` 分别输出全部、仅交互节点或不输出 locator。

### `find`

```
find --hwnd X [--name X] [--automation-id X] [--control-type X] [--class-name X]
     [--max-results 20] [--view control|content|raw]
```

至少提供一个属性筛选条件。`find` 用于先低成本定位候选，再把返回的 `runtime_id` 或 `locator` 交给 `get-tree` 做局部展开；返回结果包含 `tree_path`。

### `screenshot`

```
screenshot [--hwnd X] [--x Y --y Y --w W --h H] [--display N]
           [--mode window|visible-desktop|auto] [--allow-semantic-fallback]
           [--output path.png]
```

返回 `path`、`width`、`height`、`rect`、`capture_backend`、`actual_mode`、`fallback_used`。

### `activate`

```
activate --hwnd X
```

调用 `SetForegroundWindow`。返回 `activated`、`foreground_hwnd`。
失败时返回 `FOREGROUND_ACTIVATION_DENIED` 错误码。

### `invoke`

```
invoke --locator '{...}' [--expected-state guard-uuid.json]
```

通过 UIA InvokePattern 语义点击。执行前重新解析 locator 并验证状态 guard。

### `click`

```
click --hwnd X --x Y --y Y [--right] [--expected-state guard-uuid.json]
```

物理鼠标点击（`SendInput`），屏幕物理坐标。`click` 必须通过显式 `--hwnd` 或已验证的 `--expected-state` guard 绑定目标；两者同时存在时必须一致。注入前目标窗口必须位于前台，且坐标必须命中同一窗口族。

### `type-text`

```
type-text --text "hello" [--hwnd X] [--locator '{...}']
          [--mode value|keyboard|auto] [--allow-clipboard]
          [--expected-state guard-uuid.json]
```

文本输入优先级（`auto` 模式下）：
1. UIA `ValuePattern.SetValue`
2. Unicode `SendInput`
3. 剪贴板粘贴（仅 `--allow-clipboard` 时启用）

`type-text --mode keyboard` 必须显式提供 `--hwnd`，并在 `SendInput` 前实时校验目标窗口族位于前台。`auto` 若要允许物理路径，也应提供 HWND；guard 可额外校验观察状态，但不会替代 keyboard 模式的 HWND。

`--allow-clipboard` 模式下：保存 sequence number → 写入 → 粘贴 → 检查 sequence number → 仅当未被中间修改时恢复；被修改时返回 warning 且不覆盖。

### `key`

```
key --keys "CTRL+L" --hwnd X [--expected-state guard-uuid.json]
```

键盘组合键（`SendInput` 虚拟键）。HWND 必填；目标窗口必须位于前台。guard 可额外校验观察状态，但不替代 HWND。

### `wait-for`

```
wait-for element --hwnd X --locator '{...}' [--timeout 10000] [--poll-interval 200]
wait-for element-gone --hwnd X --locator '{...}'
wait-for property --locator '{...}' --property is_enabled --equals true
wait-for window --process X [--title X]
wait-for window-rect-stable --hwnd X [--consecutive 5]
wait-for screenshot-stable --hwnd X [--region x,y,w,h] [--threshold 0.95] [--consecutive 3]
```

位置 condition 是首选语法（例如 `wait-for element ...`）；`wait-for --condition element ...` 为现有调用方保留兼容。两者同时提供时必须一致。

`wait-for idle` 仅返回 `process-input-idle` 条件，不为 `application_ready` 语义负责。

## 全局输出格式

```json
{
  "schema_version": "1.0",
  "success": true,
  "command": "observe",
  "elapsed_ms": 124,
  "data": { },
  "warnings": [],
  "error": null
}
```

### 错误格式

```json
{
  "error": {
    "code": "WINDOW_NOT_FOUND",
    "message": "No window matching title 'Blue Archive' found",
    "retryable": true,
    "native_code": null,
    "details": {
      "candidates": ["Blue Archive (Japan)", "BlueArchive.exe"]
    }
  }
}
```

### 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功 |
| 1 | 预期的操作失败 |
| 2 | 程序自身异常或协议错误 |

- stdout → 成功和失败均为单个 JSON 文档
- stderr → 日志和 traceback（`--verbose` 时）
- 命令行解析错误 → 单个 JSON 错误响应，退出码 2
- `--help` / `--version` → 面向人的文本输出，退出码 0，不使用 JSON envelope
- 图片 → 临时文件路径（`%TEMP%\kagami\screenshots\<uuid>.png`，每次调用清理 5 分钟以上的旧文件）

## 架构抽象层

协议层不直接暴露原生 API 类型：

```
ICaptureBackend
  ├─ LegacyWindowCaptureBackend       (PrintWindow / DWM Thumbnail)
  ├─ DesktopDuplicationBackend        (DXGI)
  └─ LegacyCaptureBackend             (GDI CopyFromScreen)

IAutomationBackend
  └─ UiaAutomationBackend             (FlaUI UIA3)

IInputBackend
  └─ Win32InputBackend                (UIA Patterns + SendInput + Clipboard)

IObservationGuardStore
  └─ TempFileObservationGuardStore    (临时文件方案)
```

## DPI 与坐标系

- 进程 manifest 声明 `PerMonitorV2`
- API 对外所有坐标均为**物理屏幕像素**
- 坐标原点为**虚拟桌面原点**（第二显示器在左侧时 x 可为负数）
- UIA BoundingRectangle 与截图坐标使用同一坐标系
- 返回每个显示器的 DPI 缩放比例

## UIA 相关约束

- 默认使用 `ControlView`（非 `RawView`）
- 属性获取使用 `CacheRequest` 批量请求，非逐节点 COM 调用
- 查询 Patterns 时检查 `IsPatternAvailable` 属性，避免逐个试探
- 处理虚拟化元素：识别 `VirtualizedItemPattern`，返回 `is_virtualized` 字段
- UIA 在工作线程 MTA 上执行；主线程处理 CLI 参数解析、Mutex、watchdog
- 区分 `OPERATION_TIMEOUT`（可控）和 `UIA_PROVIDER_UNRESPONSIVE`（kill 进程）
- `is_offscreen` 是 UIA provider 信号，并不证明元素在像素层面视觉可见；视觉可见性需用当前截图确认。重叠顶层 Custom surface 可能触发 `uia_visibility_ambiguous` warning。

## 并发保护

1. **命名 Mutex**：`Global\Kagami-{user-session-id}`，防止两个 CLI 实例同时执行输入操作
2. **状态 Guard**：`--expected-state` 乐观锁，执行前验证窗口状态未变
3. **Agent 侧超时**：子进程级别的最终保障

## 端到端验收标准

MVP 完成的判定标准：

```
1. 找到 Avalonia 启动器窗口
2. legacy_window_capture 成功获得窗口截图
3. 获取 UIA ControlView 第一层
4. 通过 locator 重新找到按钮
5. 分别执行 semantic invoke 和 physical click
6. 等待结果元素出现
7. 再次 observe
8. 判断 visual state 和 UIA state 一致
```

## 项目结构

```
E:\Repos\kagami-desktop\
├── docs\
│   ├── DESIGN.md              (本文件)
│   └── adr\
├── src\
│   └── Kagami\
│       ├── Kagami.csproj
│       ├── Program.cs
│       ├── Commands\
│       ├── Backends\
│       │   ├── ICaptureBackend.cs
│       │   ├── LegacyWindowCaptureBackend.cs
│       │   ├── DesktopDuplicationBackend.cs
│       │   └── LegacyCaptureBackend.cs
│       ├── IAutomationBackend.cs
│       ├── UiaAutomationBackend.cs
│       ├── IInputBackend.cs
│       ├── Win32InputBackend.cs
│       ├── IObservationGuardStore.cs
│       ├── TempFileObservationGuardStore.cs
│       ├── Protocol\
│       │   ├── JsonResponse.cs
│       │   ├── ErrorCodes.cs
│       │   ├── Locator.cs
│       │   └── ...
│       └── Utilities\
│           ├── DpiHelper.cs
│           ├── ProcessHelper.cs
│           └── TempFileManager.cs
└── tests\
    └── Kagami.Tests\
        └── Kagami.Tests.csproj
```
