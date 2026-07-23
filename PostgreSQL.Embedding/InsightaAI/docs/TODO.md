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
- [ ] Agent 实现 IDisposable，释放旧构造函数创建的 ServiceProvider
- [ ] 未来如需 Scoped 服务（如 DbContext），需在 Agent Loop 时 CreateScope 创建子容器

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

### 4. AgentBuilder 生命周期一致性（优先级：中）

**问题描述：**
`AgentBuilder` 的 `WithXxx()` 方法使用 `TryAddSingleton` 注册服务，但构造函数中未预先注册 `ToolRegistry`。如果用户不调用 `WithToolRegistry()`，`Agent` 构造时会抛出 `InvalidOperationException`。

**当前代码：**
```csharp
// AgentBuilder 构造函数
public AgentBuilder(AgentConfig config)
{
    _config = config;
    _services = new ServiceCollection();
    _services.TryAddSingleton(_config);
    // ToolRegistry 未注册！
}

// Agent 构造函数
_toolRegistry = serviceProvider.GetRequiredService<ToolRegistry>();  // 会抛异常
```

**待优化：**
- [ ] 构造函数中默认注册 `ToolRegistry`
- [ ] 或者在 `Build()` 中检查 `ToolRegistry` 是否已注册并给出清晰错误提示

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

6.2 `LlmRequestDuration` 标签维度不一致
- **位置**: `TelemetryLlmClient.cs` `RecordMetricsAndTags`
- **问题**: `LlmRequestDuration` 直方图 Record 时只传了 duration，未携带 `gen_ai.adapter`、`gen_ai.system`、`gen_ai.request.model` 标签，导致无法按模型维度做细粒度分析
- **建议**: 统一使用与 Counter 相同的 TagList，确保 histogram 维度与 counter 一致

**已完成：**
- [x] 第 5 点已修复：`TelemetryToolCallHandler` catch 块补充 `gen_ai.tool.is_allowed` 标签
- [x] 第 5.1 点已修复：`LlmRequestDuration` metric 名从 `insighta.llm.request.duration` 改为 `gen_ai.client.operation.duration`
- [x] Token counter 命名统一为 `gen_ai.client.tokens.input/output/cache_hit`（保持独立 counter 结构，仅改前缀与 OTel GenAI 对齐）

---

### 6.3 修复已有测试失败（优先级：中）

**问题描述：**
历史上存在 9 个相关测试失败，目前已修复并纳入完整 Agent 测试集。

**失败清单：**
- [x] 4 个 `SessionMemoryCompactStrategy` 相关测试
- [x] 5 个 `SessionMemoryHookLlm` 相关测试

---

## 功能特性

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
- `SimpleMcpConnectionPool` 保持 `mcp.server.name`/`mcp.server.version`（来自 server `initialize` 握手，语义正确）

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

**待处理：**
- [ ] Hook 必要的内部处理在对外 `yield return` 事件前完成，避免消费者提前停止枚举导致 Hook 未执行
- [ ] `AgentEventHookContext.Event` 改为不可变事件快照，消除共享可变 Context 在 fire-and-forget Hook 中的竞态
- [ ] 统一 Hook 调度语义：调度过程可等待，耗时后台工作由具体 Hook 自行启动
- [ ] 设计并接入 `AgentErrorEvent` 异常生命周期，明确 Failed、Cancelled、Recoverable 和是否重新抛出
- [ ] 真正的 Chat Session 创建、归档、删除事件由会话存储或应用层负责，不放入 `AgentLoop`

---

## 记录时间
- 2026-07-03: 创建文档，记录当前待办事项
- 2026-07-15: 新增 Agent 依赖注入待办、TiktokenTokenEstimator、测试失败清单
- 2026-07-16: 新增 AgentBuilder 生命周期一致性待办（ToolRegistry 注册问题）
- 2026-07-21: 新增 Tool Result Interception 功能待办（Phase 1-3 已完成）
- 2026-07-13: 新增 ESC 打断 LLM 生成功能（已完成，待优化 Spectre.Console 兼容性和单元测试）
- 2026-07-13: 新增 -c/--continue 恢复最近会话、reasoning 事件支持、工具调用 ID 一致性修复、Spectre.Console Status spinner 兼容性修复
- 2026-07-21: 新增 MCP Telemetry Tag 命名清理待办
- 2026-07-21: 完成统一 SummaryService、全量/增量共享模板、MaxTokens 恢复、会话标题生成及输入截取 fallback
- 2026-07-22: 统一 Agent Turn/Round 生命周期术语，调整 RoundEnd 与工具调用顺序，记录 Hook 与 ErrorEvent 后续工作
