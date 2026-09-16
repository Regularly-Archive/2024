# 交接：`insighta run` 非交互协议

## 2026-09-15：Turn 结束时空引用修复

- 实机输出 `run_protocol.txt` 中，会话 `1d970555a290` 完成两轮调用与最终回答后，在 `agentRoundEnd` 后直接输出 `run.failed`；对应日志已记录 `Turn ended — status=Completed`，最终助手消息也已持久化，故失败发生在 TurnEnd 的消费边界。
- `AgentEventTelemetryHook.OnAgentTurnEndedAsync` 未处理 `StartActivity()` 返回 null。`run` 当前没有初始化 TracerProvider，但配置开启遥测时仍会注册 Hook；无监听器或未采样时可能同步抛出空引用。现已使用空值安全访问，仍执行 round context 清理。
- `Agent.SafeInvokeHookAsync` 原实现先调用 `hookAction()` 再注册 continuation，遗漏同步异常。现将调用和 await 一起放进 try/catch，同步或异步 Hook 故障仅记录日志及堆栈，不阻断 Agent 结束事件和后续 Hook；观察者取消保持不影响主 Turn。
- 回归覆盖：无监听器、未采样、正常采样；同步/异步 TurnEnd Hook 故障；`whereami` 两轮 run 最终产生 `agentTurnEnd` 与 `run.completed`。全部使用离线模型，未重新调用真实供应商。
- 以下 MVP 状态及未完成事项为此前交接记录；本修复未引入 run 的遥测 Provider 生命周期，也未更新全局工具安装。

- 日期：2026-09-15
- 分支：`feat/non-interactive-run-protocol`
- 来源分支：`feature/llm-abstraction-layer`
- 状态：MVP 已实现并通过定向测试；未提交、未合并

## 1. 目标与边界

这项工作对应 `docs/TODO.md` #20 的「`insighta run` 先行」部分，目标是给 Agent runtime 增加稳定的非交互进程边界：

```text
caller / Node UI / CI
      ↓ spawn + stdin / stdout
insighta run
      ↓ JSONL envelope + AgentEvent
AgentFactory / Agent.RunStreamAsync()
```

已实现的是 one-shot 非交互入口。持久 bridge、双向权限确认、cancel command、外部 CLI Agent adapter 不在本次范围内。

## 2. 当前实现

### 命令

```bash
dotnet run --project src/InsightaAI.Agent.Cli -- run "task"

# stdin
"task" | dotnet run --project src/InsightaAI.Agent.Cli -- run

# resume
dotnet run --project src/InsightaAI.Agent.Cli -- run "next" --session <id>

# profile
dotnet run --project src/InsightaAI.Agent.Cli -- run "task" --profile <subagent-id>
dotnet run --project src/InsightaAI.Agent.Cli -- run "task" --profile runner --allowed-tools read_file,grep
```

关键文件：

| 文件 | 说明 |
| --- | --- |
| `src/InsightaAI.Agent.Cli/Commands/RunCommand.cs` | CLI 命令、参数、stdin 读取 |
| `src/InsightaAI.Agent.Cli/Apps/Run/RunApplication.cs` | 非交互 run 的宿主组合逻辑 |
| `src/InsightaAI.Agent.Cli/Apps/Run/RunJsonlWriter.cs` | stdout JSONL envelope |
| `src/InsightaAI.Agent.Cli/Apps/Run/RunOptions.cs` | `RunRequest` / `RunExecutionResult` / exit codes |
| `src/InsightaAI.Agent/Models/AgentEvents.cs` | 给现有 `AgentEvent` 补 JSON polymorphic contract |
| `src/InsightaAI.LLM/Models/StreamEvents.cs` | 给现有 `StreamEvent` 补 JSON polymorphic contract |
| `src/InsightaAI.LLM/Models/ContentBlocks.cs` | 给现有 `ContentBlock` 补 JSON polymorphic contract |
| `tests/InsightaAI.Agent.Cli.Tests/Run/RunProtocolTests.cs` | 协议与 run 集成测试 |

注意：`RunApplication` 等文件的当前目录是 `Apps/Run/`，但 C# namespace 仍是 `InsightaAI.Agent.Cli.Run`。`Program.cs` 中的 using 和 DI 也是按这个 namespace 写的。

