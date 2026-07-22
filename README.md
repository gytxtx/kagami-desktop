# Kagami Desktop

> **鏡 (Kagami)** — 日语"镜子"之意。为 AI Agent 提供看清 Windows 桌面的眼睛，和与之交互的手指。

Kagami Desktop 是一个 Windows 原生 CLI 工具，让 AI Agent 可以**观察**（截图 + UI 控件树）和**操作**（点击、输入、语义交互）桌面窗口。通过子进程调用，结构化 JSON 输入输出，不内嵌 AI 逻辑。

## 为什么

AI Agent 操作桌面应用时的两个核心困难：

1. **看不见** — 传统截图工具截不到硬件加速窗口（Avalonia、WPF、浏览器），BitBlt 返回黑屏
2. **点不中** — 只能用像素坐标盲点，UI 变化后立即失效

Kagami Desktop 的解法：

- **截图三后端**：PrintWindow → DWM Thumbnail → DXGI Desktop Duplication → GDI CopyFromScreen
- **UIA 控件树**：遍历 UI Automation 树（类 DOM），每节点带可重解析 Locator，Agent 用语义点击不依赖坐标

## 安装

```powershell
git clone https://github.com/gytxtx/kagami-desktop.git
cd kagami-desktop
dotnet publish src/Kagami/Kagami.csproj -c Release -r win-x64 -o publish
```

要求：Windows 10 1903+, .NET 10 SDK。

## 快速开始

```powershell
kagami capabilities                                # 环境能力
kagami list-windows --title "Cafe Launcher"        # 找窗口 → 拿到 HWND
kagami observe --hwnd 0x607fc --depth 2            # 截图 + 控件树 + 窗口状态
kagami screenshot --hwnd 0x607fc --mode window     # 窗口截图（PrintWindow/DWM）
kagami find --hwnd 0x607fc --control-type Button --name "开始游戏"
kagami get-tree --hwnd 0x607fc --depth 2 --interactive-only --include-locators interactive
kagami get-tree --hwnd 0x607fc --runtime-id "42.5678" --depth 1
kagami get-tree --hwnd 0x607fc --locator '{...}' --depth 1
kagami invoke --locator '{...}'                    # 语义点击
kagami click --hwnd 0x607fc --x 840 --y 560        # 物理坐标点击；目标必须在前台
kagami type-text --text "hello" --mode keyboard --hwnd 0x607fc
kagami key --keys "CTRL+L" --hwnd 0x607fc
kagami wait-for element --hwnd 0x607fc --locator '...' --timeout 10000
```

## 命令参考

```
kagami capabilities         # 环境能力查询
kagami list-windows         # 枚举顶层窗口
kagami observe              # 快照（截图 + 控件树 + 窗口状态）
kagami get-tree             # 逐层展开 UIA 控件树
kagami find                 # 按 UIA 属性查找控件
kagami screenshot           # 截图（窗口 / 区域 / 显示器 / 全桌面）

kagami activate             # 激活窗口
kagami invoke               # 语义点击 (InvokePattern)
kagami click --hwnd X --x Y --y Y              # 物理鼠标点击 (SendInput)
kagami type-text            # 输入文字
kagami key --keys "CTRL+L" --hwnd X             # 发送组合键
kagami wait-for             # 条件等待
```

## 核心概念

### 交互模式

| 模式 | 说明 |
|---|---|
| **Semantic** (`invoke`, `type-text --mode value`) | UIA Pattern 操作，验证控件行为 |
| **Physical** (`click`, `type-text --mode keyboard`, `key`) | SendInput，验证像素级可达 |
| **Auto** (默认) | 优先 semantic，降级时输出实际路径 |

物理输入有严格的目标契约：`click` 必须传 `--hwnd`，或通过已验证的 `--expected-state` guard 推导 HWND；`key` 与 `type-text --mode keyboard` 必须显式传 `--hwnd`。执行输入前，Kagami 会实时校验目标窗口族，且目标窗口必须位于前台；鼠标点击还会校验坐标命中同一窗口族。响应中的 `target_hwnd`、`target_foreground_verified` 和 `target_delivery_verified` 用于说明这些校验结果。

