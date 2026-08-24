# InsightaAI 工具结果生命周期设计 v2

> 状态：Core Implemented
>
> 版本：2.0
>
> 日期：2026-07-21
>
> 适用范围：`InsightaAI.Agent` 工具执行、结果持久化、上下文压缩与会话恢复

## 版本说明

### v2.0（2026-07-21）

本版本重新划分工具与 Agent Runtime 的职责，并将工具结果从一次性的“拦截结果”升级为具有完整生命周期的上下文资源。

主要变化：

- 工具只负责定义结果的语义化投影，不再直接负责落盘、目录管理和历史消息删除。
- 使用统一的 `ToolResultProcessor` 处理大小判断、落盘、Preview 生成和元数据组装。
- 使用 `ToolResultArtifactStore` 统一保存原始结果并生成 Artifact 引用；恢复和自动清理仍是后续工作。
- 使用 `ProcessedToolResult` 替代 v1 的 `InterceptionResult`。
- 使用 `ToolResultRetentionLevel` 表达 `Full → Preview → Placeholder → Removed` 的渐进式降级。
- `MicroCompactStrategy` 不再重复调用工具的 `Intercept()`，只负责推进历史结果的保留等级。
- 落盘状态与上下文保留等级分离：结果可以只保留 Placeholder，同时仍可从 Artifact 恢复。
- 删除工具结果时必须维护 ToolCall/ToolResult 配对结构。

实现状态：核心执行链、结构化状态、内置工具 Projector、消息存储贯通、MicroCompact 状态推进和策略级联已经落地。Artifact 自动清理、CLI 展示和 Telemetry 指标仍列入后续工作。

### v1.x（历史设计）

历史设计见：

- `../archives/tool-result-truncation-design.md`
- `../archives/tool-result-truncation-design-review.md`
- `../archives/context-compression-design.md`

v1 通过 `ITool.Intercept()`、`InterceptionResult` 和 `Message.ToolResultIntercepted` 实现执行时截断与部分工具落盘。该设计解决了超大结果直接进入上下文的问题，但存在职责交叉、状态表达不足、持久化元数据丢失和无法继续渐进降级等限制。

---

## 1. 背景

工具结果具有两个相互独立的生命周期：

1. **数据生命周期**：原始结果是否被持久化、能否恢复、何时清理。
2. **上下文生命周期**：发送给 LLM 的结果当前保留多少内容。

这两个维度不能用单一的 `ToolResultIntercepted` 布尔值表达。例如，一个工具结果可以同时处于以下状态：

```text
原始结果：已经落盘，可恢复
上下文表示：仅保留 Placeholder
```

因此，新设计将 Artifact 与 Retention Level 分开建模。

## 2. 设计目标

### 2.1 渐进式压缩

工具结果按信息损失程度逐级降级：

```text
Full
  → Preview
  → Placeholder
  → Removed
```

系统始终优先选择损失更小、可恢复性更强的操作。

### 2.2 工具拥有语义，框架拥有生命周期

工具负责回答：

- 哪些内容最值得保留；
- 怎样生成有意义的 Preview；
- 最小摘要需要包含什么；
- 工具是否可安全重放；
- 工具是否具有副作用；
- 最低允许保留到什么等级。

框架负责回答：

- 什么时候需要落盘；
- 文件保存在哪里；
- 什么时候从 Full 降级到 Preview；
- 什么时候替换为 Placeholder；
- 什么时候删除消息；
- 如何维护 ToolCall/ToolResult 配对；
- 如何持久化元数据、清理文件和记录 Telemetry。

### 2.3 默认安全

- 默认保留工具结果，不进行不可逆删除。
- 有副作用的工具默认至少保留 Placeholder。
- 无法确认可重放性的工具不得仅以“重新调用工具”作为恢复方式。
- 删除 ToolResult 时不得留下孤立的 ToolCall。
- 落盘成功之前不得丢弃原始内容。

## 3. 总体链路

