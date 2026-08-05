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

**待优化：**
- [x] Agent 实现 IDisposable，释放旧构造函数创建的 ServiceProvider
- [ ] 未来如需 Scoped 服务（如 DbContext），需在 Agent Loop 时 CreateScope 创建子容器

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

## 当前优先级

1. Memory 自动注入校准：基于已记录的候选与入选/淘汰原因，根据真实会话调整门槛与身份查询兜底策略。
2. Agent 服务生命周期：明确 Singleton/Scoped 支持策略。
3. Hook：明确取消/中止场景的 Agent 事件契约。
4. MCP Telemetry：完成 tag 命名分层与去重。
