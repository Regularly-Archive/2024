# Agent 聊天历史压缩方案设计

## 一、需求概述

### 1.1 核心目标

基于消息格式 `<message role="user"></message>` 实现智能聊天历史管理：

1. **保留最后 N 条消息完整**：最新的对话保持原始状态
2. **历史消息压缩为摘要**：用摘要替代原始多轮对话
3. **告知 AI 已压缩**：通过 XML 标记让 AI 知道这是压缩内容
4. **支持完整内容召回**：通过 RefID 可以读取原始内容
5. **关键信息提取**：压缩时提取主题、摘要、关键点
6. **增量压缩**：只处理新增超出阈值的消息，不重复处理历史

### 1.2 消息格式

```xml
<message role="user">用户消息内容</message>
<message role="assistant">AI 回复内容</message>
```

---

## 二、方案设计

### 2.1 单层增量压缩

采用**单层增量压缩**策略：压缩块一旦创建就固定不变，只压缩活跃区域超出阈值的部分。

```
消息时间线（从早到晚）：
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
│  [ID 1-10]   │  [ID 11-20]   │  [ID 21-28]   │
│  压缩块1      │  压缩块2       │  活跃消息      │
│  (摘要)       │  (摘要)        │  (完整保留)    │
└──────────────┴────────────────┴───────────────┘
   Block 1        Block 2         ActiveMsgs
   ↑ 永远不动      ↑ 永远不动       ↑ 动态增长
```

### 2.2 压缩块 XML 格式

```xml
<compressed_history ref="block_1" range="1-10">
  <topic>Python 异步编程</topic>
  <summary>用户学习 async/await 语法和 asyncio 库的使用方法...</summary>
  <key_points>
    <item>async 用于定义协程函数</item>
    <item>await 用于等待协程完成</item>
    <item>asyncio.create_task() 创建后台任务</item>
  </key_points>
  <hint>如需完整内容，可通过 RefID 1-10 召回</hint>
</compressed_history>
```

### 2.3 配置参数

```csharp
public class ChatConfig
{
    public int ActiveRounds { get; set; } = 5;       // 活跃轮数（达到此值触发压缩）
    public int BufferRounds { get; set; } = 3;       // 缓冲轮数（保留不压缩）
    public int MaxMessageLength { get; set; } = 2000; // 单条消息最大长度
}
```

> 注：1轮 = 1条用户消息 + 1条AI消息 = 2条消息

---

## 三、增量压缩机制

### 3.1 核心概念

| 概念 | 说明 |
|------|------|
| 压缩块 (CompressedBlock) | 包含摘要的压缩单位，一旦创建永远不变 |
| 活跃起始ID | 压缩游标，之前的消息都已被压缩，由压缩块推断 |
| 活跃消息 | 从活跃起始ID到最新消息，完整保留在数据库 |

### 3.2 轮次配置

| 概念 | 说明 |
|------|------|
| 活跃轮数 | 可配置，触发压缩的阈值（如5轮） |
| 缓冲轮数 | 保留不压缩的轮数（如3轮） |
| 压缩轮数 | 每次压缩的数量 = 活跃轮数 - 缓冲轮数 |

### 3.3 详细示例

**配置**：
- 活跃轮数 = 5（达到5轮触发压缩）
- 缓冲轮数 = 3（保留最后3轮不压缩）

**示例1：初始状态（10条消息 = 5轮）**

```
消息列表：
[1] user: 你好
[2] assistant: 你好
[3] user: 什么是Python
[4] assistant: Python是...
[5] user: 怎么安装
[6] assistant: 可以用pip
[7] user: 教我写代码
[8] assistant: 好的
[9] user: 谢谢
[10] assistant: 不客气

达到5轮，触发压缩：
压缩轮数 = 5 - 3 = 2轮（4条消息）

压缩后：
┌─────────────────────┬─────────────────────────────┐
│ 压缩块 (ID 1-4)     │ 活跃消息 (ID 5-10)         │
│ user: 你好...        │ user: 怎么安装              │
│ assistant: 你好...   │ assistant: 可以用pip        │
│ user: 什么是Python   │ user: 教我写代码            │
│ assistant: Python是  │ assistant: 好的             │
│                     │ user: 谢谢                  │
│                     │ assistant: 不客气           │
└─────────────────────┴─────────────────────────────┘
```