```text
LLM 产生 ToolCall
  ↓
ToolCallExecutor
  ↓ 执行
ITool.ExecuteAsync()
  ↓
原始 ToolResult
  ↓
ToolResultProcessor.ProcessAsync()
  ├─ 计算字符数、字节数、行数和上下文压力
  ├─ 解析工具的 Result Retention Policy
  ├─ 必要时由 ToolResultArtifactStore 保存原始结果
  ├─ 调用 IToolResultProjector 生成语义化 Preview
  └─ 返回 ProcessedToolResult
  ↓
AgentLoop
  ├─ 发出工具完成事件
  └─ 将 ProcessedToolResult 转换为 ToolResult Message
  ↓
Message Storage
  └─ 持久化内容、RetentionLevel 与 Artifact 元数据
  ↓
后续 LLM 轮次
  ↓
ContextManager / MicroCompactStrategy
  ├─ 选择可降级的历史工具结果
  ├─ Preview → Placeholder
  ├─ Placeholder → Removed
  ├─ 维护 ToolCall/ToolResult 配对
  └─ 重新估算 token，判断是否继续升级压缩层级
```

## 4. 职责划分

### 4.1 `ToolCallExecutor`

职责：

- 保持顺序或并行执行工具；
- 将原始 `ToolResult` 交给 `ToolResultProcessor`；
- 传递 `ProcessedToolResult`；
- 发出执行事件。

不再负责：

- 拼接落盘路径；
- 写入工具结果文件；
- 默认截取前 200 行；
- 直接判断具体工具的 Preview 内容。

### 4.2 `IToolResultProjector`

可选的工具能力接口。工具通过它描述如何保留结果语义，但不执行 IO。

```csharp
public interface IToolResultProjector
{
    ToolResultRetentionPolicy RetentionPolicy { get; }

    ToolResultProjection CreatePreview(
        ToolResult result,
        ToolResultProjectionContext context);

    ToolResultProjection CreatePlaceholder(
        ToolResultProjectionContext context);
}
```

没有实现该接口的工具使用 `DefaultToolResultProjector`。

### 4.3 `ToolResultProcessor`

工具结果进入消息历史前的统一处理入口。

职责：

- 保留原始 `IsError` 和 `Metadata`；
- 根据预算和大小决定是否落盘；
- 调用 ArtifactStore；
- 选择工具自定义或默认 Projector；
- 生成结构化 `ProcessedToolResult`；

当前落盘失败会使处理失败，不会提交 Preview；更细粒度的降级与重试策略仍待实现。

### 4.4 `ToolResultArtifactStore`

当前已实现：

- 保存原始完整内容；
- 生成无碰撞 Artifact ID 和路径；
- 支持取消与异步写入。

尚待实现：读取 API、存在性验证、删除与过期清理，以及可选的完整性校验。

### 4.5 `MicroCompactStrategy`

职责：

- 查找超过保护范围的历史工具结果；
- 根据上下文压力推进 Retention Level；
- 保留最近 N 个结果；
- 尊重工具的最低保留等级；
- 维护消息协议结构；
- 返回压缩前后的 token 与消息数量。

不再负责：

- 第一次落盘；
- 再次调用 `ITool.Intercept()`；
- 为每种工具维护一套截断类；
- 根据工具名称硬编码语义。

## 5. 核心数据模型

### 5.1 保留等级

```csharp
public enum ToolResultRetentionLevel
{
    Full = 0,
    Preview = 1,
    Placeholder = 2,
    Removed = 3
}
```

等级越高，上下文内容越少，信息损失越大。

### 5.2 Artifact

