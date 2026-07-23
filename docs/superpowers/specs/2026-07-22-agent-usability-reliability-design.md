# Kagami Desktop Agent 易用性与可靠性修复设计

## 背景

Kagami 已能稳定捕获 Cafe Launcher 这类硬件加速 Avalonia 窗口，但真实项目测试暴露出两个会阻止 AI Agent 安全使用的根本问题：

1. 物理输入只证明 `SendInput` 已调用，目标不在前台时仍可能返回成功并把输入发送到无关窗口。
2. `observe` 返回的 locator 无法被 `invoke` 原样重新解析，语义操作契约没有闭环。

此外，CLI 与文档存在漂移，渐进式树遍历缺少可复用入口，观察输出成本偏高，固定 30 秒 guard TTL 也不适配 Agent 的推理和工具调度延迟。

## 目标

- 物理输入只在目标窗口身份、前台状态和点击命中关系均可信时执行。
- 每个对外返回的 locator 都能由同一版本工具立即重新解析。
- Agent 可以用短命令查找和局部展开控件，而无需重复传递大段 JSON。
- README、SKILL、DESIGN、`--help` 和真实 CLI 行为保持一致。
- 保留当前截图能力、结构化协议和 one-shot CLI 架构，不引入 daemon 或新运行时依赖。

## 非目标

- 不实现 Windows Graphics Capture。
- 不引入常驻服务、跨进程 observation session 或远程控制协议。
- 不针对 Cafe Launcher 的类名、AutomationId 或布局写特例。
- 不承诺仅凭 UIA provider 属性判断视觉可见性。

## 兼容性策略

安全优先于保持不安全行为：

- 已提供有效目标且目标位于前台的调用保持兼容。
- `click` 可通过新增 `--hwnd` 或 `--expected-state` guard 确定目标；两者都没有时拒绝执行。
- `key` 和 keyboard 模式 `type-text` 在目标不位于前台时拒绝执行。
- `wait-for --condition element` 保留，同时新增文档既有的 `wait-for element` 写法。
- 现有 JSON 字段保持不变；新增字段采用向后兼容扩展。

## 设计一：共享物理输入安全策略

新增单一的物理输入目标校验单元，供 `click`、`key` 和 keyboard `type-text` 共用。

校验输入包括：

- 显式 HWND，或从 observation guard 读取的目标 HWND；
- 当前前台窗口；
- 目标 PID 与进程启动时间；
- 对坐标点击，还包括 `WindowFromPoint` 的命中 HWND。

校验规则：

1. 无法确定目标 HWND 时返回 `INVALID_ARGUMENT`，不调用 `SendInput`。
2. 前台窗口必须属于目标进程；目标进程的 owned popup 或对话框可接受。
3. 坐标命中的窗口必须属于目标进程，并能沿 owner/root ancestor 关系归一到目标窗口族。
4. guard 继续验证 HWND、PID、进程启动时间和窗口矩形，但“快照时另一个应用一直位于前台”不再视为安全。
5. 任一校验失败时返回结构化错误，且 `physical_input_generated=false`。

交互结果保留 `physical_input_generated`，并新增：

- `target_hwnd`
- `target_foreground_verified`
- `target_delivery_verified`

其中 `target_delivery_verified` 只表示输入注入前的路由条件已验证，不表示业务后置条件已完成。业务成功仍需 fresh observation 或 `wait-for` 验证。

## 设计二：Locator 构造与解析同源

当前树输出使用 ControlView，但 locator 构造沿元素的默认父链上溯，解析又以另一种 children 枚举方式逐段下降。这会把不同 UIA View 的层级混入同一路径。

修复方案：

- locator 构造和解析显式使用同一种 UIA TreeWalker。
- 节点记录其 locator 所属的 view；缺省保持 `control`。
- segment 的候选筛选、AutomationId/name/class 优先级和 ordinal 计算由同一个匹配函数负责。
- 空字符串 name 不作为高优先级稳定键；缺少 AutomationId/name 时使用 class + ordinal。
- 解析失败时返回：失败段索引、segment 内容、候选数量和候选摘要。

验收契约：任何 `observe`、`get-tree` 或 `find` 返回的非空 locator，都必须在目标树未变化时立即 round-trip resolve 到相同 RuntimeId。

