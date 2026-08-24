# Agent Loop 设计研究资料

> 整理时间：2026-07-13
> 目的：为 InsightaAI Agent 的 AgentLoop 提取提供设计参考

---

## 1. Anthropic - "Building Effective Agents"

**链接**: https://www.anthropic.com/research/building-effective-agents

**核心观点**:

- 区分 **Workflow**（预定义编排）和 **Agent**（LLM 自主决策循环）
- Agent 的本质：LLM 在一个 **循环** 中使用工具，自主决定何时停止
- 提出 5 种 Workflow 模式：Prompt Chaining、Routing、Parallelization、Orchestrator-Workers、Evaluator-Optimizer
- **关键建议**：从简单方案开始，不要过度设计

**Agent Loop 模式**:
```
while not done:
    1. LLM 接收当前上下文
    2. LLM 决定：调用工具 or 返回最终回答
    3. 如果调用工具 → 执行 → 将结果反馈给 LLM → 回到 1
    4. 如果返回回答 → 结束循环
```

**对 InsightaAI 的参考价值**:
- Agent 和 Workflow 的区分 — 我们的 Agent 是纯 Agent 模式
- "Augmented LLM" 作为构建块 — LLM + 检索 + 工具 + 记忆

---

## 2. Simon Willison - "The Simplest Agent Loop"

**链接**: https://simonwillison.net/2024/Oct/11/the-simplest-thing-that-can-possibly-work/

**核心观点**:

- "An agent is simply an LLM in a loop with access to tools"
- 所有复杂框架最终都归结为这个简单模式
- 强调最小可行实现

**最简 Agent Loop 伪代码**:
```python
messages = [{"role": "user", "content": "prompt"}]

while True:
    response = llm.call(messages, tools)
    messages.append(response)

    if response.has_tool_calls():
        for tool_call in response.tool_calls:
            result = execute_tool(tool_call)
            messages.append(result)
    else:
        break  # 没有工具调用，结束
```

**对 InsightaAI 的参考价值**:
- 我们的 `RunStreamAsync` 本质上就是这个模式 + hooks/events/压缩
- 提取 AgentLoop 时，核心就是这 6 行逻辑

---

## 3. Andrew Ng - 四大 Agentic 设计模式

**来源**: DeepLearning.AI 讲座、Sequoia Capital 演讲

**四种模式**:

| 模式 | 描述 |
|------|------|
| **Reflection** | LLM 审查并批评自己的输出，然后修正 |
| **Tool Use** | Agent 调用外部工具（代码执行、搜索、API） |
| **Planning** | Agent 将复杂任务分解为子任务 |
| **Multi-Agent** | 多个专业化 Agent 协作 |

**对 InsightaAI 的参考价值**:
- Tool Use 模式 — 我们已实现
- Reflection 模式 — 可以考虑在 AgentLoop 中加入自我评估步骤
- Multi-Agent — 子 Agent 作为工具的架构讨论（之前的对话中提到过）

---

## 4. ReAct 论文 - "Synergizing Reasoning and Acting in Language Models"

**作者**: Shunyu Yao et al. (Princeton/Google Research)
**发表**: ICLR 2023
**论文**: https://arxiv.org/abs/2210.03629

**核心思想**:

- 交替进行 Chain-of-Thought 推理和工具调用
- 循环：**Thought → Action → Observation → Thought → ...**
- 推理轨迹帮助 Agent 规划，行动结果帮助 Agent 调整

**ReAct Loop**:
```
Thought: 我需要搜索北京的天气
Action: search("Beijing weather today")
Observation: 北京今天晴，25°C
Thought: 我已经获得了天气信息，可以回答用户了
Action: finish("北京今天晴天，25°C")
```

**对 InsightaAI 的参考价值**:
- Thought（推理）和 Action（工具调用）的交织 — 我们通过 streaming 事件实现了这一点
- Observation（观察结果）的反馈循环 — 我们的 ToolResult 机制

---

## 5. LangGraph - 图状态机 Agent 架构

**链接**: https://langchain-ai.github.io/langgraph/
**GitHub**: https://github.com/langchain-ai/langgraph

**核心设计**:

- 用 **有向图** 建模 Agent 循环：节点 = 动作，边 = 转换
- 条件边实现循环：`Agent → should_continue? → Tool → Agent` 或 `Agent → END`
- 内置持久化/检查点、Human-in-the-loop 中断、流式支持

**典型 Agent Loop 图**:
```
START → Agent (LLM call) → should_continue?
    ├── Yes (tool call) → Tool Node → Agent (loop back)
    └── No (done) → END
```

**状态管理**:
```python
class AgentState(TypedDict):
    messages: list[BaseMessage]
    # 可扩展自定义状态
```

**对 InsightaAI 的参考价值**:
- **状态管理** — 我们的 `IAgentContext` 讨论正是这个思路
- **条件边** — 我们的 `toolCalls.Length == 0` 判断就是条件边
- **检查点/持久化** — 可以考虑支持会话恢复