```csharp
public sealed record ToolResultArtifactInfo
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public string ContentType { get; init; } = "text/plain";
    public long ByteSize { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

Runtime 通过该结构维护 Artifact 引用；当前 Placeholder 由框架生成，并包含 Artifact ID 与可读取路径。

### 5.3 工具保留策略

```csharp
public sealed record ToolResultRetentionPolicy
{
    public bool CanReplay { get; init; }
    public bool HasSideEffects { get; init; }
    public bool PreferPersistence { get; init; }
    public ToolResultRetentionLevel MinimumLevel { get; init; }
        = ToolResultRetentionLevel.Placeholder;
}
```

示例：

| 工具 | CanReplay | HasSideEffects | MinimumLevel |
|------|-----------|----------------|--------------|
| `read_file` | true | false | Removed |
| `grep` | true | false | Removed |
| `web_search` | true | false | Removed |
| `bash` | 不确定 | 可能 | Placeholder |
| `edit_file` | false | true | Placeholder |
| `write_file` | false | true | Placeholder |

### 5.4 工具投影结果

```csharp
public sealed record ToolResultProjection
{
    public required ContentBlock[] Content { get; init; }
    public required ToolResultRetentionLevel Level { get; init; }
}
```

### 5.5 统一处理结果

```csharp
public sealed record ProcessedToolResult
{
    public required ToolResult Result { get; init; }
    public required ToolResultState State { get; init; }
    public int CurrentLength { get; init; }
}
```

生命周期等级、Artifact 和工具保留策略快照统一保存在 `State` 中。

## 6. 执行时处理

### 6.1 处理顺序

```text
1. 获取原始 ToolResult
2. 计算大小和结构信息
3. 读取工具 RetentionPolicy
4. 判断是否需要持久化
5. ArtifactStore 保存原始完整结果
6. 保存成功后生成 Preview
7. 返回 ProcessedToolResult
```

必须先成功保存原文，再用 Preview 替换上下文内容。

### 6.2 默认阈值

初始建议：

- 大于 30 KiB：优先落盘；
- `PreferPersistence = true`：只要结果非空即落盘；
- 落盘后生成 Preview；
- 阈值可配置，工具策略可提出偏好，但最终由 Runtime 决定。

字符数、UTF-8 字节数和 token 估算用途不同：

- 字节数决定磁盘和传输成本；
- token 估算决定上下文压力；
- 行数帮助行结构工具生成 Preview。

### 6.3 默认 Preview

对于没有自定义 Projector 的工具：

- 文本结果保留头尾，而不是只保留开头；
- 保留错误状态；
- 保留 `ToolResult.Metadata`；
- 显示原始大小、当前大小和是否可恢复；
- 不解析未知 JSON 并假设其结构。

## 7. 渐进式降级状态机

```text
                 ┌──────────────┐
                 │     Full     │
                 └──────┬───────┘
                        │ 执行时结果过大或 L1 压力
                        ▼
                 ┌──────────────┐
                 │   Preview    │
                 └──────┬───────┘
                        │ 上下文压力继续上升
                        ▼
                 ┌──────────────┐
                 │ Placeholder  │
                 └──────┬───────┘
                        │ 允许删除且仍需释放空间
                        ▼
                 ┌──────────────┐
                 │   Removed    │
                 └──────────────┘
```

状态只能向信息更少的方向推进。恢复 Artifact 不应直接修改历史状态，而应通过工具或专用读取能力产生新的 ToolResult。

### 7.1 Full → Preview

- 优先使用工具的语义化 Projector；
- 没有 Projector 时使用默认头尾 Preview；
- 若结果不可重放，优先落盘；
- Preview 仍保留 ToolCall/ToolResult 配对。

### 7.2 Preview → Placeholder

Placeholder 应至少包含：

- 工具名称；
- 执行结果是成功还是错误；
- 原始结果是否已持久化；
- 恢复或重新执行方式；
- 对有副作用工具的最小操作摘要。

示例：

```text
[Previous read_file result omitted to reduce context. Full output is available as artifact tr_abc123.]
```

### 7.3 Placeholder → Removed

只有满足以下条件时允许删除：

- 工具策略允许 `Removed`；
- 工具无不可忽略的副作用；
- 结果可安全重放或已有 Artifact；
- 删除后不会破坏当前模型供应商的消息协议；
- 不属于最近保护窗口。

## 8. ToolCall/ToolResult 配对规则

工具结果不是独立消息，删除时必须处理对应的 `ToolCallBlock`。

假设 Assistant 消息包含多个调用：

```text
Assistant:
  TextBlock
  ToolCall A
  ToolCall B
