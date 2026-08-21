# InsightaAI Agent - 待办事项

> 愿景与期待详见 [VISION.md](VISION.md)

## 代码重构

### X. CLI 配置向导本地化与子命令拆分

**现状：**
- `config` 命令已从单入口大循环，拆分为子命令：`config provider`、`config model`、`config language`。
- 新增 `CliStrings` 资源类与 `CliStrings.resx` / `CliStrings.zh-CN.resx`，提示文案统一抽取。
- `ConfigCommand.cs` 新增泛型菜单辅助方法：`PromptMenu<TAction>`、`PromptSelection`、`SelectAdapter`。
- 新增枚举：`ProviderAction`、`ModelAction`、`LanguageAction`、`AdapterAction`，替换硬编码字符串。
- 新增基础本地化键：`CommonBack`、`CommonNone`、`CommonDefault`。

### X.1 ChatCommand 全命令国际化（已完成）

**完成内容：**
- [x] 整理 5 个 CLI 命令的国际化资源清单（`docs/i18n/`）
- [x] `CliStrings.resx` / `CliStrings.zh-CN.resx` 新增 41 个 `Chat*` 资源条目
- [x] `CliStrings.cs` 新增 21 个非格式化 Key 的静态属性
- [x] `ChatCommand.cs` 30 处硬编码字符串替换为 `CliStrings` 引用
- [x] `ChatRenderer.cs` 5 处硬编码字符串替换为 `CliStrings` 引用
- [x] `EventRenderer.cs` 6 处硬编码字符串替换为 `CliStrings` 引用
- [x] Spectre.Console markup 标签整体存入 resx，翻译时保留标签
- [x] `ask_user` 工具 `question` 参数增加 `Markup.Escape` 防护
- [x] 构建通过，0 Error / 0 Warning，202 个单元测试全部通过

### X.2 Chat Slash 命令候选与补全（已完成）

- [x] 由 `ChatApplication` 集中定义 `/model`、`/compact`、`/clear`、`/exit`、`/quit` 的命令元数据。
- [x] 单行 `/` 前缀实时显示候选；精确匹配、多行、普通文本和折叠粘贴时隐藏。
- [x] 候选展示命令和 `ChatSlashCommand*Description` 本地化说明，命令列固定宽度对齐，输入与候选之间保留空行。
- [x] `Tab` 对唯一候选补全；带参数命令自动追加空格，其他场景保留制表符输入。
- [x] 修复候选重绘时旧行多于新行造成的索引越界；修复 `Ctrl+C` 被误作空输入而无法退出 chat。

### 1. 摘要服务统一（优先级：中）

**问题描述：**
`TraditionalCompactStrategy.GenerateSummaryAsync` 和 `SessionMemoryHook.GenerateAnchoredSummaryAsync` 存在重复代码。

**重复内容：**
- LLM 请求构建模式（Model、MaxTokens、Temperature、ToolChoice=None）
- 错误处理逻辑
- `ExtractSummary` 方法（提取 `<summary>` 标签内容）

**当前状态：**
- [x] TraditionalCompactStrategy 已添加 `ExtractSummary` 方法
- [x] 创建公共 `ISummaryService` / `SummaryService`
- [x] SessionMemoryHook 改用 `ISummaryService.UpdateAsync`
- [x] TraditionalCompactStrategy 改用 `ISummaryService.SummarizeAsync`
- [x] 全量和增量摘要统一共享结构模板
- [x] 检查 `FinishReason`，MaxTokens 时执行激进压缩重试
- [x] 摘要失败时不覆盖已有 Session Memory、不提交无效 TraditionalCompact

**最终方案：**
在 `Context/Summary` 下建立独立服务，集中负责请求构建、模型解析、摘要提取、完整性校验、错误处理和重试；调用方只负责提供消息并消费 `SummaryResult`。

### 1.1 会话标题生成（已完成）

- [x] `ISummaryService.GenerateTitleAsync` 根据首条用户消息生成简短标题
- [x] 独立 `session-title.txt` Prompt，同语言输出且禁用工具
- [x] 标题请求与首轮 Agent 请求并行，不阻塞主要生成流程
- [x] 标题生成与保存作为后台 best-effort 任务，Usage 显示后不等待它完成，下一轮 Prompt 立即可用
- [x] 推理模型输出预算从 256 tokens 起步，MaxTokens 时扩容到 512 tokens 重试
- [x] LLM 失败时使用首条用户输入降级，Unicode 安全截断到 30 字符
- [x] JSONL/PostgreSQL 使用 `UpdateSessionTitleAsync` 原子更新标题
- [x] `insighta sessions` 展示 Title 列
- [x] 覆盖全量、增量、MaxTokens、标题规范化和 fallback 的单元测试

---

## 架构设计