**示例2：继续对话（未达阈值）**

```
添加消息11, 12：
[11] user: 再见
[12] assistant: 再见

活跃轮数 = (12 - 5 + 1) / 2 = 4轮 < 5，不压缩

结果：
┌─────────────────────┬───────────────────────────────────┐
│ 压缩块 (ID 1-4)     │ 活跃消息 (ID 5-12)              │
│ ...                 │ [原5-10] + [11]user: 再见       │
│                     │ [12]assistant: 再见             │
└─────────────────────┴───────────────────────────────────┘
```

**示例3：达到阈值触发压缩**

```
继续添加消息13, 14：
[13] user: 明天见
[14] assistant: 明天见

活跃轮数 = (14 - 5 + 1) / 2 = 5轮 = 阈值，触发压缩

需要压缩 = ID 5-14（共10条 = 5轮）
压缩轮数 = 5 - 3 = 2轮 = 4条（ID 5,6,7,8）

压缩逻辑：
1. 读取压缩块内容：ID 1-4
2. 读取需压缩的活跃消息：ID 5,6,7,8
3. 合并：ID 1-8 一起重新压缩
4. 活跃消息：ID 9-14

压缩后：
┌─────────────────────┬───────────────────┐
│ 压缩块 (ID 1-8)     │ 活跃消息          │
│ (重新压缩)          │ (ID 9-14)        │
│ user: 你好...       │ user: 谢谢        │
│ assistant: 你好...  │ assistant: 不客气  │
│ user: 什么是Python  │ user: 再见        │
│ assistant: Python是 │ assistant: 再见   │
│ user: 怎么安装      │ user: 明天见      │
│ assistant: 可以用pip│ assistant: 明天见 │
└─────────────────────┴───────────────────┘
```

**示例4：继续对话**

```
继续添加消息15, 16：
[15] user: 下次见
[16] assistant: 下次见

活跃轮数 = (16 - 9 + 1) / 2 = 4轮 < 5，不压缩

结果：
┌─────────────────────┬───────────────────────────────┐
│ 压缩块 (ID 1-8)     │ 活跃消息 (ID 9-16)           │
│ ...                 │ [原9-14] + [15]user: 下次见  │
│                     │ [16]assistant: 下次见        │
└─────────────────────┴───────────────────────────────┘
```

**示例5：再次触发压缩**

```
继续添加消息17, 18：
[17] user: 好的
[18] assistant: 好的

活跃轮数 = (18 - 9 + 1) / 2 = 5轮 = 阈值，触发压缩

需要压缩 = ID 9-18（共10条 = 5轮）
压缩轮数 = 5 - 3 = 2轮 = 4条（ID 9,10,11,12）

压缩逻辑：
1. 读取压缩块内容：ID 1-8
2. 读取需压缩的活跃消息：ID 9,10,11,12
3. 合并：ID 1-12 一起重新压缩
4. 活跃消息：ID 13-18

压缩后：
┌─────────────────────┬───────────────────┐
│ 压缩块 (ID 1-12)    │ 活跃消息          │
│ (重新压缩)          │ (ID 13-18)       │
│ (包含所有历史)      │                   │
└─────────────────────┴───────────────────┘
```

✅ 永远只有一个压缩块
✅ 早期上下文永不丢失
✅ 每次触发压缩时合并重新压缩

### 3.4 伪代码