`physical_input_generated: true` 仅表示输入已注入，不代表业务后置条件完成。输入后仍应优先使用位置 condition 语法 `kagami wait-for element ...`，再执行 fresh observation 验证视觉和/或 UIA 状态；`kagami wait-for --condition element ...` 仅作为兼容语法保留。

### 截图模式

| 模式 | 方法 | 有无遮挡 |
|---|---|---|
| `window` | legacy_window_capture (PrintWindow → DWM Thumbnail) | **无** |
| `visible-desktop` | DXGI Desktop Duplication | 有 |
| `auto` | 自动最优 | — |

跨后端的语义降级（`window` → `visible-desktop`）默认关闭，需 `--allow-semantic-fallback` 显式允许。`legacy_window_capture` 后端自身仍可能在 PrintWindow/DWM 失败后退化为可见桌面裁剪；此时结果会明确报告 `actual_mode: "visible-desktop-crop"`、`fallback_used: true` 和 `occlusion_possible: true`，因此 `--mode window` 不保证最终截图无视遮挡。

> **注意：** "WGC" (Windows.Graphics.Capture) 尚未实现。当前 `window` 模式使用 `legacy_window_capture` 后端（PrintWindow + DWM Thumbnail + GDI fallback）。每个捕获结果报告 `capture_backend` 和 `capture_method` 字段以透明区分实际使用的技术。

### Locator（控件定位路径）

```json
{ "window": {"hwnd": "0x607fc"}, "path": [{"control_type":"Button","name":"开始游戏","ordinal":0}] }
```

解析优先级：AutomationId → ControlType+Name+ClassName → ControlType+ClassName+ordinal。多匹配返回 `LOCATOR_AMBIGUOUS`。

### 渐进式 UIA 查询

先用 `find` 缩小候选，再用 `get-tree` 从局部入口展开。`get-tree` 的 `--path`、`--runtime-id`、`--locator` 三种入口互斥；响应中的 `tree_path` 是当前 UIA view 下从窗口根开始的索引路径，`runtime_id` 只适合短时验证，持久操作仍使用 locator。

`--interactive-only` 仅保留交互节点及其祖先；`--include-locators all|interactive|none` 控制 locator 输出量。`is_offscreen` 是 UIA provider 信号，不保证视觉可见；尤其出现 `uia_visibility_ambiguous` warning 时，必须用当前截图确认。

### Observation guard

Guard 的最长有效期为 120 秒，但 TTL 不是“状态仍新鲜”的保证。每次状态变更前仍需 fresh observation，并在操作时传入新 guard；收到 `STALE_OBSERVATION` 后必须重新观察，不能重放旧坐标或旧状态。

### 输出格式

协议调用的 stdout 每次只输出单个 JSON 文档：`{"success":true,"data":{...},"error":null}`，诊断写入 stderr。成功退出码为 0，预期操作失败为 1；命令行解析错误同样返回单个 JSON，退出码 2。

## 架构

```
AI Agent ── subprocess ──→ kagami.exe
                             ├─ ICaptureBackend     (PrintWindow / DXGI / GDI)
                             ├─ IAutomationBackend  (FlaUI UIA3)
                             ├─ IInputBackend       (UIA Pattern / SendInput / Clipboard)
                             └─ IObservationGuardStore
```

C# / .NET 10 · FlaUI 4.0 · SharpDX · System.CommandLine

## 构建与测试

```powershell
dotnet test tests/Kagami.Tests/Kagami.Tests.csproj -c Debug
```

```text
kagami-desktop/
├── docs/DESIGN.md + adr/              # 技术设计与架构决策记录
├── src/Kagami/
│   ├── Program.cs                     # CLI 入口 (11 个子命令)
│   ├── Backends/                      # Capture / Automation / Input 接口与实现
│   ├── Commands/                      # 命令处理器
│   ├── Protocol/                      # JSON 协议类型
│   └── Utilities/                     # Win32 P/Invoke、辅助方法
└── tests/Kagami.Tests/                # xUnit tests
```

## 许可

MIT
