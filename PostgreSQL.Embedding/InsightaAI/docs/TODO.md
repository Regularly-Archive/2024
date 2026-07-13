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
- [x] `catch (OperationCanceledException)` 调用 `EventRenderer.ShowInterrupted()` 显示中断提示
- [x] 中断后不保存未完成的助手消息，避免 LLM 下轮重复生成
- [x] `PromptUser()` 前清空输入缓冲区，防止 ESC 残留泄漏到 prompt
- [x] `finally` 块确保 ESC 监听任务退出

**待优化：**
- [ ] Spectre.Console `Status` spinner 运行时 `Console.KeyAvailable` 可能不稳定，考虑替换为不接管终端的实现
- [ ] 取消流程单元测试

---

## 记录时间
- 2026-07-03: 创建文档，记录当前待办事项
- 2026-07-15: 新增 Agent 依赖注入待办、TiktokenTokenEstimator、测试失败清单
- 2026-07-16: 新增 AgentBuilder 生命周期一致性待办（ToolRegistry 注册问题）
- 2026-07-21: 新增 Tool Result Interception 功能待办（Phase 1-3 已完成）
- 2026-07-13: 新增 ESC 打断 LLM 生成功能（已完成，待优化 Spectre.Console 兼容性和单元测试）