```csharp
class ChatHistoryManager
{
    // 配置（按轮次）
    ActiveRounds = 5    // 活跃轮数（达到此值触发压缩）
    BufferRounds = 3    // 缓冲轮数（保留不压缩）

    // 状态
    compressedBlock = null    // 当前压缩块（只有一个）

    // 方法：获取活跃起始ID
    func getActiveStartId():
        if compressedBlock == null:
            return 1
        return compressedBlock.EndMsgId + 1

    // 方法：添加消息
    func addMessage(msg):
        // 1. 消息存入数据库（由外部处理）
        // 2. 计算活跃轮数
        activeStartId = getActiveStartId()
        activeCount = msg.id - activeStartId + 1
        activeRounds = activeCount / 2

        // 3. 未达阈值，直接返回（活跃消息已在数据库）
        if activeRounds < ActiveRounds:
            return

        // 4. 达到阈值，触发压缩
        // 4.1 计算需要压缩的消息范围
        toCompressRounds = activeRounds - BufferRounds
        toCompressCount = toCompressRounds * 2
        toCompressStartId = activeStartId

        // 4.2 收集待压缩内容
        var toCompress = []

        // 如果有压缩块，读取其覆盖的原始消息
        if compressedBlock != null:
            var blockMsgs = 查询消息(compressedBlock.StartMsgId, compressedBlock.EndMsgId)
            toCompress.addRange(blockMsgs)

        // 读取需要压缩的活跃消息
        var activeToCompress = 查询消息(toCompressStartId, toCompressCount)
        toCompress.addRange(activeToCompress)

        // 4.3 调用 AI 压缩
        var newBlock = compressWithAI(toCompress)

        // 4.4 更新压缩块
        compressedBlock = newBlock

        // 4.5 保存压缩块（持久化）
        await SaveAsync(compressedBlock)

    // 方法：获取上下文（给 AI 用）
    func getContext():
        result = []

        // 1. 压缩块转 XML
        if compressedBlock != null:
            result.add(compressedBlock.toXml())

        // 2. 活跃消息（从数据库查询）
        activeStartId = getActiveStartId()
        activeMsgs = 查询消息(ID >= activeStartId)
        for msg in activeMsgs:
            result.add(msg.toXml())

        return result

    // 方法：压缩（调用 AI）
    func compressWithAI(messages):
        prompt = """
        压缩以下对话（按轮次），输出 JSON {
            "topic": "一句话主题",
            "summary": "2-4句摘要",
            "key_points": ["关键点1", "关键点2", "关键点3"],
            "hint": "如需完整内容，可通过 RefID [{startId}-{endId}] 召回"
        }：
        {对话内容}
        """

        response = callAI(prompt)
        return parseResponse(response)
```

---

## 四、持久化设计

### 4.1 持久化什么？

**Messages 不需要持久化**，因为它们本身存储在数据库中。只需要持久化：

1. **CompressedBlock** - 当前压缩块（只有一个）

> 注意：最后一条消息 ID 可通过查询数据库 `MAX(ID)` 获取

### 4.2 数据模型

```csharp
public class CompressedBlock
{
    public int StartMsgId { get; set; }        // 起始消息ID
    public int EndMsgId { get; set; }          // 结束消息ID
    public string Topic { get; set; } = "";     // 主题
    public string Summary { get; set; } = "";   // 摘要
    public List<string> KeyPoints { get; set; } = new();  // 关键点（数组）
    public string Hint { get; set; } = "";       // 召回提示
    public DateTime CompressedAt { get; set; }  // 压缩时间

    public string ToXml()
    {
        var blockId = $"block_{(StartMsgId - 1) / 10 + 1}";  // 每10条一个block
        var keyPointsXml = string.Join("\n",
            KeyPoints.Select(k => $"    <item>{k}</item>"));

        return $@"<compressed_history ref='{blockId}' range='{StartMsgId}-{EndMsgId}'>
  <topic>{Topic}</topic>
  <summary>{Summary}</summary>
  <key_points>
{keyPointsXml}
  </key_points>
  <hint>{Hint}</hint>
</compressed_history>";
    }
}

public class ConversationState
{
    public CompressedBlock? CompressedBlock { get; set; }  // 当前压缩块（只有一个）

    // 推断活跃起始ID
    public int GetActiveStartId()
    {
        if (CompressedBlock == null)
            return 1;
        return CompressedBlock.EndMsgId + 1;
    }
}
```