### 2. Agent 依赖注入（优先级：高）

**已完成：**
- [x] 新增 `TiktokenTokenEstimator`（基于 Microsoft.ML.Tokenizers）
- [x] Agent 双构造函数支持：旧构造函数（手动注入）+ 新构造函数（IServiceProvider）
- [x] 旧构造函数内部构建私有 ServiceProvider（Singleton 注册）
- [x] HookContext 和 ToolExecutionContext 移除 LlmClient，统一通过 Services 解析
- [x] SessionMemoryHook 改用 `context.Services?.GetService<ILlmClient>()`

**当前约定：**
- [x] Agent 实现 IDisposable，释放旧构造函数创建的 ServiceProvider
- [x] Agent 私有 Provider 只支持 Singleton/Transient，当前不支持 Scoped；见 `agent-service-lifetime.md`
- [ ] 未来若确有 DbContext 等 Scoped 需求，先定义 Host / Chat Session / Turn 作用域边界，再重新设计容器关系，不在现有 Agent Loop 中局部加 `CreateScope`

---

### 2.1 CLI 配置启动阶段与运行阶段分离（优先级：高）

**问题描述：**
语言、Telemetry 和 OTLP endpoint 等配置需要在 `Program.cs` 初始化 CLI 文化、日志和 OpenTelemetry 之前生效，同时 Agent 运行时只能访问当前 Agent 的服务集合。

**进展：**
- [x] 增加 CLI 启动初始化阶段，在创建 Host 前加载并应用 Bootstrap 环境变量
- [x] 区分 Bootstrap 配置与 Agent/Chat Runtime 配置
- [x] 统一配置读取，`CliConfig` 通过 Host DI 复用
- [x] 明确优先级：进程环境变量 > `CliConfig.Envs` > 默认值
- [x] 提取 `CliBootstrap`，覆盖配置应用、进程环境优先级及语言、Telemetry、OTLP endpoint 默认值的启动前解析测试

---

### 3. SessionMemoryCompactStrategy 与 TraditionalCompactStrategy 统一

**已完成：**
- [x] 两个策略统一使用 `compacted-context.txt` 模板
- [x] `CreateBoundaryMarker` 统一为模板渲染

**待优化：**
- [ ] `SplitMessages` 方法可以提取到 `CompactionHelper`
- [x] `EstimateMessagesTokens` 方法已提取为 `TokenEstimatorExtensions` 扩展方法
- [ ] `StripImages` 方法可以提取到 `CompactionHelper`

---

### 4. AgentBuilder 与 AgentFactory 生命周期一致性（已完成）

`AgentBuilder` 现在负责 Agent 级服务组合：构造时注册 `AgentConfig` 和默认 `ToolRegistry`，`WithXxx()` 覆盖显式依赖，`ConfigureServices()` 提供扩展注册点，`Build()` 创建当前 Agent 专属的 ServiceProvider。`Agent` 负责释放该 Provider。

- [x] 构造函数中默认注册 `ToolRegistry`
- [x] 增加 `ConfigureServices(Action<IServiceCollection>)`
- [x] `AgentFactory` 通过 `AgentBuilder` 完成服务组合，不直接创建 Provider
- [x] 保留旧的显式构造函数，兼容现有调用方

---

### 5. 上下文用量显示（优先级：中）

**问题描述：**
用户希望在 Tokens Usage 显示区域增加上下文用量百分比，类似内存占用，通过预估当前消息列表的 token 总数和可用输入预算做对比。

**已完成：**
- [x] 创建 `TokenEstimatorExtensions` 扩展方法（放在 `Extensions` 目录）
- [x] 替换 `TraditionalCompactStrategy` 和 `SessionMemoryCompactStrategy` 中的 `EstimateMessagesTokens` 方法
- [x] `IContextManager` 接口新增 `MaxContextTokens` 属性
- [x] `ContextManager` 实现 `MaxContextTokens` 属性
- [x] `AgentResult` 新增 `EstimatedContextTokens`、`MaxContextTokens` 和 `AvailableInputTokens` 字段
- [x] 压缩阈值与 CLI 占用率统一使用 `AvailableInputTokens`（上下文窗口减输出预留）
- [x] `AgentLoop.cs` 填充新字段
- [x] `EventRenderer` 显示上下文用量百分比（带颜色：绿<70%、黄70-90%、红>90%）

---

## 代码质量

### 5.1 SessionMemoryHook 代码清理

**已完成：**
- [x] 删除死代码 `Truncate` 方法
- [x] 提取公共方法 `ExtractMemoryInBackground`
- [x] 空 catch 块增加日志
- [x] `LoadMetadataAsync` 增加异常处理
- [x] 修正 `CreateOrUpdateMetadata` 拼写错误

