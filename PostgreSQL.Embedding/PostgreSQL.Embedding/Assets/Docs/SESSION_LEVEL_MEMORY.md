# 会话级记忆提取提示词

## 角色定义

你是一个会话级记忆管理器。你的职责从整个会话（Conversation）中提取「跨越多个 Run 仍有价值」的信息。这些信息需要在会话结束后保留，并可能晋升为长期记忆。

## 输入数据

- 本次会话的所有 Run 记录（按时间顺序）
- 每个 Run 的提取结果（Run 级记忆）
- 会话起始时的问题/目标
- 用户的背景信息（如果有）

## 提取维度

### 维度一：会话主题

提取本次会话的核心主题：

- 用户最初的问题或目标
- 会话过程中讨论的主要话题
- 话题的演进脉络
- 是否产生了结论

### 维度二：项目背景

提取与本次会话相关的上下文信息：

- 用户正在做什么项目
- 项目的当前状态
- 涉及的技术栈或工具
- 关键的时间节点或里程碑

### 维度三：决策记录

提取会话中产生的关键决策：

- 技术选型决策
- 方案选择决策
- 优先级排序
- 确认的需求或规格

### 维度四：待推进事项

提取需要后续跟进的事项：

- 明确承诺的待办
- 发现的待探索方向
- 需要用户确认的事项
- 需要等待的前置条件

### 维度五：用户信息

从会话中提取或更新用户相关信息：

- 表达的技术偏好
- 提及的工作背景
- 明确的个人特征
- 新的认知或知识

### 维度六：会话元信息

记录会话本身的状态：

- 会话类型（咨询、任务、闲聊等）
- 涉及的领域
- 复杂程度评估
- 是否有后续会话的可能

## 输出格式

请严格按照以下 JSON 格式输出：

```json
{
  "conversation_id": "会话的唯一标识",
  "session_theme": {
    "initial_goal": "用户最初的问题或目标",
    "main_topics": ["主要话题1", "主要话题2"],
    "topic_evolution": "话题演进脉络简述",
    "has_conclusion": true或false,
    "conclusion_summary": "结论摘要（如果有）"
  },
  "project_context": {
    "project_name": "项目名称或 null",
    "project_status": "初始 | 进行中 | 收尾 | 完成",
    "tech_stack": ["技术栈列表"],
    "milestones": ["关键里程碑"]
  },
  "decisions": [
    {
      "type": "技术选型 | 方案选择 | 优先级 | 需求确认",
      "description": "决策描述",
      "rationale": "决策理由",
      "participants": ["涉及的 Run ID"]
    }
  ],
  "pending_matters": [
    {
      "type": "待办 | 待确认 | 待探索 | 等待",
      "description": "事项描述",
      "from_run": "来源 Run ID",
      "priority": "高 | 中 | 低",
      "deadline": "截止日期或 null"
    }
  ],
  "user_info_updates": {
    "preferences": [
      {
        "dimension": "偏好维度",
        "value": "偏好内容",
        "evidence": "证据来源"
      }
    ],
    "background": [
      {
        "dimension": "背景维度",
        "value": "背景内容",
        "evidence": "证据来源"
      }
    ]
  },
  "session_metadata": {
    "session_type": "咨询 | 任务 | 混合 | 闲聊",
    "domain": ["涉及的领域"],
    "complexity": "简单 | 中等 | 复杂",
    "has_follow_up": true或false,
    "estimated_follow_up_time": "预计后续会话时间"
  },
  "promote_to_application": [
    {
      "type": "fact | preference | knowledge",
      "content": "应该晋升到应用级的内容",
      "reason": "为什么应该晋升"
    }
  ]
}

重要提醒
关注「跨 Run 的连续性」：一个 Run 的信息可能需要结合其他 Run 才有意义
区分「已完成」和「待完成」：确保待办事项准确无误
识别「临时信息」：某些信息只在本次会话中有用，不要提取
标记高价值信息：有可能晋升到应用级的信息需要特别标记

### 2.2 提取示例

**输入场景**：用户与 Agent 讨论并设计了一个 Agent 记忆系统的架构

**会话概述**：

- Run 1：用户提出想要构建个人 Agent 的记忆系统
- Run 2：讨论了三层架构设计
- Run 3：确定了目录结构方案
- Run 4：明确了记忆更新策略
- Run 5：讨论了具体的实现细节

**提取结果**：

```json
{
  "conversation_id": "conv_20260220_001",
  "session_theme": {
    "initial_goal": "设计个人 Agent 的沙盒目录结构与记忆系统",
    "main_topics": [
      "三层架构设计",
      "目录结构规划",
      "记忆更新策略",
      "记忆维度定义"
    ],
    "topic_evolution": "从宏观架构到具体实现的演进：先确定三层架构，再细化目录结构，接着明确更新机制，最后讨论实现细节",
    "has_conclusion": true,
    "conclusion_summary": "确定采用三层架构（应用层、会话层、任务层），使用 Markdown 存储，每日定时合并，人工审核 SOUL 更新"
  },
  "project_context": {
    "project_name": "Agent 沙盒记忆系统",
    "project_status": "初始",
    "tech_stack": ["Markdown", "SQLite（可选索引）"],
    "milestones": [
      "已完成：架构设计与目录结构",
      "待完成：实现记忆提取提示词",
      "待完成：设计合并流程"
    ]
  },
  "decisions": [
    {
      "type": "架构设计",
      "description": "采用三层架构：应用层（soul）、会话层（memory）、任务层（short_term）",
      "rationale": "不同生命周期、不同更新频率的数据应该分离存储",
      "participants": ["run_001", "run_002"]
    },
    {
      "type": "更新策略",
      "description": "短期记忆实时写入，中期记忆每日合并，长期记忆人工审核",
      "rationale": "确保质量控制，避免单次任务污染长期状态",
      "participants": ["run_004"]
    }
  ],
  "pending_matters": [
    {
      "type": "待完成",
      "description": "实现短期记忆提取提示词",
      "from_run": "run_005",
      "priority": "高",
      "deadline": null
    },
    {
      "type": "待完成",
      "description": "实现长期记忆合并提示词",
      "from_run": "run_005",
      "priority": "高",
      "deadline": null
    },
    {
      "type": "待完成",
      "description": "设计会话总结生成流程",
      "from_run": "run_005",
      "priority": "中",
      "deadline": null
    }
  ],
  "user_info_updates": {
    "preferences": [
      {
        "dimension": "回答风格",
        "value": "喜欢简洁直接的回答",
        "evidence": "用户在讨论中多次提到「简洁」「直接」"
      },
      {
        "dimension": "架构风格",
        "value": "偏好分层架构设计",
        "evidence": "用户主动提出三层架构的概念"
      }
    ],
    "background": [
      {
        "dimension": "技术背景",
        "value": "熟悉后端开发",
        "evidence": "用户使用技术术语，讨论了后端架构"
      },
      {
        "dimension": "当前目标",
        "value": "构建个人 Agent 的完整记忆系统",
        "evidence": "用户明确表达这是本次会话的核心目标"
      }
    ]
  },
  "session_metadata": {
    "session_type": "咨询+任务",
    "domain": ["系统架构", "AI 应用"],
    "complexity": "中等",
    "has_follow_up": true,
    "estimated_follow_up_time": "1-2周后开始实现"
  },
  "promote_to_application": [
    {
      "type": "preference",
      "content": "用户偏好简洁直接的回答风格",
      "reason": "这是用户的稳定偏好，适用于所有会话"
    },
    {
      "type": "knowledge",
      "content": "用户对 Agent 记忆系统有明确需求和初步构想",
      "reason": "这是用户的核心目标，应该纳入长期规划"
    }
  ]
}
三、两级提示词的协作机制
3.1 数据流动
用户输入
    │
    ▼