ToolResult A
ToolResult B
```

删除 A 时：

```text
Assistant:
  TextBlock
  ToolCall B
ToolResult B
```

规则：

1. 通过 `ToolCallId` 精确匹配。
2. 从 Assistant 内容中移除对应的 `ToolCallBlock`。
3. 删除对应 ToolResult Message。
4. Assistant 仍有其他内容时保留。
5. Assistant 内容为空时才删除该 Assistant Message。
6. 删除前后的消息必须通过供应商协议测试。

Placeholder 阶段不删除配对，只替换 ToolResult 内容。

## 9. ContextManager 与压缩层级

建议阈值：

```text
45%：允许 MicroCompact，优先 Full → Preview
65%：允许 SessionMemoryCompact；MicroCompact 可先推进 Preview → Placeholder
80%：允许 TraditionalCompact；必要时清理允许 Removed 的工具配对
```

这些阈值表示策略开始有资格运行，不是互斥区间。阈值和 CLI 的上下文占用率统一以 `AvailableInputTokens = MaxContextTokens - ReservedForOutput` 为分母。

每个策略使用“试算 → 提交”两阶段执行：

```text
复制当前消息
  → 在副本上执行策略并重新估算 token
  → 无实际收益：丢弃副本，原消息保持不变
  → 有实际收益：提交副本
```

自动压缩仍按阈值依次检查所有合格策略，并允许在提交有效结果后继续检查下一层。手动 `/compact auto` 不受自动阈值限制，按优先级逐个试算策略，提交第一个产生实际收益的结果后停止。

MicroCompact 在某一级投影没有收益时不推进状态；若当前阈值允许更低的保留等级，则继续试探下一等级。例如 65% 时，内容不变的 Preview 可以直接跳过并尝试 Placeholder。

## 10. 消息与存储模型

工具结果消息需要持久化结构化状态，而不是只把路径嵌入文本。

当前结构：

```csharp
public sealed record ToolResultState
{
    public ToolResultRetentionLevel RetentionLevel { get; init; }
    public ToolResultArtifactInfo? Artifact { get; init; }
    public int OriginalLength { get; init; }
    public bool CanReplay { get; init; }
    public bool HasSideEffects { get; init; }
    public ToolResultRetentionLevel MinimumLevel { get; init; }
        = ToolResultRetentionLevel.Placeholder;
}
```

需要贯通：

- `ProcessedToolResult`
- `Message`
- `MessageRecord`
- `MessageConverters`
- JSONL/PostgreSQL 存储
- 会话恢复

恢复会话后，RetentionLevel 和 Artifact 可用性必须保持一致。

## 11. Artifact 生命周期

目录结构建议：

```text
~/.insighta/sessions/{sessionId}/tool_results/
  {toolName}_{timestamp}_{toolCallId}_{artifactId}.txt
