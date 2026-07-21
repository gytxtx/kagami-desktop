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

### 截图模式（Capture Mode）

| 模式 | 说明 | 后端 |
|---|---|---|
| `window` | 捕获单个窗口表面（即使被遮挡） | legacy_window_capture（PrintWindow → DWM Thumbnail） |

> **当前状态：** WGC (`CreateForWindow`) 尚未实现。MVP 使用 `legacy_window_capture` 后端（PrintWindow + DWM Thumbnail + GDI fallback）。每次捕获的结果报告 `capture_backend` 和 `capture_method` 以透明区分实际使用的技术。
| `visible-desktop` | 捕获桌面合成帧上指定区域 | DXGI Desktop Duplication |
| `auto` | 优先 `window`，失败时按策略降级 | 自动选择 |

跨语义降级（`window` → `visible-desktop`）默认关闭，需 `--allow-semantic-fallback` 显式允许。
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

后续命令 `--expected-state guard-uuid.json` 传入。执行前验证：
- HWND 仍然存在
- PID 匹配且进程启动时间一致（防止 PID 复用）
- 窗口矩形未显著变化
- 前台窗口一致

不一致返回 `STALE_OBSERVATION`。Guard 文件超过 30 秒自动视为失效。
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
screenshot      — 独立截图（窗口/区域/显示器）

activate        — 尝试将窗口带到前台
invoke          — 语义点击（UIA InvokePattern）
click           — 物理鼠标坐标点击
type-text       — 文本输入（--mode value|keyboard|auto，--allow-clipboard）
key             — 组合键输入

wait-for        — 条件等待（element/element-gone/property/window/window-rect-stable/screenshot-stable）
```

推迟到 post-MVP：`focus`、`find`、`scroll`、`cleanup`。

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
        [--capture-mode window|visible-desktop|auto] [--allow-semantic-fallback]
```

协调采集截图 + UIA 树 + 窗口状态 + 光标，返回 observation 结果和 guard 文件路径。

### `get-tree`

```
get-tree --hwnd X [--runtime-id "42.1234"] [--path "0/2"]
         [--depth 1] [--max-nodes 200] [--view control|content|raw]
```

逐层展开 UIA 控件树。默认 `ControlView`，默认展开一层，默认最多 200 子节点。
节点结构：

```json
{
  "node_id": "temporary-id",
  "runtime_id": [42, 5678],
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
click --x Y --y Y [--right] [--expected-state guard-uuid.json]
```

物理鼠标点击（`SendInput`），屏幕物理坐标。

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

`--allow-clipboard` 模式下：保存 sequence number → 写入 → 粘贴 → 检查 sequence number → 仅当未被中间修改时恢复；被修改时返回 warning 且不覆盖。

### `key`

```
key --keys "CTRL+L" [--hwnd X] [--expected-state guard-uuid.json]
```

键盘组合键（`SendInput` 虚拟键）。

### `wait-for`

```
wait-for element --hwnd X --locator '{...}' [--timeout 10000] [--poll-interval 200]
wait-for element-gone --hwnd X --locator '{...}'
wait-for property --locator '{...}' --property is_enabled --equals true
wait-for window --process X [--title X]
wait-for window-rect-stable --hwnd X [--consecutive 5]
wait-for screenshot-stable --hwnd X [--region x,y,w,h] [--threshold 0.95] [--consecutive 3]
```

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

- stdout → JSON
- stderr → 日志和 traceback（`--verbose` 时）
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