## 3. 协议设计

stdout 每行一个 JSON 对象，stderr 不作为协议通道。不要解析 Spectre CLI 的人类可读输出。

```jsonc
// 进程生命周期 envelope
{ "protocol": 1, "sequence": 1, "type": "run.started", "sessionId": "...", "model": "...", "profileId": null }

// 直接复用现有 AgentEvent，不重新映射事件名
{
  "protocol": 1,
  "sequence": 2,
  "type": "agent.event",
  "event": {
    "$type": "agentLlmStream",
    "type": "llmStream",
    "agentId": "...",
    "timestamp": "...",
    "streamEvent": {
      "$type": "textDelta",
      "type": "textDelta",
      "delta": "..."
    }
  }
}

// 进程结束 envelope；result 直接序列化现有 AgentResult
{ "protocol": 1, "sequence": 9, "type": "run.completed", "sessionId": "...", "exitCode": 0, "result": { "...": "..." } }
```

设计约束：

1. `agent.event.event` 就是现有 `AgentEvent`，不新造 `tool.start`、`round.start`、`llm.text.delta` 这类第二套事件名。
2. `run.started` / `run.completed` / `run.failed` 只是进程 envelope，不是 Agent 事件。
3. `AgentEvent`、`StreamEvent`、`ContentBlock` 使用 `$type` discriminator；枚举输出 camelCase 字符串。
4. exit code：`0` 成功，`1` 失败，`130` 取消；CLI 缺少 task/stdin 时返回 `2`。

## 4. 安全与权限

非交互模式没有交互式权限确认。

### 无 profile 默认权限

默认收紧为保守只读：

- 排除 `ask_user`
- 排除 `delegate`
- 排除 `bash`
- 排除 `write_file` / `edit_file`
- 排除 `web_fetch` / `web_search`
- 排除 Skill、MCP、Memory 工具组

没有提供 `--yes` 或全量工具模式。

### `--profile` 权限

`--profile` 复用 `InsightaSubagentDefinition`：

1. 通过 `LocalSubagentDefinitionStore` 读取全局 profile。
2. `ToolRegistry` 只注册 `definition.ToolNames`。
3. `--allowed-tools` 接受一个逗号分隔的工具名列表，只能继续收紧 profile，不能扩权；未指定 `--profile` 或给出 profile 未允许的工具名时，输出 `run.failed` 并失败，不会静默运行空工具集。
4. `Capabilities` 决定 Skill / MCP / Memory 工具组是否排除。
5. `definition.Model` 可覆盖主模型；未指定时使用 `PrimaryModel`。
6. System prompt 追加 runtime constraints：非交互、不可委派、不可使用被排除基础设施。
7. `delegate` 始终排除。

`RunApplication` 的 profile resolver 已做成构造函数注入，测试可以替换；CLI 默认仍使用本地全局 store。

## 5. 会话与持久化

- 新 run 默认创建新 session。
- `--session <id>` 续接既有 main session。
- session 的 model 与 profile 解析出的 model 不一致时报错。
- 消息持久化仍由现有 `Agent.RunStreamAsync()` + `IMessageStorage` 处理。
- `RunApplication` 复用 `AgentFactory`、`SkillRegistry`、`McpRegistry`、`SummaryService`、Memory 与现有 compaction。

## 6. 验证状态

定向测试：

```bash
dotnet test tests/InsightaAI.Agent.Cli.Tests/InsightaAI.Agent.Cli.Tests.csproj --filter RunProtocolTests
```

当前结果：3/3 通过。

覆盖点：

1. `RunJsonlWriter` 直接序列化现有 `AgentEvent`，保留 `$type`，不引入映射事件。
2. `RunApplication` 完成 one-shot turn，输出 `run.started`、`agent.event`、`run.completed`。
3. `--profile` 的工具集合正确收紧，只暴露 `definition.ToolNames` 中的工具。

此前在本分支上还验证过：

- `InsightaAI.Agent.Cli.Tests`: 16/16 通过。
- `InsightaAI.Agent.Tests`: 335/335 通过。
- `InsightaAI.LLM.Tests`: 125/125 通过。

