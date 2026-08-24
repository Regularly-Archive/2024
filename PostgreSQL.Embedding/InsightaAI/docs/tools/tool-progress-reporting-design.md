# 通用工具进度报告设计

## 背景

当前工具调用对用户而言只有 `AgentToolStartEvent` 与 `AgentToolEndEvent` 两个可见边界。对于 bash 构建、下载、长时间 MCP 调用，或 `delegate` 启动的 Subagent，这会造成较长的无反馈等待；而 Subagent 的最终 `ToolResult` 往往尤其晚到。

本设计增加**工具执行期的旁路进度流**。它是所有工具的统一基础设施，不是 Subagent 专属功能；bash、MCP、Web 抓取、批处理和未来工具均可选择接入。

## 目标与非目标

### 目标

- 允许工具在执行期间报告原始状态或输出增量。
- 由 Runtime 统一接收、脱敏并有界分发进度事件。
- 由各前端的 `ToolProgressWindow` 统一处理固定窗口、折叠、节流和渲染；CLI、Desktop、Web 可采用不同策略。
- 保持 LLM 的工具协议不变：父 Agent 仍只消费最终、完整的 `ToolResult`。
- 保持既有工具兼容：未调用进度接口的工具仍然是 Start → End。
- 所有进入用户界面的进度文本遵循与工具结果一致的脱敏边界。

### 非目标

- 不将进度文本写入父 Agent 上下文、消息存储或最终 ToolResult Artifact。
- 不将工具过程输出作为增量 ToolResult 回传给 LLM。
- 不在本功能中实现 detached/background process 的完整生命周期管理。
- 不要求每个工具报告百分比；许多工具只能可靠报告阶段和活动状态。

## 核心原则

1. **工具报告事实，UI 决定呈现。** 工具不知道“保留六行”“每 200ms 刷新”等展示策略。
2. **每个 Tool Call 一个 UI 窗口。** `ToolProgressWindow` 以 `ToolCallId` 关联，支持并行工具而不串台。
3. **旁路而非上下文。** Progress 面向用户与观察者，不面向父 LLM 推理。
4. **最终结果仍是权威。** `AgentToolEndEvent` 与最终 `ToolResult` 的语义不变。
5. **默认安全且有界。** 文本先脱敏；队列、窗口和刷新频率都有上限。

## 数据流

```text
ToolExecutionContext.Progress.ReportAsync(raw update)
                         |
                         v
       Runtime progress dispatcher
       - correlation / redaction / bounded delivery
                         |
                         v
         AgentToolProgressEvent (raw update)
                         |
           +-------------+-------------+
           |                           |
           v                           v
 CLI ToolProgressWindow      Web/Desktop presentation state

AgentToolEndEvent → final ToolResult → ToolResultProcessor → LLM / storage / artifacts
```

## 原始进度契约

工具上下文提供可选的报告接口和默认空实现：

```csharp
public interface IToolProgressReporter
{
    ValueTask ReportAsync(
        ToolProgressUpdate update,
        CancellationToken cancellationToken = default);
}

public sealed record ToolProgressUpdate
{
    public required ToolProgressKind Kind { get; init; }
    public string? Message { get; init; }
    public string? Text { get; init; }
    public ToolOutputStream? Stream { get; init; }
}

public enum ToolProgressKind { Status, Output, Heartbeat }
public enum ToolOutputStream { Stdout, Stderr }

public sealed class ToolExecutionContext
{
    // Existing execution metadata omitted.
    public IToolProgressReporter Progress { get; init; } = NullToolProgressReporter.Instance;
}
```

工具只报告原始增量：

```csharp
await context.Progress.ReportAsync(new ToolProgressUpdate
{
    Kind = ToolProgressKind.Output,
    Stream = ToolOutputStream.Stdout,
    Text = line
});
```

报告接口是 best-effort：前端断开、消费者拥塞或进度渲染故障不能使工具调用失败。工具取消仍由原有 `CancellationToken` 处理。

## Runtime 分发与 UI ToolProgressWindow

Runtime 不拥有窗口或 UI 状态。它为每次工具执行创建 `IToolProgressReporter`，将工具报告的原始 update 关联到当前 `ToolCallId`，完成脱敏并投递到有界 progress event channel。该 channel 的背压策略用于保护执行资源，而不是决定用户看见多少行。

`ToolProgressWindow` 是前端组件或前端状态对象。CLI 用它维护一个按 `ToolCallId` 分组的固定活动区域；Web/Desktop 可以采用其他保留长度、刷新频率或展开方式。它接收 `AgentToolProgressEvent` 后自行做行规范化、折叠、节流和重绘。

CLI 的初始策略建议：

- 最多保留 6 行、2 KB 的已脱敏文本；超出时从最旧内容开始折叠。
- 最多每 200ms 重绘一次；Tool End 到达时完成最后一次重绘并销毁活动窗口。
- 文本按 LF 规范化为行；无换行的 token/delta 先合并，再按刷新周期显示。
- 显示 `CollapsedLineCount`，让用户知道早期过程被折叠，而非误以为没有发生。
- 在没有输出的长任务中，显示从 Start/Heartbeat 推导的运行时长和最后状态。

窗口只保留瞬态展示数据。最终 `ToolResult` 仍按现有 `ToolResultProcessor` 生命周期处理。

## Agent 事件与汇流

新增 `AgentToolProgressEvent`，携带由 Runtime 补齐的 Tool Call 身份和已脱敏的原始 update：

```csharp
public sealed record AgentToolProgressEvent : AgentEvent
{
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required ToolProgressUpdate Progress { get; init; }
}
```

单个工具调用的可见事件顺序为：

```text
AgentToolStartEvent
  → 0..N AgentToolProgressEvent
  → AgentToolEndEvent
```