┌─────────────────┐
│  Run 级提取器   │ ← 每次 Run 完成后触发
│                 │
│  输出：         │
│  • 即时约束     │
│  • 关键实体    │
│  • 执行发现    │
│  • 待办事项    │
└────────┬────────┘
         │
         │ 异步写入
         ▼
┌─────────────────┐
│  Run 记忆存储   │
│  short_term.md │
└─────────────────┘
         │
    每日定时触发
         │
         ▼
┌─────────────────┐
│ 会话级提取器    │ ← 每次会话结束时 或 每日合并时触发
│                 │
│ 输出：          │
│ • 会话主题      │
│ • 决策记录      │
│ • 待推进事项    │
│ • 用户更新      │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ 会话记忆存储    │
│ memory.md       │
└─────────────────┘
         │
   每周/里程碑
         ▼
┌─────────────────┐
│ 应用级晋升审核  │
└─────────────────┘
3.2 关键区别总结
维度	Run 级提取	会话级提取
触发时机	每次 Run 完成后	会话结束或每日合并
关注焦点	本次任务的执行	跨 Run 的整体脉络
信息粒度	离散的动作和产出	连续的上下文和决策
保留周期	任务完成可压缩	会话结束才归档
晋升目标	会话级 memory	应用级 soul
四、使用建议
4.1 实施建议
在实际使用时，建议采用以下策略：

Run 级提取：作为 Agent 执行流程的一部分，在每次工具调用或任务完成后自动触发。可以将提取结果追加到 short_term.md，同时更新 SQLite 索引。

会话级提取：可以在以下时机触发：会话明确结束时、用户离开超过一定时间后、或者每日定时任务中批量处理所有未提取的会话。

4.2 调优建议
如果发现提取质量不够好，可以从以下角度调整：

增加具体的示例（few-shot）让提取器理解期望的格式
调整提取维度的权重，强调某些维度的重要性
增加边界情况的处理指导
定期审查提取结果，补充遗漏的信息类型
这套提示词设计完成后，可以直接嵌入您的 Agent 系统中使用。如果您需要进一步调整某个维度的提取逻辑，或者需要补充具体的示例，我可以继续优化。