**待优化：**
- [ ] `CreateOrUpdateMetadata` 增加字段 fallback 逻辑（当字段为空时自动修正）

---

### 6. Telemetry 防御性与规范化（优先级：中）

**问题描述：**
OpenTelemetry 插桩代码存在防御性不足和指标维度不一致问题。

**子项：**

6.1 `CurrentRoundContext` 安全索引（低风险）
- **位置**: `TelemetryLlmClient.cs`、`TelemetryToolCallHandler.cs`
- **问题**: 直接使用字典索引 `CurrentRoundContext[_agentId]`，若 `AddTelemetry()` 未先调用会抛 `KeyNotFoundException`
- **修复**: 改用 `TryGetValue`，未命中时记录非 parented 的 Activity

**当前状态：** 已完成（2026-08-05）。`ToolCallHandlerTelemetryWrapper` 和 `LlmClientTelemetryProxy` 均已改用 `TryGetValue` 兜底，字典缺失（如测试环境未挂 round hook）时降级为无父 span，不再抛 `KeyNotFoundException`。该问题在修复 `AgentFactoryTests.CreateAsync_Should_Expose_AgentServices_To_Tools` 时暴露并一并修复。

6.2 `LlmRequestDuration` 标签维度不一致
- **位置**: `TelemetryLlmClient.cs` `RecordMetricsAndTags`
- **问题**: `LlmRequestDuration` 直方图 Record 时只传了 duration，未携带 `gen_ai.adapter`、`gen_ai.system`、`gen_ai.request.model` 标签，导致无法按模型维度做细粒度分析
- **建议**: 统一使用与 Counter 相同的 TagList，确保 histogram 维度与 counter 一致

**已完成：**
- [x] 第 5 点已修复：`TelemetryToolCallHandler` catch 块补充 `gen_ai.tool.is_allowed` 标签
- [x] 第 5.1 点已修复：`LlmRequestDuration` metric 名从 `insighta.llm.request.duration` 改为 `gen_ai.client.operation.duration`
- [x] Token counter 命名统一为 `gen_ai.client.tokens.input/output/cache_hit`（保持独立 counter 结构，仅改前缀与 OTel GenAI 对齐）
- [x] `LlmRequestDuration` 使用带模型和供应商维度的标签记录

---

### 6.3 修复已有测试失败（优先级：中）

**问题描述：**
历史上存在 9 个相关测试失败，目前已修复并纳入完整 Agent 测试集。

**失败清单：**
- [x] 4 个 `SessionMemoryCompactStrategy` 相关测试
- [x] 5 个 `SessionMemoryHookLlm` 相关测试

---

## 功能特性

### 6.4 Memory 轻量化索引（核心链路已完成）

**已完成：**
- [x] `SqliteMemoryProvider` 使用 SQLite FTS5 `trigram` 作为运行时记忆主存储，并提供 Markdown 历史迁移工具
- [x] `ActiveMemorySnapshot` 将 `CoreEntries` 与按任务召回的 `ActiveEntries` 分离，并在一个用户 Turn 内复用
- [x] `search_memory` 与自动活跃召回记录粗粒度 `AccessCount` / `LastAccessedAt`；Core 常驻注入不计数
- [x] 排序以 FTS 词法相关性为主，频率和近期访问分别最多提供 10% 与 5% 的乘法修正
- [x] 快照由 `FormatAsString()` 负责 Prompt 文本化，消除名称与摘要的截断重复
- [x] 自动快照要求至少 3 个查询片段、2 个命中且 50% 覆盖；短而宽泛的输入允许只注入 Core
- [x] `MemoryManager` 将候选的入选/淘汰原因、命中数与覆盖率写入本地 Debug 日志，不记录输入或记忆正文

**待处理：**
- [ ] 基于真实会话校准自动注入门槛，避免通用项目历史挤入快照
- [ ] 收紧身份查询的兜底策略：当前 `explicit_type` 会放行全部 `User` 候选；应改为依赖少量 Core 用户画像，普通 User 记忆仍按相关性筛选
- [ ] Memory Update/Delete 授权：`UpdateMemoryAsync` / `DeleteMemoryAsync` 当前只按 `memoryId` 操作，尚未验证调用方 `userId`。暂不修复；后续先定义 Team memory 的项目成员授权模型，再为 Private 校验 `existing.UserId == userId`，并为 Team 校验项目成员权限。不得以调用方可传入的 project 字符串代替授权。

**设计文档：** [memory-index-optimization-design.md](memory-index-optimization-design.md)

---

### 7. Tool Result Lifecycle v2（优先级：高）

**问题描述：**
大型工具结果（如读取大文件、大范围搜索）会迅速消耗上下文窗口，导致压缩频繁触发。

