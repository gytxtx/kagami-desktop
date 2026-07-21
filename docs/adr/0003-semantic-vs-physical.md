# ADR 0003: Semantic vs. physical interaction separation

## Status

Accepted

## Context

UIA Pattern 操作（`InvokePattern.Invoke`、`ValuePattern.SetValue`）是异步语义调用，不经过鼠标命中测试、悬停、按下释放、焦点切换等真实输入路径。它们验证的是"控件的 UIA provider 能执行该动作"，而不是"用户可以通过鼠标/键盘触发该动作"。

本工具的定位是"AI Agent 的视觉辅助验证 / 实际行为测试"，两种验证都需要。

## Decision

- 协议中显式区分 `semantic`、`physical`、`auto` 三种交互模式
- `invoke` → 语义点击（UIA InvokePattern）
- `click` → 物理鼠标点击（SendInput 坐标）
- `type-text --mode value` → 语义输入（ValuePattern.SetValue）
- `type-text --mode keyboard` → 物理键盘输入（SendInput）
- `--mode auto` → 优先 semantic，降级时输出实际路径
- 输出中必须报告 `interaction.mode_actual` 和 `interaction.physical_input_generated`

## Consequences

- 命令命名必须承载交互语义（`invoke` vs `click`，而非统一 `click` 加参数）
- Agent 需要理解两种测试路径的区别，并在验证场景中选择正确的命令
- 为每个操作输出 interaction 元数据，方便 Agent 日志和调试