## 设计三：渐进式树与查找接口

### `get-tree`

新增：

- `--runtime-id <id>`
- `--locator <json>`

每个节点新增 `tree_path`。`--path`、`--runtime-id` 和 `--locator` 必须使用相同 view。

### `find`

将现有 backend `FindAsync` 暴露为 CLI：

```text
kagami find --hwnd <HWND> [--name <text>] [--control-type <type>]
            [--automation-id <id>] [--class-name <class>] [--max-results 20]
            [--view control|content|raw]
```

至少提供一个筛选条件；结果返回精简节点数组和可复用 locator。

### 输出成本

新增：

- `--interactive-only`：仅保留具备交互 pattern、键盘可聚焦或可编辑的节点，并保留必要祖先。
- `--include-locators all|interactive|none`：默认 `all` 以保持兼容。

不把已有无空白 JSON 序列化重新命名为 compact；“紧凑”通过减少节点和重复 locator 实现。

## 设计四：CLI 与协议一致性

- `wait-for` 同时接受位置参数和 `--condition`；两者同时提供且不一致时返回 `INVALID_ARGUMENT`。
- 根命令和子命令的解析错误统一写入 `JsonResponse`，stdout 保持单个 JSON 文档，退出码为 2。
- `list-windows --process-name` 比较时规范化可选 `.exe` 后缀。
- `observe.data.window` 复用 `list-windows` 的窗口身份读取逻辑，填充 title 与 class name。
- README 和 SKILL 的示例由 CLI 合约测试覆盖，避免再次漂移。

## 设计五：UIA 可见性诚实表达

保留 provider 原始 `is_offscreen`，不声称它等价于截图中的视觉可见性。

当多个顶层 Custom 节点同时满足以下条件时，返回 `UIA_VISIBILITY_AMBIGUOUS` warning：

- `is_offscreen=false`
- 矩形高度和宽度均大于零
- 与目标窗口大面积重叠
- 节点矩形彼此大面积重叠

warning 说明 provider 同时暴露了多个重叠表面，Agent 应通过截图或状态属性确认实际可见层。该规则不得隐藏或修改原节点。

## 设计六：Guard 生命周期

guard TTL 从 30 秒调整为 120 秒。安全性仍由每次操作时的实时验证保证，而不是依赖较短 TTL。

guard 保持一次 observation 多次只读验证的现有行为；本轮不引入续期或服务端 session。错误信息从硬编码秒数改为使用实际 TTL。

## 测试策略

所有行为变更遵循 RED-GREEN-REFACTOR。

### 单元测试

- 目标缺失、目标非前台、命中其他进程和 owned popup 场景的输入安全测试。
- 有无 `.exe` 后缀的进程名规范化测试。
- wait-for 两种语法及冲突语法测试。
- guard 120 秒边界与动态错误信息测试。
- locator segment 统一匹配、空 name、class + ordinal 和详细失败信息测试。
- visibility ambiguous warning 测试。
- TreeNode 新字段和 locator 输出策略序列化测试。

### Windows 集成测试

- 对当前可用的真实 UIA 窗口执行 locator round-trip。
- 验证前台正确时物理输入可以执行，前台错误时不会调用输入 backend。
- 验证 `get-tree --runtime-id` 与 `tree_path` 指向同一节点。

### Cafe Launcher 实测

使用 `E:\Repos\Cafe.Launcher.Avalonia` 的现有可执行文件验证：

1. `list-windows` 有无 `.exe` 后缀均可找到窗口。
2. `observe` 返回真实 title/class。
3. “设置”按钮 locator 可以直接 `invoke`。
4. 后台 click/key 被拒绝且界面不变化。
5. 激活后物理点击可以打开设置。
6. `find` 可以定位“设置”“取消”“保存”。
7. `get-tree --runtime-id` 和 `tree_path` 可局部展开设置页。
8. `--interactive-only` 明显减少节点数和 JSON 字符数。

## 完成标准

- 所有新增回归测试先失败后通过。
- 全部测试通过，Release publish 成功。
- README、SKILL、DESIGN 与 `--help` 一致。
- Cafe Launcher 八项实测通过。
- 不覆盖或丢失本轮开始前已有的 tracked/untracked 修改。
