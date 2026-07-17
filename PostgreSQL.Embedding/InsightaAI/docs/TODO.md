# InsightaAI Agent - 待办事项

> 愿景与期待详见 [VISION.md](VISION.md)

## 代码重构

### 1. 摘要服务统一（优先级：中）

**问题描述：**
`TraditionalCompactStrategy.GenerateSummaryAsync` 和 `SessionMemoryHook.GenerateAnchoredSummaryAsync` 存在重复代码。

**重复内容：**
- LLM 请求构建模式（Model、MaxTokens、Temperature、ToolChoice=None）
- 错误处理逻辑
- `ExtractSummary` 方法（提取 `<summary>` 标签内容）

**当前状态：**
- [x] TraditionalCompactStrategy 已添加 `ExtractSummary` 方法
- [ ] 创建公共的 `SummaryService` 或 `CompactionHelper` 类
- [ ] SessionMemoryHook 改用公共方法

**建议方案：**
提取 `ExtractSummary` 到 `CompactionHelper` 静态类，`GenerateSummaryAsync` 作为静态方法接受 `ILlmClient` 参数。

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
用户希望在 Tokens Usage 显示区域增加上下文用量百分比，类似内存占用，通过预估当前消息列表的 token 总数和上下文窗口大小做对比。

**已完成：**
- [x] 创建 `TokenEstimatorExtensions` 扩展方法（放在 `Extensions` 目录）
- [x] 替换 `TraditionalCompactStrategy` 和 `SessionMemoryCompactStrategy` 中的 `EstimateMessagesTokens` 方法
- [x] `IContextManager` 接口新增 `MaxContextTokens` 属性
- [x] `ContextManager` 实现 `MaxContextTokens` 属性
- [x] `AgentResult` 新增 `EstimatedContextTokens` 和 `MaxContextTokens` 字段
- [x] `Agent.cs` 填充新字段
- [x] `EventRenderer` 显示上下文用量百分比（带颜色：绿<70%、黄70-90%、红>90%）

---

## 代码质量

### 5. SessionMemoryHook 代码清理

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

### 6. 修复已有测试失败（优先级：中）

**问题描述：**
9 个测试失败需要修复（均为已有问题，非本次改动引入）。

**失败清单：**
- [ ] 4 个 `SessionMemoryCompactStrategy` 相关测试
- [ ] 5 个 `SessionMemoryHookLlm` 相关测试（关键词降级逻辑未实现）

---

## 功能特性

### 7. Tool Result Interception（优先级：高）

**问题描述：**
大型工具结果（如读取大文件、大范围搜索）会迅速消耗上下文窗口，导致压缩频繁触发。

**设计方案：**
在工具结果进入上下文前拦截，根据工具类型进行预处理（持久化、截断等）。

**已完成：**
- [x] Phase 1: Core Infrastructure
  - `TruncationContext`、`InterceptionResult` 类
  - `IToolExecutor.Intercept()` 默认接口方法
  - `ToolExecutor.TryInterceptResult()` 集成
  - `Message.ToolResultIntercepted` 标志
  - Feature flag（构造参数控制）
- [x] Phase 2: Built-in Tool Overrides
  - `FileReadTool.Intercept` — 持久化 + 200 行预览
  - `GrepTool.Intercept` — 文件名 + 匹配数量
  - `BashTool.Intercept` — 头尾各 50 行
  - `WebFetchTool.Intercept` — 重构为 IToolExecutor + 持久化 + 5000 字符预览
  - `WebSearchTool.Intercept` — 重构为 IToolExecutor + 10000 字符预览
- [x] Phase 3: MicroCompactStrategy Refactoring
  - 跳过已拦截结果（`ToolResultIntercepted = true`）
  - 委托给 `tool.Intercept()`

**待优化：**
- [ ] Phase 4: Testing & Polish
  - [ ] 单元测试：各工具的 `Intercept` 方法
  - [ ] 集成测试：大文件读取 → 持久化 → 重新读取
  - [ ] CLI 显示截断/持久化状态
  - [ ] 监控指标：截断频率、持久化频率
- [ ] Phase 5: Cleanup & Documentation
  - [ ] `ToolResultDirectory` 生命周期清理（正常退出 + 异常退出）
  - [ ] 性能基准测试
  - [ ] 移除冗余的 `ToolTruncationStrategy` 类（可选）

**详细设计文档：** [tool-result-truncation-design.md](tool-result-truncation-design.md)

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

## 记录时间
- 2026-07-03: 创建文档，记录当前待办事项
- 2026-07-15: 新增 Agent 依赖注入待办、TiktokenTokenEstimator、测试失败清单
- 2026-07-16: 新增 AgentBuilder 生命周期一致性待办（ToolRegistry 注册问题）
- 2026-07-21: 新增 Tool Result Interception 功能待办（Phase 1-3 已完成）
- 2026-07-13: 新增 ESC 打断 LLM 生成功能（已完成，待优化 Spectre.Console 兼容性和单元测试）
- 2026-07-13: 新增 -c/--continue 恢复最近会话、reasoning 事件支持、工具调用 ID 一致性修复、Spectre.Console Status spinner 兼容性修复
- 2026-07-21: 新增 MCP Telemetry Tag 命名清理待办
