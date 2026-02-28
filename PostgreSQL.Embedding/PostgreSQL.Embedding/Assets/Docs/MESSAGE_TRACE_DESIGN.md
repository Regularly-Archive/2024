# 消息 Trace 存储设计

## 概述

消息 Trace 用于存储消息的推理过程、工具调用、执行计划和产物。这些信息与消息关联，用于追踪和回溯 AI 对话的执行过程。

## 数据模型

### 1. 推理过程 (chat_message_reasonings)

| 字段 | 类型 | 说明 |
|------|------|------|
| id | bigint | 主键 |
| run_id | varchar(36) | 运行ID，用于跨消息追踪同一轮执行 |
| message_id | bigint | 关联的消息ID |
| content | text | 推理内容 |

### 2. 工具调用 (chat_message_tool_calls)

| 字段 | 类型 | 说明 |
|------|------|------|
| id | bigint | 主键 |
| run_id | varchar(36) | 运行ID |
| message_id | bigint | 关联的消息ID |
| sort | int | 排序 |
| name | varchar(255) | 工具名称 |
| input | jsonb | 输入参数 |
| output | text | 输出结果 |
| status | int | 状态（0=pending, 1=success, 2=error） |
| duration_ms | bigint | 持续时长（毫秒） |

### 3. 执行计划 (chat_message_plans)

| 字段 | 类型 | 说明 |
|------|------|------|
| id | bigint | 主键 |
| run_id | varchar(36) | 运行ID |
| message_id | bigint | 关联的消息ID |
| plan_id | bigint | 计划ID（唯一标识，用于更新） |
| title | text | 计划/子任务标题 |
| description | text | 计划/子任务描述 |
| output | text | 计划/子任务输出 |
| status | int | 计划/子任务状态（使用枚举） |

### 4. 产物 (chat_message_artifacts)

| 字段 | 类型 | 说明 |
|------|------|------|
| id | bigint | 主键 |
| run_id | varchar(36) | 运行ID |
| message_id | bigint | 关联的消息ID |
| file_id | varchar(255) | 产物ID |
| file_name | varchar(255) | 文件名 |
| file_type | int | 文件类型（枚举值：code=1, image=2, document=3, audio=4, video=5） |
| url | varchar(1000) | 访问URL |
| can_preview | boolean | 是否可预览 |
| can_download | boolean | 是否可下载 |
| file_size | bigint | 文件大小（字节） |

## 关联关系

```
Message (chat_messages)
    │
    ├── Reasoning (chat_message_reasonings)
    │       - message_id → Message.Id
    │       - run_id → 追踪同一轮执行
    │
    ├── ToolCall (chat_message_tool_calls)
    │       - message_id → Message.Id
    │       - run_id → 追踪同一轮执行
    │
    ├── Plan (chat_message_plans)
    │       - message_id → Message.Id
    │       - run_id → 追踪同一轮执行
    │
    └── Artifact (chat_message_artifacts)
            - message_id → Message.Id
            - run_id → 追踪同一轮执行
```

## 索引设计

建议创建以下索引以优化查询性能：

```sql
-- 按消息ID查询推理
CREATE INDEX idx_reasonings_message_id ON chat_message_reasonings(message_id);
CREATE INDEX idx_reasonings_run_id ON chat_message_reasonings(run_id);

-- 按消息ID查询工具调用
CREATE INDEX idx_tool_calls_message_id ON chat_message_tool_calls(message_id);
CREATE INDEX idx_tool_calls_run_id ON chat_message_tool_calls(run_id);

-- 按消息ID查询计划（唯一索引）
CREATE UNIQUE INDEX idx_plans_unique ON chat_message_plans(message_id, run_id, plan_id);

-- 按消息ID查询产物
CREATE INDEX idx_artifacts_message_id ON chat_message_artifacts(message_id);
CREATE INDEX idx_artifacts_run_id ON chat_message_artifacts(run_id);
```

## 使用场景

1. **调试**：查看 AI 执行过程中的推理链和工具调用
2. **审计**：追踪用户的请求经过哪些步骤完成
3. **展示**：在界面中展示推理过程、执行计划、生成的代码等
4. **性能分析**：通过 duration_ms 分析工具调用性能

## 文件列表

| 文件 | 说明 |
|------|------|
| Domain/Entities/ChatMessageReasoning.cs | 推理过程实体 |
| Domain/Entities/ChatMessageToolCall.cs | 工具调用实体 |
| Domain/Entities/ChatMessagePlan.cs | 执行计划实体 |
| Domain/Entities/ChatMessageArtifact.cs | 产物实体 |
