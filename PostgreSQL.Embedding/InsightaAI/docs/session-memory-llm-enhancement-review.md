# Session Memory Hook LLM 增强设计 — Review

> 基于 `docs/session-memory-llm-enhancement.md`（设计文档）与当前代码实现的差异分析。
> Review 日期: 2025-06-23

---

## 总体评分

| 维度 | 评分 | 说明 |
|------|------|------|
| **架构设计** | ★★★★☆ | Hook 模式正确，调用链清晰，竞品参考充分 |
| **完整性** | ★★★★☆ | 覆盖了接口、流程、降级、配置、测试步骤 |
| **可实施性** | ★★★★★ | 步骤明确、文件一一对应、改动范围清晰 |
| **细节严谨性** | ★★★☆☆ | 同步阻塞问题被承认但未充分解决；空节保留策略值得商榷 |
| **竞品参考价值** | ★★★★★ | 锚定增量摘要优于 Claude Code 的全量覆盖方式 |

**总分：4.2 / 5.0**

---

## 一、设计亮点

### 1.1 锚定增量摘要（Anchored Summary）是最正确的选择

没有走"每轮重新总结全部"的蛮力路线，而是 **读旧摘要 → 合并新信息 → 写回**。这让摘要随对话自然生长，而不是每次推倒重来。

对比竞品：
- **Claude Code**：不做增量，每轮全量总结 → 越到后面越丢失早期上下文
- **Open Code**：做锚定增量 → 多次压缩不丢失信息
- **本设计**：采用 Open Code 的方案，优于 Claude Code

### 1.2 降级路径（关键词匹配 fallback）务实

LLM 调用可能因网络、限流、Token 耗尽而失败。保留关键词匹配作为 fallback，让系统在异常时不会完全失忆。值得肯定。

### 1.3 结构化摘要模板

相比自由文本摘要，8 节结构化模板（Goal / Progress / Decisions / Next Steps / Critical Context / Relevant Files...）让 LLM 输出更可控、更可解析。其中 `Critical Context` 和 `Relevant Files` 两个 section 尤其实用——它们是后续工具调用的直接输入。

### 1.4 压缩后导入提示的优先级规则

```
- 最新消息 WINS：覆盖摘要中的旧任务
- 反转信号立即生效："停下"、"撤销"、"算了" → 终止旧工作
- 持久化记忆始终权威：MEMORY.md / USER.md 不受压缩影响
```

这三条规则解决了压缩后最常见的模型行为问题——模型倾向于"完成旧任务"而不是"听从最新指令"。参考 Hermes Agent，篇幅虽小但实际价值很高。

### 1.5 配置参数默认值合理

| 参数 | 默认值 | 评价 |
|------|--------|------|
| `minRoundsBeforeLlm` | 3（设计）/ 5（代码） | 前几轮信息本来就少，关键词够用 |
| `summaryInterval` | 1（设计）/ 3（代码） | 设计 1 偏保守；代码 3 更经济 |
| `Temperature` | 0.2-0.3 | 摘要需要确定性，这个范围正确 |
| `MaxTokens` | 512（设计）/ 1024（代码） | 512 足够模板+要点；1024 更宽松 |

---

## 二、值得商榷的设计决策

### 2.1 [重要] OnRoundEndAsync 的同步阻塞问题

**设计文档描述**：Hook 是同步执行，会阻塞下一轮。

**当前代码**：已改为 fire-and-forget（第 96 行 `Task.Run`），设计文档 vs 代码已有偏差。

**分析**：这个改动方向是正确的。`SessionMemoryHook` 做的事情——提取摘要、写文件——是 agent 的家务活，用户不应该等它完成再看到响应。

调用链的职责划分：

```
用户发送消息
  → Agent.RunStreamAsync()         ← 用户在前台等这个
  → LLM 调用 + 工具执行            ← 用户关心这个
  → hook.OnRoundEndAsync()         ← agent 自己的家务活
  → LLM 摘要 + 文件写入            ← 用户不需要等这个
  → 返回响应给用户
```

**核心原则**：agent 的家务活不应阻塞用户交互。

**建议**：设计文档应更新以反映 fire-and-forget 的实际实现，并补充对线程安全和取消语义的说明（见下文第三节）。

---

### 2.2 [中等] Prompt 中"禁止工具调用"的约束强度

设计文档在 Prompt 的开头和结尾都强调 "Do NOT call any tools"，参考了 Claude Code。

**风险**：如果 `ILlmClient` 的实现携带了工具列表（即使当前代码显式传了 `Tools = []` 和 `ToolChoice = ToolChoiceMode.None`），Prompt 层面的约束可能被 LLM 忽略。

**实际代码已经处理了这个风险**（第 257-258 行）：

