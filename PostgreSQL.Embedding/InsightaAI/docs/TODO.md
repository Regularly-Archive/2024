# InsightaAI Agent - 待办事项

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

### 4. 上下文用量显示（优先级：中）

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

## 记录时间
- 2026-07-03: 创建文档，记录当前待办事项
- 2026-07-15: 新增 Agent 依赖注入待办、TiktokenTokenEstimator、测试失败清单