### 4.3 加载与保存

```csharp
// 加载（会话恢复时）
public async Task<ConversationState> LoadAsync(string sessionId)
{
    var block = await _db.GetCompressedBlockAsync(sessionId);
    return new ConversationState { CompressedBlock = block };
}

// 保存（压缩后）
public async Task SaveAsync(string sessionId, ConversationState state)
{
    await _db.SaveCompressedBlockAsync(sessionId, state.CompressedBlock);
}

// 获取最后消息ID（用于计算活跃轮数）
public async Task<int> GetLastMessageIdAsync(string sessionId)
{
    var lastMsg = await _db.GetLastMessageAsync(sessionId);
    return lastMsg?.Id ?? 0;
}
```

---

## 五、召回机制

### 5.1 AI 召回

当 AI 需要历史消息的完整内容时，可以调用工具获取：

```csharp
// AI 可调用的召回工具
public async Task<string> RecallByRefIdAsync(int startId, int endId)
{
    var messages = await _db.GetMessagesByIdRangeAsync(startId, endId);
    return string.Join("\n\n", messages.Select(m =>
        $"<message role='{m.Role}'>{m.Content}</message>"));
}
```

### 5.2 使用场景

AI 可以根据 `<key_points>` 决定是否需要召回原文：

```xml
<!-- AI 看到压缩块 -->
<compressed_history ref="block_1" range="1-10">
  <topic>Python 异步编程</topic>
  <key_points>
    <item>async 定义协程</item>
    <item>await 等待协程</item>
  </key_points>
</compressed_history>

<!-- 如果 AI 需要查看具体代码示例 -->
<!-- 调用 RecallByRefId(1, 10) 获取完整对话 -->
```

---

## 六、单条消息过长处理

### 6.1 处理策略

当单条消息内容超过 `MaxMessageLength`（默认 2000 字符）时：

1. 调用 AI 生成压缩摘要
2. 保留原文的前 500 字符
3. 添加压缩标记

### 6.2 压缩结果

```
原文（3000字）：
async/await 是 Python 3.5+ 引入的异步编程语法。async 用于定义协程...

压缩后：
async/await 是 Python 3.5+ 引入的异步编程语法。async 用于定义协程...

[此消息已被压缩，原长度: 3000 字]
```

---

## 七、使用示例

```csharp
// 1. 初始化（从数据库加载状态）
var state = await LoadAsync(sessionId);
var manager = new ChatHistoryManager(config, state);

// 2. 添加对话
await manager.AddMessageAsync(new Message { Role = "user", Content = "什么是 async/await？" });
await manager.AddMessageAsync(new Message { Role = "assistant", Content = "async/await 是..." });

// 保存状态
await SaveAsync(sessionId, manager.State);

// 3. 获取上下文（发送给 AI）
var context = manager.GetContext();

// 4. 召回完整内容
var original = await RecallByRefIdAsync(1, 10);
```

---

## 八、方案优势

| 特性 | 说明 |
|------|------|
| **按轮计算** | 按轮次（user+assistant）对等计算，更直观 |
| **永远一个压缩块** | 触发压缩时合并重新压缩，永不丢失早期上下文 |
| **结构化关键点** | KeyPoints 数组让 AI 更好理解内容 |
| **可召回** | 通过 RefID 可获取完整对话 |
| **数据库无关** | Messages 存数据库，只需持久化单个压缩块 |
| **状态推断** | 活跃起始ID由压缩块推断，无需额外存储 |

---

*文档版本: v4.0*