这些是在加入 profile resolver/profile toolset 测试之前跑的；提交前建议再跑一次完整相关套件。

## 7. 当前工作区状态

当前分支上除本功能外，还有一组未提交的移动/整理：

```text
src/InsightaAI.Agent.Cli/Services/ChatApplication.cs       -> deleted
src/InsightaAI.Agent.Cli/Services/IChatApplication.cs      -> deleted
src/InsightaAI.Agent.Cli/Apps/Chat/ChatApplication.cs      -> untracked
src/InsightaAI.Agent.Cli/Apps/Chat/IChatApplication.cs     -> untracked
```

这些类 namespace 目前仍保持原 namespace，因此编译可过。提交时需要确认这是有意纳入本分支的结构调整；如果不是，应单独提交或从本分支拆出。

另外，工作区还有：

```text
.learnings/ERRORS.md
artifacts/evals/report.json
```

这些是本地运行/学习产物，不应随功能提交。

父仓库显示 `SCUI-Admin` 和 `weibo-personality` 有各自状态变化，与本项目当前功能无关。

## 8. 未完成事项

按优先级：

1. **复查 profile 逻辑去重**  
   `RunApplication` 与 `CliInsightaSubagentAdapter` 都有 profile → `AgentConfig` / excluded tools / runtime constraints 的逻辑。应提取共享的 host-level profile builder，避免后续行为漂移。

2. **补 profile 能力测试**  
   当前已测 `ToolNames` 收紧，还需要覆盖：
   - `Capabilities.EnableSkills`
   - `Capabilities.EnableMcp`
   - `Capabilities.EnableMemory`
   - `IncludeProjectInstructions`
   - profile model override
   - deny rule 仍不可绕过

3. **取消语义**  
   目前依赖 `OperationCanceledException` 映射为 exit `130` 和 `run.cancelled`。还需要确认工具执行中的取消、子 Agent 取消、进程 kill tree 和 JSONL tail 行为。

4. **预算/专用 exit code**  
   TODO #20 要求区分预算耗尽。当前只有 `0/1/130`，尚未定义和实现 budget-specific code。

5. **文档与 TODO 同步**  
   TODO #20 的前几项已可勾选：`run` 命令、`--session`、`--profile`、stdout JSONL、基础 exit code、非交互默认收紧。`--pretty` 和 `executionMode` 未做。`AGENTS.md` 还需要在功能稳定后记录这项架构变化。

6. **提交前整理**  
   - 确认 `Apps/Chat` 移动是否属于本提交。  
   - 检查 `.gitignore` 是否应吸收 `artifacts/`、`.learnings/`。  
   - 移除无用 using（如 `InsightaAI.Agent.Hooks` / `Microsoft.Extensions.Logging` 是否确实使用）。  
   - 运行完整 CLI / Agent / LLM 测试。  
   - commit 建议拆分：
     - `feat(cli): add non-interactive run protocol`
     - `chore(cli): organize application hosts under apps directory`

## 9. 后续路线

1. 完成 `insighta run` MVP 收尾并提交。
2. 让 Node/Electron 桌面 MVP spawn `insighta run`，消费 JSONL。
3. 在桌面需要交互审批后，再新增 persistent bridge 模式：

```text
insighta bridge
```

或：

```text
insighta run serve
```

bridge 复用同一份 `AgentEvent` 序列化契约，新增双向控制：

```jsonc
{ "type": "permission.request", "requestId": "...", "toolCallId": "...", "tool": "bash" }
{ "type": "permission.response", "requestId": "...", "choice": "allow" }
{ "type": "cancel", "turnId": "..." }
```

4. 之后再评估 `executionMode: in-process | process` 和 ExternalCliAgentAdapter。不要在 `run` MVP 阶段提前固化外部 Agent 协议。

## 10. 给接手人的判断基线

这份实现的关键取舍是：**stdout 协议只做 transport，不重定义 Agent 事件模型**。后续如果要加字段，优先检查它是否已经是 `AgentEvent` / `AgentResult` / `SubagentDefinition` 的一部分；如果需要新字段，应加在现有模型或明确的 envelope 层，而不是在 `RunJsonlWriter` 里发明第二套语义。