### 串行执行的消费边界

`ParallelToolExecution = false` 不只表示工具任务彼此不并发。`ToolCallExecutor` 为每个 Tool Call 建立独立的有界事件 channel：前一工具的 `ToolEnd` 被 `RunStreamAsync()` 产出后，执行器会停在该 `yield`；只有消费方处理该事件并请求下一事件，才会启动下一工具。CLI 的消费方在请求下一事件前会完成 `EventRenderer.HandleEventAsync()`，因此前一工具的最终块已收束，下一工具的 `ToolPermissionHook` / `ask_user` 才可能写入终端。

该边界是通用事件消费语义，不依赖 CLI 或 Spectre.Console；它不承诺某个前端的物理绘制成功，但消除了“上一工具的 ToolEnd 仍排队、下一工具已进入交互”的执行时序。

`RunStreamAsync()` 需要汇流 Agent Loop 的常规事件与工具执行期间产生的 progress events，保证工具尚未完成时 UI 已能收到更新。Progress event 不触发 Agent Hook，不写入消息历史，也不传给 LLM。

## 工具接入示例

### Bash

- 进程启动时报告 `Status`。
- 异步读取 stdout/stderr，并报告 `Output`；窗口负责截断与刷新。
- CLI 窗口每 200ms 重绘并显示运行时长；后续可由工具额外报告 `Heartbeat`。取消时终止整个进程树。
- 最终 stdout/stderr 继续作为原始 ToolResult，由既有结果处理器投影、脱敏和持久化。

### Subagent delegate

Subagent 不是特殊展示机制。CLI adapter 消费子 Agent 的 `RunStreamAsync()`，把子 Agent 的文本增量、Round Start 和子工具状态翻译为父 `delegate` 工具的原始 `ToolProgressUpdate`：

```text
child text delta       → Output
child round started    → Status
child tool started     → Status (tool name only)
```

adapter 不持有独立环形缓冲、不做节流，也不决定保留行数；所有事件作为父 Tool Call 的 progress event 交给前端 `ToolProgressWindow`。默认不转发子工具参数或子工具结果，避免噪音及敏感数据扩散。

### MCP 与其他工具

支持 streaming/progress notification 的 MCP transport 可映射到 `Output` 或 `Status`。没有过程通知的工具无需改动。Web 抓取、批处理等工具也可在合理阶段报告状态。

## 前端呈现

CLI 的 `ToolProgressWindow` 将运行中窗口作为临时区域，而不是不断追加聊天历史：

```text
○ delegate(reviewer) · running 18s

  … 42 earlier updates collapsed
  ⎿ Reading src/Agent.cs
    Checking session storage
    Drafting recommendation...
```

完成后临时窗口收束为普通的 Tool End 块和最终结果。Web/Desktop 基于同一组 raw progress events 提供展开日志或固定窗口；是否展示更长历史是前端策略。

并行工具各自拥有独立窗口。终端渲染需要支持按 `ToolCallId` 更新活动区域，不能将 progress 当成普通聊天行追加。Spectre.Console 的 `Live` 与 `SelectionPrompt` 等交互组件不能并行使用；CLI 启用交互权限时应采用串行工具执行，避免一个仍在刷新的工具窗口与下一工具的确认界面争用终端。

## 安全、资源与可观测性

- Progress 文本必须在进入窗口前经过与 Tool Result 一致的 `ISecretRedactor`；不得绕过工具结果脱敏边界。
- Runtime progress event channel 采用有界容量。拥塞时可合并或丢弃低优先级 Output update，但不能无限缓冲；UI 的折叠行数是独立的呈现策略。
- 进度正文不作为 Prometheus 标签，也不默认进入 Telemetry；可记录低基数的工具运行时长、是否报告进度、折叠次数等指标。
- 工具调用取消时关闭对应窗口；完成、失败或取消均保证最终 flush 后清理。

## Background Process 边界

前台长命令（例如 `dotnet test`）适合此模型。shell 中的 `command &` 代表 detached/background process：shell 可能已结束，子进程输出、取消和生命周期不再可靠地绑定于该 Tool Call。

后台任务应由后续独立的 Job / Process Handle 模型解决，例如 `start_process`、`get_process_status`、`read_process_output`、`stop_process`。不能将它伪装成普通 bash progress。

## 实施状态

1. [x] 定义 `IToolProgressReporter`、`ToolProgressUpdate`、默认空实现与 `AgentToolProgressEvent`。
2. [x] 改造工具事件汇流，使执行中的 progress event 可从 `RunStreamAsync()` 产出；使用有界 channel，并在分发前脱敏。
3. [x] 在 CLI renderer 实现 `ToolProgressWindow`：按 `ToolCallId` 分组，最多保留 6 行 / 2 KB，并由交互式终端的 Live 区域每 200ms 刷新。非交互式输出保持原有 Tool End 渲染。
4. [x] 接入 bash 与 `delegate`；Subagent adapter 只做事件翻译。
5. [x] 串行工具执行以 `ToolEnd` 的事件消费为边界；下一 Tool Call 只在前一 ToolEnd 被消费端推进后启动。
6. [ ] 评估 MCP progress notification 与独立 background Job 模型；为其他长运行工具补阶段性状态或 Heartbeat。

## 验收标准

- 未接入 progress 的工具行为与当前版本完全一致。
- 长 bash 与 Subagent 执行时，用户在 Tool End 前能看到持续更新的有限窗口。
- 大量 stdout 或子 Agent 文本不会无限增长终端输出、内存或 Agent 事件队列。
- progress 文本脱敏后才可见，且不会进入父 Agent 消息历史或 LLM 输入。
- 并行工具窗口按 `ToolCallId` 隔离；取消、失败和正常完成均不会遗留活动窗口。