**设计方案：**
将工具结果作为具有独立数据生命周期和上下文生命周期的资源管理，由 Runtime 统一持久化并按 `Full → Preview → Placeholder → Removed` 渐进降级，工具仅定义语义化投影。

**已完成：**
- [x] Phase 1: Core Infrastructure
  - `ToolResultRetentionLevel`、`ProcessedToolResult`、`ToolResultArtifactInfo` 模型
  - `IToolResultProjector` 工具语义投影接口
  - `ToolResultProcessor` + `ToolResultArtifactStore` 统一处理链
  - `Message.ToolResultState` 结构化生命周期状态
  - Message 与 Storage 结构化状态贯通
- [x] Phase 2: Built-in Tool Projectors
  - `FileReadTool` Projector — 200 行预览
  - `GrepTool` Projector — 文件名 + 匹配数量
  - `BashTool` Projector — 头尾各 50 行
  - `WebFetchTool` Projector — 5000 字符预览
  - `WebSearchTool` Projector — 10000 字符预览
- [x] Phase 3: MicroCompactStrategy Refactoring
  - `Full → Preview → Placeholder → Removed` 状态推进
  - ToolCall/ToolResult 成对删除，并保留同一 Assistant 消息中的其他并行 ToolCall
  - 两阶段压缩：先在消息副本试算，只有 token 或消息数量实际下降时才提交
  - `/compact auto` 按优先级逐个尝试，跳过无收益策略
  - 45% / 65% / 80% 阈值，压缩后级联到 L2/L3
  - 压缩阈值与 CLI Usage 统一使用可用输入预算

**待优化：**
- [ ] Phase 4: Testing & Polish
  - [x] 单元测试：原始结果落盘、状态推进、消息配对删除、存储恢复
  - [ ] 集成测试：大文件读取 → 持久化 → 重新读取
  - [ ] CLI 显示截断/持久化状态
  - [ ] 监控指标：截断频率、持久化频率
- [ ] Phase 5: Cleanup & Documentation
  - [ ] Tool Result Artifact 生命周期清理（正常退出 + 异常退出）
  - [ ] 性能基准测试
  - [x] 移除 `InterceptionResult`、`TruncationContext`、`Intercept()` 和冗余 `ToolTruncationStrategy`
  - [x] 更新生命周期 v2、压缩、Memory 与项目入口文档；旧设计标记为历史文档

**当前设计文档：** [tool-result-lifecycle-design-v2.md](tool-result-lifecycle-design-v2.md)
**历史设计文档：** [tool-result-truncation-design.md](tool-result-truncation-design.md)

---

### 8. ESC 打断 LLM 生成（优先级：中）

**问题描述：**
用户在 Agent 生成回复时无法中断，必须等待生成完成。

**已完成：**
- [x] `ChatCommand.ExecuteAgentAsync` 创建 `CancellationTokenSource`，传入 `RunStreamAsync`
- [x] 后台 `Task` 监听 ESC 键，触发 `cts.Cancel()`
- [x] 循环体内显式 `ThrowIfCancellationRequested()` 补充 `WithCancellation` 时机不足
- [x] `catch (OperationCanceledException)` 调用 `EventRenderer.ShowInterruptedAsync()` 显示中断提示
- [x] 中断后不保存未完成的助手消息，避免 LLM 下轮重复生成
- [x] `PromptUser()` 前清空输入缓冲区，防止 ESC 残留泄漏到 prompt
- [x] `finally` 块确保 ESC 监听任务退出

**待优化：**
- [x] Spectre.Console `Status` spinner 与 `Prompt` 冲突：`StopThinkingAsync` 增加 150ms 等待渲染循环退出；`ThinkingEndEvent` 和 `AgentRoundEndEvent` 确保 spinner 在工具权限提示前停止
- [ ] Spectre.Console `Status` spinner 运行时 `Console.KeyAvailable` 可能不稳定，考虑替换为不接管终端的实现
- [ ] 取消流程单元测试

---

### 9. -c/--continue 恢复最近会话（优先级：中）

**问题描述：**
用户希望快速恢复当前工作目录的最近一次会话，无需手动查找 session ID。

**已完成：**
- [x] `SessionRecord` 新增 `WorkDir` 字段，记录会话所属工作目录
- [x] `IMessageStorage.CreateSessionAsync` 新增 `workDir` 参数
- [x] `JsonlMessageStorage` 和 `PostgresMessageStorage` 实现 `GetLastSessionForWorkDirAsync`
- [x] `ChatCommand` 新增 `-c`/`--continue` 选项，自动恢复当前目录最近会话
- [x] `Program.cs` 支持 `insighta -c` 自动补全为 `insighta chat -c`
- [x] `ChatSession.CreateAsync` 传递 `workDir`

---

### 10. OpenAI Responses API reasoning 事件支持（优先级：高）