---

## 6. OpenAI Agents SDK - Agent Loop + Handoff

**链接**: https://openai.github.io/openai-agents-python/
**GitHub**: https://github.com/openai/openai-agents-python

**核心设计**:

- `Runner` 类编排 Agent Loop
- **Handoff** 机制：Agent 可以将控制权交给另一个 Agent
- 内置 Guardrails（输入/输出校验）和 Tracing

**Agent Loop 流程**:
```
User Input → Agent 1 (LLM call) → [Decision]
    ├── Tool Call → Execute → Loop back
    └── Handoff → Transfer to Agent 2 → Agent 2 (LLM call) → ...
```

**关键代码结构**:
```python
class Agent:
    name: str
    instructions: str
    tools: list[Tool]
    handoffs: list[Agent]

class Runner:
    @staticmethod
    def run_sync(agent, input, max_turns=10):
        # Agent Loop 核心
        while turns < max_turns:
            response = llm.call(messages, tools)
            if response.has_tool_calls():
                # 执行工具，继续循环
            elif response.has_handoff():
                # 切换到目标 Agent
            else:
                return response
```

**对 InsightaAI 的参考价值**:
- **Handoff** — 子 Agent 作为工具的另一种实现方式
- **Runner 分离** — Agent 定义和 Agent 执行分离，正是我们提取 AgentLoop 的思路
- **max_turns** — 我们的 `MaxToolRounds`

---

## 7. Microsoft AutoGen - 多 Agent 对话循环

**链接**: https://microsoft.github.io/autogen/
**GitHub**: https://github.com/microsoft/autogen

**核心设计**:

- **对话式循环**：Agent 轮流发送和接收消息
- **Runtime** 管理循环执行（单线程或分布式）
- 多种编排模式：RoundRobin、Selector、Swarm

**Agent Loop 模式**:
```
while not termination_condition:
    select next agent → agent generates message/tool call
    → process result → check termination → continue
```

**AutoGen 0.4 新架构**:
- 事件驱动、async-first
- 可插拔 Runtime（本地/分布式）
- `BaseAgent` 类，通过 `on_messages` 接口扩展

**对 InsightaAI 的参考价值**:
- **事件驱动** — 我们的 `IAsyncEnumerable<AgentEvent>` 正是这个模式
- **终止策略** — MaxTurns、文本匹配、自定义函数
- **分布式 Runtime** — 未来扩展方向

---

## 8. CrewAI - Agent-Task-Crew 编排

**链接**: https://docs.crewai.com
**GitHub**: https://github.com/crewAIInc/crewAI

**核心设计**:

- **Crew** = 编排器，管理多个 Agent 和 Task
- **Agent** = 有角色、目标、工具的专业化执行者
- **Task** = 有描述、预期输出、所属 Agent 的工作单元

**Agent 内部循环**:
```
Agent._execute_task()
    while not complete:
        ├─ Format prompt with context/memory
        ├─ LLM call → get response
        ├─ Parse for tool calls
        │    └─ Execute tool → get result
        ├─ Evaluate: task done? (max_iter check)
        └─ Store in memory
```

**编排模式**:
| 模式 | 描述 |
|------|------|
| Sequential | Agent 按顺序执行，结果传递给下一个 |
| Hierarchical | Manager Agent 委派给 Worker Agent |
| Delegation | Agent 可以将子任务委派给其他 Agent |

**对 InsightaAI 的参考价值**:
- **记忆系统** — 短期/长期记忆分离
- **回调函数** — 每步的 hooks（我们已有类似机制）
- **Human-in-the-loop** — 暂停等待人类输入（我们有 ToolHook）

---

## 9. Vercel AI SDK - 流式 Agent Loop

**链接**: https://sdk.vercel.ai
**GitHub**: https://github.com/vercel/ai

**核心设计**:

- `streamText()` / `generateText()` 核心函数
- **`maxSteps`** 参数实现多步 Agent Loop
- 流式传输 + 工具调用一体化

**Agent Loop 设计**:
```
User Input → LLM Call → Tool Execution → LLM Call → ... → Final Response
               ↕ streaming chunks ↕
```

**关键特性**:
- `maxSteps`：自动处理工具调用循环，无需手动管理
- `useChat()` / `useCompletion()`：前端 React hooks
- `StreamData`：在 LLM 流旁边发送自定义数据
- `AbortController`：取消支持

**对 InsightaAI 的参考价值**:
- **maxSteps 抽象** — 用户不需要自己写循环，框架自动处理
- **流式 + 工具调用的统一** — 我们的 streaming 事件机制
- **前端集成** — 事件流设计考虑 UI 消费

---

## 10. Semantic Kernel - .NET Agent 架构

**链接**: https://learn.microsoft.com/en-us/semantic-kernel
**GitHub**: https://github.com/microsoft/semantic-kernel

**核心设计**:

- .NET 生态的 AI Agent SDK
- **Planner** 模式：Stepwise（ReAct 风格）、Handlebars、Function Calling
- **Agent 框架**：ChatCompletionAgent、OpenAIAssistantAgent