```csharp
Tools = [],              // 不提供工具
ToolChoice = ToolChoiceMode.None,  // API 层面禁止
```

**结论**：当前代码在 API 层面已经杜绝了工具调用，比 Prompt 约束更可靠。设计文档应补充说明从 API 层面禁止工具调用，而不是仅依赖 Prompt 约束。

---

### 2.3 [轻微] 摘要模板的"空节保留"规则浪费 Token

设计说 "Keep every section, even when empty"。在早期对话中，Blocked、Relevant Files、Key Decisions 可能都是空的，但模板仍然输出 `- (none)`。

8 个 section 即使全空也要占约 200 tokens。每轮重复输出，累积起来不少。

**建议**：
- 初始时输出完整模板
- 增量摘要时只输出 **非空节**，LLM 会隐式保留其他节的内容
- 或将模板放在 System Prompt 里，LLM 响应只输出内容部分

---

### 2.4 [轻微] HookContext 和 ToolExecutionContext 职责边界

两个上下文都承载 `LlmClient`，区别仅在于一个是 Hook 专用、一个是 Tool 专用。

如果未来有某个组件既是 Hook 又是 Tool（例如 ToolHook），它需要同时使用两个上下文，会增加复杂度。

**建议**：加一条注释说明两者的设计意图和选用原则。

---

### 2.5 [中等] TraditionalCompact 复用 session-memory.md 的信息丢失

设计第 4.3 节说：

```csharp
if (!string.IsNullOrEmpty(sessionMemory))
{
    summary = sessionMemory;    // 复用已有的锚定摘要
}
```

但 `session-memory.md` 是 **增量更新**的，它记录的是"重要信息"，不是"全部历史"。直接用这个做 TraditionalCompact 的全量摘要，可能会丢失一些不在摘要中的上下文。

**建议**：
- 复用范围应仅限于 SessionMemoryCompact（L2）
- TraditionalCompact（L3）仍然做全量 LLM 摘要，但以 session-memory.md 作为输入的一部分
- 或复用时间上 disclaimer：`[Note: This summary was built incrementally and may not capture all details]`

---

## 三、代码实现与设计文档的差距

### 3.1 差距矩阵

| 设计文档要求 | 实际实现 | 差距影响 | 优先级 |
|-------------|---------|---------|--------|
| **锚定增量摘要**（读旧摘要→增量更新） | 仅传最近 10 条消息，不读旧摘要 | **大**：每次覆盖写，不保留早期信息 | P0 |
| **结构化 Markdown 模板**（8 节） | 扁平列表（用户偏好 / 决策 / 问题） | **中**：可读性和可解析性下降 | P1 |
| **两步式 Prompt**（analysis→summary） | 单步 Prompt | **小**：影响质量但不会失效 | P2 |
| **Hermes 风格导入提示**（优先级规则） | 简单边界标记 | **中**：模型可能不正确地遵循旧任务 | P1 |
| **TraditionalCompact 复用摘要** | 总是全量 LLM 摘要，不复用 | **小**：多一次 LLM 调用，不影响正确性 | P2 |

**底线**：设计文档描述的"高质量增量式会话记忆"在实际代码中 **尚未完全落地**。当前实现更像是"用 LLM 替换了关键词匹配，但提取逻辑没有本质变化"。

### 3.2 代码层面的具体问题

#### 问题 A：messages 集合的线程安全（中风险）

```csharp
_ = Task.Run(async () =>
{
    await ExtractAndSaveMemoryAsync(context, round, messages, assistantMessage, CancellationToken.None);
});
```

`messages` 是 `IReadOnlyList<Message>`，但不保证底层 List 不被修改。Agent 主循环可能在下一轮追加新 Message，后台线程正在 `TakeLast(10)`。

可能的结果：
- 读到半截修改过的数据
- TakeLast 时底层数组被 resize，抛出异常

**建议**：在 fire-and-forget 前做 `messages.ToList()` 快照，传入副本。

#### 问题 B：SemaphoreSlim + fire-and-forget 的排队积压（低风险）

```csharp
await _lock.WaitAsync(cancellationToken);
try
{
    // 读文件 → 追加 → 写文件
}
finally
{
    _lock.Release();
}
```

```
Round 5 → fire-and-forget → LLM 摘要（~2s）→ 写文件（锁）
Round 6 → fire-and-forget → 排队等待锁
Round 7 → fire-and-forget → 排队等待锁
```

如果 LLM 摘要慢，多个后台任务排队。后面的摘要写入时基于的是几分钟前的"已有内容"——虽然不会丢信息（增量写入），但执行顺序可能和 round 顺序不同。

**建议**：考虑将写入策略从"每轮写文件"改为"内存缓存 + 按需合并写入"（详见第四节）。

---

## 四、更深层的设计思考