**问题描述：**
`OpenAIResponseAdapter` 缺少 `response.reasoning_text.delta` 和 `response.reasoning_text.done` 事件处理，导致 thinking spinner 无法正常停止。

**已完成：**
- [x] `ParseStreamEvent` 新增 `response.reasoning_text.delta` → `ThinkingDeltaEvent`
- [x] `ParseStreamEvent` 新增 `response.reasoning_text.done` → `ThinkingEndEvent`
- [x] `EventRenderer.HandleStreamEventAsync` 新增 `ThinkingEndEvent` 处理，调用 `StopThinkingAsync()`

---

### 11. 工具调用 ID 一致性修复（优先级：高）

**问题描述：**
`HandleOutputItemDone` 使用 `item.id`（output item ID）作为工具调用 ID，而 `HandleOutputItemAdded` 使用 `item.call_id`，导致 `LlmStream.BuildResponseFromEvents` 去重失败，同一工具调用被添加两次（第二次参数为空）。

**已完成：**
- [x] `HandleOutputItemDone` 改为优先使用 `call_id`，与 `HandleOutputItemAdded` 一致
- [x] 移除 `LlmStream.BuildResponseFromEvents` 中的 `seenToolCallIds` 去重逻辑，去重职责统一交给 Agent 层

---

### 12. MCP Telemetry Tag 命名清理（优先级：低）

**问题描述：**
`ToolCallHandlerTelemetryWrapper` 消费 `ToolResult.Metadata` 后，MCP span 上出现语义重叠的 tag：

| 来源 | tag | 问题 |
|------|-----|------|
| MCP SDK | `mcp.client.transport` | 与我们的 `mcp.server.transport` 重复 |
| MCP SDK | `mcp.client.description` | 与我们的 `mcp.server.description` 重复 |
| 我们的 Registry | `mcp.server.description` | 值来自本地 `McpServerConfig.Description`，非 server 自报，放 `mcp.server.*` 下语义有误导 |

**建议方案：**
- `McpRegistry` 中 `mcp.server.description` → `mcp.config.description`
- `McpRegistry` 中移除 `mcp.server.transport`（MCP SDK 已有）
- `SimpleMcpConnectionPool` 保持来自 server `initialize` 握手的 `mcp.server.name`/`mcp.server.version`，并确认 description 是否应归入 server 身份

**当前状态：** 尚未完成。当前连接池仍输出 `mcp.server.description`，需要完成 tag 分层并补充测试。

**最终 tag 分层：**
- `mcp.server.*` — 远端 server 身份（连接池层，来自握手）
- `mcp.config.*` — 本地配置（Registry 层）
- `mcp.client.*` / `mcp.method.*` — SDK 运行时（保持不变）

---

### 13. Agent 事件与 Hook 生命周期整理（优先级：高）

**生命周期定义：**
- `Session`：持久化聊天会话，可包含多次用户交互
- `Turn`：一次 `RunStreamAsync`，完整处理一条用户输入
- `Round`：Turn 内的一次 LLM 推理及其工具调用
- 当前不引入 `TurnId`；单条事件流天然对应一个 Turn，继续使用 `SessionId` 关联持久会话

**已完成：**
- [x] `AgentSessionStart/End` 统一重命名为 `AgentTurnStart/End`
- [x] `IAgentEventHook` 生命周期方法同步调整为 `OnAgentTurnStartedAsync` / `OnAgentTurnEndedAsync`
- [x] Telemetry span 与标签从 session 生命周期语义调整为 turn
- [x] 串行和并行工具执行均改为在全部 `ToolEnd` 事件之后发送 `AgentRoundEndEvent`
- [x] Hook 触发移到 `yield return` 之前，消费者提前退出不丢失 Hook 工作
- [x] 四个 Trigger 方法统一为 fire-and-forget 并行调度（`SafeInvokeHookAsync`）
- [x] 移除 `OnHookError` 静态事件，Hook 异常仅写日志
- [x] Agent 注入 `ILogger<Agent>`，`Debug.WriteLine` 替换为结构化日志
- [x] `Agent.LogEvent()` 记录 TurnStart/End、RoundStart/End、ToolStart/End、Error、ContextCompacted
- [x] CLI 层通过 Serilog 配置文件日志（`~/.insighta/logs/{date}.log`）
- [x] `list_skills` 工具供 Agent 运行时查询可用技能

**待处理：**
- [x] `AgentEventHookContext.Event` 改为不可变事件快照，消除共享可变 Context 在 fire-and-forget Hook 中的竞态
- [x] 新增 `IUserPromptEventHook`：用户消息接收后的 fire-and-forget 观察点，记录用户输入日志；合规拦截留待独立的可等待接口
- [x] 接入 `AgentErrorEvent` 异常生命周期：LLM `ErrorEvent` 唯一映射为 Agent 级错误事件，不再透传；触发错误 Hook 与日志后以 `Failed` TurnEnd 收束，且不写入空助手消息
- [ ] 明确取消/中止场景（`DoneReason.Aborted`、`OperationCanceledException`）的 Agent 事件契约
- [ ] 真正的 Chat Session 创建、归档、删除事件由会话存储或应用层负责，不放入 `AgentLoop`