```

要求：

- 文件名包含 `toolCallId` 或随机 Artifact ID，支持并行执行；
- 保存原始完整结果，不能保存 Preview 冒充原文；
- 正常会话清理时按配置删除；
- 异常退出后按保留期清理；
- Message 引用不存在的 Artifact 时，应降级为不可恢复状态；
- Artifact 清理不得影响仍被活跃会话引用的文件。

## 12. 错误处理

### 12.1 落盘失败

- 不得用 Preview 替换原始结果后再报告成功；
- 保留原始结果，或在硬限制下采用明确标记的不可逆截断；
- 记录错误 Telemetry，但工具执行本身的成功/失败状态不应被覆盖。

### 12.2 Preview 生成失败

- 使用默认 Projector；
- 默认 Projector 失败时保留原始结果；
- 不允许空内容静默替换。

### 12.3 Artifact 丢失

- Placeholder 更新为“Artifact unavailable”；
- 可重放工具提示重新调用；
- 不可重放工具保留现有摘要，避免进一步删除。

## 13. Telemetry 与事件

建议记录：

- `tool.result.original_size`
- `tool.result.current_size`
- `tool.result.retention_level`
- `tool.result.persisted`
- `tool.result.artifact_size`
- `tool.result.saved_tokens`
- `tool.result.projection`（default/tool-specific）
- `tool.result.transition`（如 `preview_to_placeholder`）

CLI 可显示：

```text
Tool result: 84.2 KiB → 6.1 KiB preview, full output persisted
```

## 14. 安全与隐私

- Artifact 与原始 ToolResult 具有相同敏感级别；
- 路径不得跨越 session 目录；
- 文件权限应限制为当前用户；
- Telemetry 不记录原始内容和完整路径；
- 长期保留前应考虑敏感信息扫描或加密；
- 工具结果包含凭据时，应在落盘前应用安全策略。

## 15. 迁移计划

### Phase 1：修复现有正确性问题

- 默认策略落盘时保存原始 `totalText`，而不是 Preview。
- 文件名加入 `toolCallId`。
- 保留 `ToolResult.IsError` 和 `Metadata`。
- 并行结果按原始 ToolCall 顺序输出。
- 修正大小和行数提示格式。

### Phase 2：引入新模型

- 新增 `ToolResultRetentionLevel`。
- 新增 `ToolResultArtifactInfo`。
- 新增 `ProcessedToolResult`。
- 新增 `ToolResultRetentionPolicy`。
- `ToolExecutionResult` 携带处理后的 `ToolResult` 与 `ToolResultState`。

### Phase 3：统一处理与存储

- 提取 `ToolResultProcessor`。
- 提取 `ToolResultArtifactStore`。
- 将 FileRead/WebFetch 的直接 IO 迁移到 ArtifactStore。
- 贯通 Message 与 Storage 元数据。

### Phase 4：迁移工具语义化投影

- 引入 `IToolResultProjector`。
- 迁移 Bash、Grep、FileRead、WebFetch、WebSearch。
- 增加默认 Projector。
- 废弃 `ITool.Intercept()` 和 `InterceptionResult`。

### Phase 5：重构 MicroCompact

- 删除 `CompactableTools` 和旧 `ToolTruncationStrategy`。
- 实现 Retention Level 状态推进。
- 实现 ToolCall/ToolResult 成对删除。
- 增加收益检查与压缩策略级联。

### Phase 6：测试与可观测性

- 大结果落盘并可恢复。
- 落盘失败不丢原始内容。
- Preview → Placeholder → Removed 状态转换。
- 副作用工具最低保留等级。
- 并行工具 Artifact 不冲突。
- 会话保存与恢复保留结构化状态。
- OpenAI、Anthropic、Gemini 消息配对兼容性。
- CLI 和 Telemetry 展示处理结果。

## 16. 兼容与废弃策略

v2 在当前重构分支中按破坏性变更直接完成迁移，不保留旧接口适配层。以下 v1 类型和机制已经删除：

- `ITool.Intercept()`
- `InterceptionResult`
- `TruncationContext`
- `Message.ToolResultIntercepted`
- `MicroCompactStrategy` 中的 `CompactableTools` 与旧 `ToolTruncationStrategy`

外部自定义工具若实现过 `Intercept()`，应改为按需实现 `IToolResultProjector`；未实现时由 Runtime 的默认 Projector 处理。

## 17. 最终原则

1. 工具决定压缩后留下什么语义。
2. Runtime 决定什么时候压缩、是否落盘和推进到哪一级。
3. Artifact 保存原始数据，Projection 只表示上下文视图。
4. 可逆操作优先于不可逆操作。
5. Placeholder 优先于删除。
6. 删除必须维护 ToolCall/ToolResult 协议结构。
7. 每次压缩必须用实际 token 收益衡量效果。
8. 任何失败都不得静默伪装成“完整结果已保存”。