### 4.1 Fire-and-Forget 的写入时机

当前方案：

```
每轮结束 → Task.Run → LLM 摘要 → 写文件
                         ↓
                    压缩触发 → 读文件 → 上下文替换
```

`GetSessionMemoryAsync()` 被调用时（压缩触发），文件可能还没写完（虽然概率很低）。

替代方案：

```
每轮结束 → Task.Run → LLM 摘要 → 存在内存里（ConcurrentDictionary<int, string>）
                                     ↓
压缩触发 → 从内存读取所有轮次摘要 → 合并 → 一次性写入文件 → 上下文替换
```

优点：
- **零磁盘 IO** 在关键路径上
- **没有 SemaphoreSlim 竞争**
- **压缩时读到的摘要一定是最新的**（内存里不可能还没写完）
- 文件写入变成"有人需要读它的时候才写"

代价：
- 内存占用（微不足道，每个 session 的摘要撑死几 KB）
- 进程崩溃时丢数据（但 session-memory 是短期记忆，丢了不影响长期记忆）

### 4.2 "用户不需要操心"原则的延伸

这个原则不仅适用于 fire-and-forget，还可以延伸到：

1. **文件 IO**：所有磁盘操作都应是异步的、非关键路径的
2. **网络请求**：LLM 摘要调用不应增加用户感知的延迟
3. **错误处理**：后台任务失败不应抛出异常到上层（当前代码用 `Debug.WriteLine` 处理了，赞）
4. **取消语义**：用户关闭会话不应中断正在进行的摘要写入（当前代码用 `CancellationToken.None` 处理了，赞）

### 4.3 当前 LLM Prompt 的改进空间

当前 Prompt（第 230-251 行）是单步的：

```
Analyze this conversation and extract key information...
```

设计文档要求的锚定增量 Prompt：

```
Read existing summary → merge new facts → remove stale facts → output
```

**差异**：当前实现每次都是"从零提取"，没有"锚定"。这导致：
- Round 1 摘要：讲了一些事实
- Round 5 LLM 调用：只看最近 10 条，可能遗漏 Round 1 的事实
- Round 10：更早的信息已经丢失

**建议**：这是当前实现与设计文档最大的差距，应优先修复。实现锚定增量摘要的 Prompt 结构：

```
System: Do NOT call any tools.
         Update the anchored summary below using the conversation above.
         Preserve still-true details, remove stale, merge new facts.

<previous-summary>
{已有的 session-memory.md 内容}
</previous-summary>

<conversation>
{最近几轮对话}
</conversation>

<template>
## Goal
...
</template>
```

---

## 五、优先级建议

| 优先级 | 事项 | 工作量 | 影响 |
|--------|------|--------|------|
| **P0** | 实现锚定增量摘要（读旧摘要→合并新信息） | 修改 Prompt，~0.5d | 解决信息丢失的核心问题 |
| **P1** | 切换到结构化 Markdown 模板（8 节） | 修改 Prompt，~0.5d | 提升可读性和可解析性 |
| **P1** | 添加 Hermes 风格压缩导入提示 | 修改 CompactStrategy，~0.5d | 防止模型错误地执行旧任务 |
| **P1** | messages 快照（线程安全） | 加一行 ToList()，~0.1d | 消除潜在的并发异常 |
| **P2** | 两步式 Prompt（analysis→summary） | 修改 Prompt，~0.3d | 提升摘要质量 |
| **P2** | TraditionalCompact 复用策略优化 | 修改 CompactStrategy，~0.5d | 减少不必要的 LLM 调用 |
| **P3** | 内存缓存替代每轮写文件 | 重构写入逻辑，~1d | 消除锁竞争、保证读取一致性 |
| **P3** | 更新设计文档 v2，反映实际实现 | 文档更新，~0.3d | 消除文档与代码的偏差 |

---

## 六、总结

设计文档的整体架构（Hook 模式、三层压缩、降级策略）是经过深思熟虑的，竞品参考（Open Code、Claude Code、Hermes Agent）扎实，实施步骤清晰可执行。

最大的 gap 在 **LLM 摘要的核心逻辑**：
- 设计文档说要"锚定增量摘要"——实际代码是"每轮重新提取"
- 设计文档说要"结构化模板"——实际代码是"扁平列表"

这两个差距导致当前实现的实际效果接近"用 LLM 替换了关键词匹配"，而没有达到设计文档预期的"高质量增量式会话记忆"。

好消息是：**基础设施已经完全到位**（HookContext、LlmClient 注入、降级路径、配置参数），核心 LLM 摘要逻辑的改动范围仅限于 Prompt 层面和少量流程调整，不涉及架构变更。

建议优先修复 P0 和 P1 的差距——这些改动都在 Prompt 和策略层面，风险低、收益高。