---

### WebFetchTool 内容提取优化

**已完成：**
- [x] 使用 AngleSharp 选择主内容区域（`article` / `main` / `[role=main]`），并提取精选页面元数据。
- [x] 使用 ReverseMarkdown 转换 HTML 为 Markdown，转换前将相对链接和图片地址规范化为绝对 URL。
- [x] `format=text` 按块级元素保留段落、标题和列表换行。
- [x] 过滤导航、页脚、脚本及常见交互控件，减少网页 UI 噪音。

**待处理：**
- [ ] 为自定义 Web Components（例如 `<bread-crumbs>`）提供 Markdown 降级或移除策略。
- [ ] 对文章尾部反馈、相关推荐和其他资源区增加启发式过滤，且避免误删正文。

---

### 14. Agent 安全增强：DenyList 与敏感文件保护（优先级：中）

**问题描述：**
Agent 可执行 Shell 命令、读写文件、搜索内容，当前缺少用户可配置的安全防线。两个目标：
1. `AgentConfig` 添加 `DenyList`，拒绝 `rm -rf`、`Remove-Item` 等危险操作，规则可自定义。
2. 敏感文件保护：`.env`、配置文件、`.ssh` 私钥等含机密内容不应被 Agent 直接读取。

**威胁模型：**
`read_file` / `grep` 有显式路径参数（可精确拦截），但 `bash` 是"任意代码执行"逃逸通道，命令内容检测无法完全约束（可无限变形）。目标界定为"防止敏感内容进入 LLM 上下文 + 防止结构化工具直接触碰敏感路径"，而非阻止 bash 执行本身。

**配置链路（职责分离）：**
```
CliConfig (config.json) ←最终配置链路─ AgentFactory 映射 → AgentConfig ←数据传播链路─ SecurityPolicyHook (IToolHook)
```

**拦截语义：强制拒绝，不提供交互放行。** 交互确认是 `ToolPermissionHook` 的职责；DenyList 是预先声明，命中即拒绝并返回明确错误。放行 = 用户改 `CliConfig` 删规则。

**纵深防御分层（推荐第一版范围 L1+L3）：**
- L1 结构化工具层：`read_file`/`grep`/`write_file` 按路径精确拦截（可靠）
- L2 bash 命令内容启发式：只拦直白写法（拦不住变形）
- L3 bash 输出打码：`OnAfterExecutionAsync` 扫 stdout，命中敏感模式替换为 `[REDACTED]` 再交给 LLM（兜底防泄漏进上下文）
- L4 最小权限执行：`IShellExecutor` 换 Docker/沙箱（根治，独立大工程，单独立项）

**Phase 1：DenyList（已完成）**
- [x] `AgentConfig` 加 `DenyRules`（`DenyRule(Pattern, DenyMatchMode)`，模式 exact/glob/regex）
- [x] `CliConfig.SecurityConfig.DenyList` + JSON 映射（`security.deny_list`）
- [x] `SecurityPolicyHook`（`IToolHook`，由 CLI `AgentFactory` 在交互权限 Hook 前注册）实现三种模式匹配
- [x] 规则命中不可被 Allow always 绕过；Phase 1 仅匹配 bash 的 `command`
- [x] 单元测试 + 集成测试
- [ ] 内置默认高危规则清单（暂不新增，保留待决策）
- [ ] 补充 `BashTool.IsDangerousCommand()` 的分层语义注释与定向测试：它是不可配置的工具级底线，不是默认 DenyList

**Phase 2：敏感文件保护（约 0.5~1 天）**
- [ ] `SecurityConfig.SensitivePaths`（glob 模式：`**/.env`、`**/.ssh/**`、`~/.insighta/config.json`）
- [ ] L1：按路径拦截 `read_file`/`grep`/`write_file`（解析 arguments 中 file_path/path 参数）
- [x] L3：`ToolResultProcessor` 在 artifact、投影和 ToolEnd 预览前统一脱敏，不依赖无法替换结果的 `OnAfterExecutionAsync`；`read_file` 行号包装及 Windows `CRLF` 在匹配前归一化、输出后恢复原换行风格
- [x] JSON 格式保真：`read_file` 的行号包装使完整输出不是 JSON，但 `SensitiveLine` 匹配带引号 JSON 键时保留字符串 value 的双引号和尾部逗号（`"key": "[REDACTED]"`），而不是输出未加引号的占位符。
- [x] 脱敏占位符写入保护：`write_file.content`、`edit_file.old_string` 与 `edit_file.new_string` 含 `[REDACTED]` 时明确拒绝，避免模型把脱敏展示文本落盘。
- [ ] 扩展格式化脱敏：YAML 完整语法（当前为键值风格）、TOML 与专用配置格式；继续保留通用文本兜底
- [ ] 链式命令拆分检测：暂缓。简单按 `;`、`&&`、`|` 切分无法正确处理 PowerShell/Bash 引号、管道和子表达式，不能作为安全边界。

