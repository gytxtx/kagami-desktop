# ADR 0001: One-shot CLI with temp-file observation guards

## Status

Accepted

## Context

Kagami 需要支持跨多次 CLI 调用的状态一致性验证。AI Agent 的典型流程是：

1. `observe` — 采集窗口截图和 UIA 树
2. 分析截图/tree
3. `invoke` / `click` — 执行操作
4. `observe` — 验证结果

步骤 2 到 3 之间可能经过数秒（Agent 推理时间），窗口状态可能已变化。

UIA Runtime ID 不能跨进程重新查找元素——Windows 不提供"根据任意 Runtime ID 重新获取 AutomationElement"的公共 API。

## Decision

采用 **临时 guard 文件** 方案（方案 B）：

- `observe` 写入 guard JSON 文件到 `%TEMP%\kagami\guards\<uuid>.json`
- 包含 HWND、PID、进程启动时间、窗口矩形、根 Runtime ID、采集时间
- 后续命令通过 `--expected-state guard-uuid.json` 传入
- 执行前验证窗口状态一致性；不一致返回 `STALE_OBSERVATION`
- Guard 文件超过 30 秒自动视为失效

命名 Mutex（`Global\Kagami-{user-session-id}`）仅用于防止并行输入冲突。

### Rejected alternative: 进程内 session/daemon

Daemon 模型在内存中保留 AutomationElement 引用更自然，但对 MVP 引入了进程生命周期管理、IPC 协议、和 daemon 无响应时的恢复策略。One-shot 反而有优势：某次 UIA COM 调用卡死时可以直接 kill 进程，不污染 Agent。

协议预留 `session_id` 字段，便于后期演进。

## Consequences

- 引入了磁盘状态（临时 guard 文件），不再是纯无状态 CLI
- 需要管理 guard 文件的生命周期（30 秒 TTL，超时后清理）
- Agent 在两次调用之间必须保持 guard 文件路径
- 验证逻辑需要对进程启动时间和 PID 做双重校验（防止系统复用 HWND 和 PID）
