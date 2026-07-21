# ADR 0002: Capture backend selection and fallback policy

## Status

Accepted

## Context

截图有三种主路径：

1. **Windows Graphics Capture (WGC)** — `CreateForWindow(HWND)` 捕获单个窗口表面，即使被遮挡或硬件加速渲染
2. **DXGI Desktop Duplication** — 以显示器 output 为单位捕获桌面合成结果，不感知窗口边界
3. **PrintWindow / BitBlt** — 传统 GDI 路径，对硬件加速窗口常返回黑屏

三者有不同的观察语义：WGC 捕获的是窗口表面，DXGI 裁剪捕获的是"当前桌面上可见的那块区域"（可能包含遮挡），BitBlt 捕获的是 GDI 兼容表面。

## Decision

- 默认 `screenshot --hwnd X` → 优先 WGC `CreateForWindow`
- `screenshot --display N` / `--x --y --w --h` → DXGI Desktop Duplication
- `screenshot`（无参数）→ DXGI Desktop Duplication 全桌面
- 跨语义降级（`window` → `visible-desktop` → `legacy`）**默认关闭**
- 需 `--allow-semantic-fallback` 显式允许
- 输出报告 `actual_mode`、`capture_backend`、`fallback_used`、`occlusion_possible`

### Rejected alternative: 静默 fallback

静默 fallback 会让 Agent 将"被另一个窗口遮挡"误判为被测试应用自己的显示结果。必须让 Agent 知道截图语义何时发生了变化。

## Consequences

- WGC 仅支持 Win10 1903+，与整体兼容目标一致
- DXGI 需处理多显示器（虚拟桌面拼接）、鼠标指针、旋转显示器等边缘情况
- 接口抽象为 `ICaptureBackend`，后期可扩展其他后端