**待决策：**
- [ ] DenyRule 匹配对象：整条命令规范化匹配（Phase 1）还是拆解命令 token（Phase 2）
- [ ] 内置默认规则清单范围
- [ ] L2 启发式检测阈值
- [ ] L4 沙箱执行器是否立项（Docker 还是受限用户）

**设计文档：** [agent-security-design.md](agent-security-design.md)

---

### 15. CLI 并行工具调用的结果归属渲染（已完成）

**问题描述：**
开启 `ParallelToolExecution` 时，事件可能按“多个 `AgentToolStartEvent` 连续到达，再按完成顺序到达多个 `AgentToolEndEvent`”的顺序输出。此前 `EventRenderer` 在 Start 时立即输出调用、End 时只输出缩进结果，导致多个结果在视觉上都会像是最后一个工具的子节点。

**实施：**
- [x] `ToolStart` 时按 `ToolCallId` 缓存调用信息，不立即写入终端历史。
- [x] `ToolEnd` 时按 ID 取回调用，将 `○ tool(args)` 与 `  ⎿ result` 作为一个完整块输出。
- [x] 不采用 ANSI 光标回写已输出行：自动换行、终端滚动、权限/AskUser 交互插入及不同终端的光标行为会导致定位不可靠。
- [x] 工具仍在 Start 后并行执行；终端仅按完成顺序追加完整块，因此视觉上像同步输出，但不改变执行并发度。

**后续：**
- [x] 单元测试：串行延迟输出、并行完成顺序反转、工具错误与缺失 Start 的降级显示。
- [ ] 补充权限/AskUser 交互插入场景的渲染测试（该交互由 `ChatApplication` / `ToolPermissionHook` 输出，不经过 `EventRenderer`）。

---

### 16. Token 用量归属与审计（优先级：中）

**目标：** 精确统计每个用户、会话和模型的 LLM token 消耗，同时避免向 Prometheus 写入高基数的 user/session label。

- [ ] 在 `Agent/Usage/` 定义 `UsageRecord`、`UsageQuery`、`UsageSummary` 与 `IUsageStore`
- [ ] 新增 `AgentContext.UserId`，将用户与会话归属带入所有 LLM Round
- [ ] 提供 `SqliteUsageStore`；CLI 注入默认 `~/.insighta/usage/usage.db`
- [ ] Agent Loop 在收到每轮最终 `TokenUsage` 后同步持久化，不依赖 Telemetry
- [ ] 补齐 Session / User / Model 聚合查询与可靠性测试
- [ ] 新增 `insighta usage` 查询命令
- [ ] 单独决策会话删除与 usage 审计记录的保留/级联删除策略

**设计文档：** [usage-accounting-design.md](usage-accounting-design.md)

---

### 17. 可观测性 Dashboard 拆分与复核（已完成）

**背景：** 原 Overview Dashboard 承载全部指标，Agent/LLM 细节混杂。昨日日报目标为拆分出独立的 Agent 与 LLM Dashboard，并复核查询语义。

**已完成：**
- [x] 拆出 `insighta-agent.json`（Round 延迟 p50/p95、平均每 Turn Round 数）与 `insighta-llm.json`（按模型的请求量、p50/p95 延迟、输入/输出 Token、cached/uncached、Token rate、input:output ratio）
- [x] Overview 保持健康摘要（turns / requests / cache hit ratio 三个 stat）
- [x] Insighta 独立复核全部 4 个 Dashboard，用真实 Prometheus 数据验证 PromQL（见 `docs/observability-dashboard-review.md`）
- [x] Anthropic 归一化：`InputTokens = input_tokens + cache_creation + cache_read`（`AnthropicAdapter.cs`），与 OpenAI/Gemini 口径统一，修复 Anthropic 下 cache hit ratio 失真
- [x] rounds per turn 去掉 `clamp_min` 除零假值（分母为 0 不绘制）
- [x] input:output ratio 按 `gen_ai_request_model` 分组
- [x] 完整测试 69 项通过（`chore/mcp-telemetry-tags` 分支）