**Agent Loop（Stepwise Planner）**:
```
Step 1: LLM 推理 → 决定调用哪个函数
Step 2: 执行函数 → 获取结果
Step 3: 结果反馈给 LLM → 继续推理
Step 4: 重复直到 LLM 认为任务完成
```

**Process Framework**（新）:
- 结构化工作流
- 步骤 + 事件驱动
- 可组合、可嵌套

**对 InsightaAI 的参考价值**:
- **同为 .NET 生态** — 可以参考其 C# 设计模式
- **Planner 分离** — 规划和执行分离
- **Process Framework** — 事件驱动的步骤编排

---

## 11. LlamaIndex - Workflow API Agent 架构

**链接**: https://docs.llamaindex.ai
**GitHub**: https://github.com/run-llama/llama_index

**核心设计**:

- **AgentRunner / AgentWorker**：编排 ReAct 循环
- **Workflow API**（新）：事件驱动的步骤编排
- 支持 ReAct Agent 和 Function Calling Agent

**Workflow 模式**:
```
Step 1: 接收输入 → 发出事件
Step 2: 监听事件 → 执行 LLM 调用 → 发出结果事件
Step 3: 监听结果 → 判断是否需要工具 → 发出工具事件或完成事件
```

**关键特性**:
- 事件驱动：步骤通过 typed events 通信
- 可组合：Workflow 可以嵌套/链式调用
- 显式状态管理：Context 对象在步骤间传递

**对 InsightaAI 的参考价值**:
- **事件驱动步骤** — 与我们的 AgentEvent 流类似
- **Context 对象** — 我们的 IAgentContext 讨论
- **可组合性** — 子 Agent 作为工具

---

## 12. 额外参考：Agent Loop 的关注点分离

**来源**: 多个框架的共同模式总结

### Agent Loop 的三层关注点

```
┌─────────────────────────────────────────┐
│  编排层 (Orchestration)                  │
│  - 循环控制（round/iteration）           │
│  - 终止条件判断                          │
│  - 错误恢复                              │
├─────────────────────────────────────────┤
│  执行层 (Execution)                      │
│  - LLM 调用                             │
│  - 工具执行                              │
│  - 结果处理                              │
├─────────────────────────────────────────┤
│  基础设施层 (Infrastructure)             │
│  - 消息存储                              │
│  - 事件通知                              │
│  - Hook 触发                            │
│  - 上下文压缩                            │
│  - 日志/可观测性                         │
└─────────────────────────────────────────┘
```

### 共同模式

| 模式 | 描述 | 框架 |
|------|------|------|
| **Runner/Executor 分离** | Agent 定义 vs Agent 执行分离 | OpenAI SDK, LlamaIndex |
| **事件驱动** | 循环产生事件，外部消费 | AutoGen, LangGraph, LlamaIndex |
| **状态注入** | 循环不持有状态，通过接口读写 | LangGraph, Semantic Kernel |
| **Hook/Callback** | 在关键点注入自定义逻辑 | CrewAI, OpenAI SDK |
| **最大轮次** | 防止无限循环的安全机制 | 所有框架 |
| **工具结果反馈** | 工具执行结果追加到消息历史 | 所有框架 |

### 关键设计问题

1. **消息管理**：循环是否直接操作消息列表？还是通过抽象层？
2. **事件 vs 回调**：用 `IAsyncEnumerable<AgentEvent>` 还是 `Action<AgentEvent>` 回调？
3. **Hook 触发点**：循环内触发还是外部消费事件后触发？
4. **错误处理**：工具执行失败时重试还是让 LLM 决定？
5. **上下文压缩**：循环内检查还是外部预处理？

---

## 总结：对 InsightaAI AgentLoop 提取的建议

### 核心循环（应该在 AgentLoop 中）
```
for round in 1..MaxToolRounds:
    1. 获取当前消息（通过 IAgentContext）
    2. 调用 LLM（ILlmClient.Streaming）
    3. 转发流事件（yield AgentLlmStreamEvent）
    4. 追加助手消息（IAgentContext.AddMessage）
    5. 检查工具调用
       - 无 → 返回完成事件
       - 有 → 执行工具 → 追加工具结果 → 继续循环
    6. 触发轮次事件（AgentRoundEndEvent）
```

### 不应该在 AgentLoop 中
- System Prompt 构建（Skills/MCP/Memory 注入）
- Hook 的具体实现（只通过接口/回调暴露触发点）
- 消息存储的具体实现（通过 IAgentContext 抽象）
- Session 管理（sessionId 的生成和传递）

### 可选抽象
- `IAgentContext`：消息读写 + 上下文压缩 + token 统计
- `IAgentEventSink`：事件消费接口（替代直接 yield）
- 工具执行可以内联在循环中，也可以通过 ToolCallExecutor 分离

---

*本文档供设计讨论使用，后续根据实现情况更新。*