**复核中驳回的修改（保持原样）：**
- Skill Top 5 的 `max_over_time` 是正确语义：短生命周期 CLI 进程 counter 随进程重置，`increase` 跨进程归零；`max_over_time` 反映可见累计量
- Tools Top 5 的 `$__range` 是用户可选范围，非 bug
- `histogram_quantile` 不加 `{le="+Inf"}` 兜底（会只保留最后一个桶破坏分位数计算）

**遗留：**
- [ ] Agent Dashboard 补充 Turn 指标（当前仅 2 个面板）
- [ ] 评估 context compaction 指标：用于长会话压缩健康分析；当前暂不埋点，待 Memory/压缩校准阶段确定语义与展示方式。
- [ ] 评估每 Turn 工具链长度：当前事件契约无法在 TurnEnd 可靠取得工具调用数；暂不扩展事件模型或引入聚合状态。
- [ ] 评估 AskUser 频率：可在工具包装器低成本统计，但当前不新增行为指标，待确认自主性分析需求后再实施。
- [ ] Jaeger Trace Drilldown 未做（Jaeger 数据源已配 `uid: jaeger`，dashboard 无 drilldown 链接）

---

### 18. Agent Invocation 与受限 Subagent（优先级：中）

**目标：** 以 `Invocation` 作为一次独立 Agent 工作单元的统一术语，让 Orchestrator 的 Agent 节点和未来的 Subagent 工具共享标准 Agent 构建、运行、释放和取消链路。

- [x] Invocation 通用契约迁至 `InsightaAI.Agents.Subagents`，不在 `Agent` 核心暴露 `AgentConfig`
- [x] CLI 内部 adapter 复用 `AgentFactory`；宿主负责工具、Skill、MCP、Hook、Memory、Telemetry 与安全策略装配
- [x] Session 存储已补充 `ParentSessionId` / `ParentInvocationId`；仍需单独决策父会话删除后的保留策略
- [x] 新建 `InsightaAI.Agents.Subagents`：定义 `SubagentDefinition`、`ISubagentCatalog`、`ISubagentAdapter` 与 `SubagentDispatcher`
- [x] Orchestrator 的 `NodeExecutor` 改依赖 `SubagentDispatcher`，移除 `Agent/Invocation` 依赖
- [x] CLI 提供 `CliInsightaSubagentAdapter`；Definition 只能收紧工具、CLI 保留安全配置主导权
- [x] Subagent 采用静态预授权：不注册交互式 `ToolPermissionHook`，始终保留 `SecurityPolicyHook`
- [x] 子 Agent 的 Skill、MCP、Memory 工具组统一映射为 `AgentConfig.ExcludedToolNames`；基础设施保留在 DI，项目指令作为独立 Prompt 选项
- [x] 实现项目级 `LocalSubagentCatalog`，并提供 reviewer / explorer / planner 三个只读定义
- [x] 建立独立 `InsightaAI.Agents.Subagents.Tests` 项目，覆盖 dispatcher 与 local catalog
- [x] CLI 主 Agent 注册核心 `delegate` 工具，通过本地 Catalog 委派，最大深度为 1
- [x] 提供最大深度为 1、权限只能收紧、结果先脱敏的核心 `delegate` 工具
- [ ] 以工具白名单实现 CLI `Explorer` Profile；不以 Prompt 代替只读约束

**设计文档：** [agent-invocation-design.md](agent-invocation-design.md)

**当前边界：** 第一阶段只实现 Insighta 内部 adapter。外部 Codex / Claude Code 暂缓接入，未来以新的 Definition 类型和 adapter 扩展，不将未确定的外部 CLI 协议写入公共契约。

---

## 当前优先级

已完成：Dashboard 拆分与 Anthropic 归一化（#17），以及 MCP Telemetry tag 命名分层与去重（#12）。后续可观测性工作保留 Agent Dashboard 的 Turn 指标、低基数行为指标评估和 Jaeger Trace Drilldown；见 `observability-design.md` §8。

1. Agent 安全增强（#14）：优先完成 Phase 2 L1 敏感路径保护——按 `SecurityConfig.SensitivePaths` 拦截 `read_file` / `grep` / `write_file` 等结构化工具；L3 结果脱敏已完成。
2. 运行时用量：区分流式模型未返回 token usage 与真实的 0，并继续推进 #16 的用户、会话与模型用量审计设计。
3. 可观测性：补充 Agent Dashboard 的 Turn 指标与 Jaeger Trace Drilldown；context compaction、每 Turn 工具链长度、AskUser 频率仅在确认语义后新增低基数指标。
4. Memory 自动注入校准：基于已记录的候选与入选/淘汰原因，根据真实会话调整门槛与身份查询兜底策略。
5. Hook：明确取消/中止场景的 Agent 事件契约。

Agent 服务生命周期已完成当前阶段决策：私有 Provider 支持 Singleton/Transient，不支持 Scoped；见 `agent-service-lifetime.md`。
