# Multi-Agent Framework 设计文档

## 一、设计目标

构建一个分层的 Agent 框架，自底向上包含：
1. **LLM 抽象层** - 统一的 LLM 调用接口和事件流
2. **Agent 运行时** - 单个 Agent 的执行引擎
3. **Multi-Agent 编排** - 多 Agent 协作和任务调度

---

## 二、Layer 1: LLM 抽象层 (LlmClient)

### 2.1 核心接口

```typescript
// ============================================================
// 类型定义
// ============================================================

/** 消息角色 */
type MessageRole = 'system' | 'user' | 'assistant' | 'toolResult';

/** 内容块类型 */
type ContentBlockType = 'text' | 'toolCall' | 'toolResult' | 'image' | 'thinking';

/** 文本块 */
interface TextBlock {
  type: 'text';
  text: string;
}

/** 工具调用块 */
interface ToolCallBlock {
  type: 'toolCall';
  id: string;
  name: string;
  arguments: Record<string, unknown>;
}

/** 思考过程块 (Claude extended thinking) */
interface ThinkingBlock {
  type: 'thinking';
  thinking: string;
}

/** 图片块 */
interface ImageBlock {
  type: 'image';
  source: { type: 'base64'; mediaType: string; data: string };
}

type ContentBlock = TextBlock | ToolCallBlock | ThinkingBlock | ImageBlock;

/** 消息 */
interface Message {
  role: MessageRole;
  content: ContentBlock[] | string;
  toolCallId?: string;    // toolResult 时使用
  toolName?: string;      // toolResult 时使用
  timestamp?: number;
}

/** 工具定义 */
interface ToolDefinition {
  name: string;
  description: string;
  parameters: JsonSchema;  // JSON Schema 格式
}

/** LLM 请求配置 */
interface LlmRequest {
  model: string;
  messages: Message[];
  tools?: ToolDefinition[];
  temperature?: number;
  maxTokens?: number;
  stream?: boolean;
  // Provider 特定配置
  providerOptions?: Record<string, unknown>;
}

/** Token 用量 */
interface TokenUsage {
  input: number;
  output: number;
  cacheRead?: number;
  cacheWrite?: number;
  totalCost?: number;
}

// ============================================================
// 事件流定义 (统一事件模型)
// ============================================================

/** 流式事件类型 */
type StreamEventType =
  | 'start'           // 流开始
  | 'text_start'      // 文本生成开始
  | 'text_delta'      // 文本增量
  | 'text_end'        // 文本生成结束
  | 'thinking_start'  // 思考开始
  | 'thinking_delta'  // 思考增量
  | 'thinking_end'    // 思考结束
  | 'toolcall_start'  // 工具调用开始
  | 'toolcall_delta'  // 工具参数增量
  | 'toolcall_end'    // 工具调用结束
  | 'usage'           // Token 用量报告
  | 'done'            // 流完成
  | 'error';          // 错误

/** 流式事件基类 */
interface StreamEventBase {
  type: StreamEventType;
  timestamp: number;
}

/** 各事件类型的具体定义 */
interface StartEvent extends StreamEventBase {
  type: 'start';
  model: string;
  provider: string;
}

interface TextStartEvent extends StreamEventBase {
  type: 'text_start';
  contentIndex: number;
}

interface TextDeltaEvent extends StreamEventBase {
  type: 'text_delta';
  delta: string;
  contentIndex: number;
}

interface TextEndEvent extends StreamEventBase {
  type: 'text_end';
  contentIndex: number;
}

interface ThinkingStartEvent extends StreamEventBase {
  type: 'thinking_start';
  contentIndex: number;
}

interface ThinkingDeltaEvent extends StreamEventBase {
  type: 'thinking_delta';
  delta: string;
  contentIndex: number;
}

interface ThinkingEndEvent extends StreamEventBase {
  type: 'thinking_end';
  contentIndex: number;
}

interface ToolCallStartEvent extends StreamEventBase {
  type: 'toolcall_start';
  contentIndex: number;
  toolName: string;
}

interface ToolCallDeltaEvent extends StreamEventBase {
  type: 'toolcall_delta';
  contentIndex: number;
  argumentsDelta: string;
  partial: { name: string; arguments: Record<string, unknown> };
}

interface ToolCallEndEvent extends StreamEventBase {
  type: 'toolcall_end';
  toolCall: ToolCallBlock;
}

interface UsageEvent extends StreamEventBase {
  type: 'usage';
  usage: TokenUsage;
}

interface DoneEvent extends StreamEventBase {
  type: 'done';
  reason: 'complete' | 'toolCalls' | 'stop' | 'error';
  message?: Message;
}

interface ErrorEvent extends StreamEventBase {
  type: 'error';
  error: Error;
  recoverable: boolean;
}

type StreamEvent =
  | StartEvent | TextStartEvent | TextDeltaEvent | TextEndEvent
  | ThinkingStartEvent | ThinkingDeltaEvent | ThinkingEndEvent
  | ToolCallStartEvent | ToolCallDeltaEvent | ToolCallEndEvent
  | UsageEvent | DoneEvent | ErrorEvent;

// ============================================================
// 流对象
// ============================================================

/** 可迭代的流对象 */
interface LlmStream extends AsyncIterable<StreamEvent> {
  /** 等待流完成，返回最终消息 */
  result(): Promise<Message>;
  /** 中断流 */
  abort(): void;
  /** 当前已累积的内容 */
  readonly accumulated: {
    text: string;
    thinking: string;
    toolCalls: ToolCallBlock[];
  };
}

// ============================================================
// LLM Client 接口
// ============================================================

interface LlmClient {
  /** 发起流式请求 */
  stream(request: LlmRequest): LlmStream;

  /** 发起非流式请求 (内部可能仍用流式) */
  complete(request: LlmRequest): Promise<Message>;
}
```

### 2.2 Provider 适配器

```typescript
// ============================================================
// Provider 适配器接口
// ============================================================

/** Provider 配置 */
interface ProviderConfig {
  apiKey: string;
  baseUrl?: string;
  headers?: Record<string, string>;
  // 特定 provider 的选项
  options?: Record<string, unknown>;
}

/** Provider 适配器接口 */
interface ProviderAdapter {
  readonly name: string;

  /** 将统一请求转换为 provider 特定格式 */
  formatRequest(request: LlmRequest): unknown;

  /** 将 provider 的流事件转换为统一事件 */
  parseStreamEvent(raw: unknown): StreamEvent | null;

  /** 将 provider 的响应转换为统一消息 */
  parseResponse(raw: unknown): Message;
}

// ============================================================
// 内置 Provider 实现
// ============================================================

/** OpenAI 兼容适配器 (支持 OpenAI, DeepSeek, 通义千问, Ollama 等) */
class OpenAICompatibleAdapter implements ProviderAdapter {
  name = 'openai';

  // OpenAI 使用 SSE 格式: data: {"choices":[{"delta":{...}}]}
  // 工具调用通过 function_call / tool_calls 字段传递
  // 思考过程通过 reasoning_content 字段传递 (DeepSeek)
}

/** Anthropic 兼容适配器 (支持 Claude, 及兼容接口) */
class AnthropicCompatibleAdapter implements ProviderAdapter {
  name = 'anthropic';

  // Anthropic 使用 SSE 格式: event: content_block_start/delta/stop
  // 工具调用通过 content_block type=tool_use 传递
  // 思考过程通过 content_block type=thinking 传递
}

// ============================================================
// LlmClient 工厂
// ============================================================

class LlmClientFactory {
  private adapters = new Map<string, ProviderAdapter>();

  registerAdapter(adapter: ProviderAdapter): void;

  /** 根据 provider 名称创建 client */
  create(provider: string, config: ProviderConfig): LlmClient;

  /** 根据 "provider/model" 格式创建 */
  fromModel(modelId: string, config: ProviderConfig): LlmClient;
}

// 使用示例
const factory = new LlmClientFactory();
factory.registerAdapter(new OpenAICompatibleAdapter());
factory.registerAdapter(new AnthropicCompatibleAdapter());

const client = factory.create('openai', {
  apiKey: 'sk-xxx',
  baseUrl: 'https://api.openai.com/v1'
});

const stream = client.stream({
  model: 'gpt-4o',
  messages: [{ role: 'user', content: 'Hello' }],
  tools: [weatherTool]
});

for await (const event of stream) {
  if (event.type === 'text_delta') process.stdout.write(event.delta);
  if (event.type === 'toolcall_end') console.log('Tool:', event.toolCall.name);
}

const result = await stream.result();
```

### 2.3 事件流架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        LlmClient                                │
│                                                                 │
│  stream(request) ──► LlmStream (AsyncIterable<StreamEvent>)    │
│  complete(request) ──► Promise<Message>                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │
         ┌─────────────▼─────────────┐
         │    ProviderAdapter        │
         │                           │
         │  formatRequest()          │
         │  parseStreamEvent()       │
         │  parseResponse()          │
         └─────────────┬─────────────┘
                       │
       ┌───────────────┼───────────────┐
       │               │               │
┌──────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
│   OpenAI    │ │  Anthropic  │ │  Custom     │
│  Compatible │ │  Compatible │ │  Provider   │
└─────────────┘ └─────────────┘ └─────────────┘
```

---

## 三、Layer 2: Agent 运行时

### 3.1 核心接口

```typescript
// ============================================================
// Agent 配置
// ============================================================

interface AgentConfig {
  /** Agent 唯一标识 */
  id: string;
  /** Agent 名称 */
  name: string;
  /** 系统提示词 */
  systemPrompt: string;
  /** 使用的模型 (provider/model 格式) */
  model: string;
  /** 可用工具 */
  tools?: ToolDefinition[];
  /** 温度 */
  temperature?: number;
  /** 最大 token 数 */
  maxTokens?: number;
  /** 最大工具调用轮次 (防止无限循环) */
  maxToolRounds?: number;
  /** 自定义元数据 */
  metadata?: Record<string, unknown>;
}

// ============================================================
// 工具执行器
// ============================================================

/** 工具执行上下文 */
interface ToolExecutionContext {
  agentId: string;
  toolCallId: string;
  conversationId: string;
  abortSignal?: AbortSignal;
}

/** 工具执行结果 */
interface ToolResult {
  content: ContentBlock[];
  isError?: boolean;
}

/** 工具执行器接口 */
interface ToolExecutor {
  readonly name: string;
  readonly definition: ToolDefinition;

  /** 执行工具 */
  execute(args: Record<string, unknown>, context: ToolExecutionContext): Promise<ToolResult>;
}

// ============================================================
// 工具注册表
// ============================================================

class ToolRegistry {
  private executors = new Map<string, ToolExecutor>();

  /** 注册工具 */
  register(executor: ToolExecutor): void;

  /** 批量注册 */
  registerAll(executors: ToolExecutor[]): void;

  /** 获取工具定义列表 (用于 LLM 请求) */
  getDefinitions(): ToolDefinition[];

  /** 执行工具调用 */
  execute(
    toolCall: ToolCallBlock,
    context: ToolExecutionContext
  ): Promise<ToolResult>;
}

// ============================================================
// Agent 运行时
// ============================================================

/** Agent 执行状态 */
type AgentStatus = 'idle' | 'running' | 'waiting_tool' | 'completed' | 'failed' | 'aborted';

/** Agent 执行结果 */
interface AgentResult {
  status: AgentStatus;
  message: Message;
  usage: TokenUsage;
  rounds: number;  // 工具调用轮次
  duration: number; // 执行时长 ms
}

/** Agent 事件 */
type AgentEventType =
  | 'start'
  | 'round_start'     // 新一轮 LLM 调用
  | 'llm_stream'      // LLM 流式事件透传
  | 'tool_start'      // 工具开始执行
  | 'tool_end'        // 工具执行完成
  | 'round_end'       // 一轮结束
  | 'complete'        // Agent 完成
  | 'error';

interface AgentEvent {
  type: AgentEventType;
  agentId: string;
  timestamp: number;
  data?: unknown;
}

interface AgentStream extends AsyncIterable<AgentEvent> {
  result(): Promise<AgentResult>;
  abort(): void;
  readonly status: AgentStatus;
}

// ============================================================
// Agent 类
// ============================================================

class Agent {
  constructor(
    config: AgentConfig,
    llmClient: LlmClient,
    toolRegistry: ToolRegistry
  );

  /** 执行 Agent (流式) */
  run(input: string | Message[], conversationId?: string): AgentStream;

  /** 执行 Agent (非流式) */
  execute(input: string | Message[], conversationId?: string): Promise<AgentResult>;
}
```

### 3.2 Agent 执行流程

```typescript
// Agent.run() 内部逻辑 (伪代码)
async function* runAgent(agent: Agent, input: Input): AsyncIterable<AgentEvent> {
  const conversation: Message[] = buildInitialMessages(input);
  let round = 0;

  while (round < agent.config.maxToolRounds) {
    round++;
    yield { type: 'round_start', round };

    // 1. 调用 LLM
    const stream = agent.llmClient.stream({
      model: agent.config.model,
      messages: conversation,
      tools: agent.toolRegistry.getDefinitions(),
      temperature: agent.config.temperature,
      maxTokens: agent.config.maxTokens,
    });

    // 2. 转发 LLM 流事件
    for await (const event of stream) {
      yield { type: 'llm_stream', data: event };
    }

    // 3. 获取最终消息
    const message = await stream.result();
    conversation.push(message);

    // 4. 检查是否有工具调用
    const toolCalls = message.content.filter(b => b.type === 'toolCall');
    if (toolCalls.length === 0) {
      // 无工具调用，Agent 完成
      yield { type: 'complete', message };
      return;
    }

    // 5. 执行工具调用 (支持并行)
    const results = await Promise.all(
      toolCalls.map(async (tc) => {
        yield { type: 'tool_start', toolCall: tc };
        const result = await agent.toolRegistry.execute(tc, context);
        yield { type: 'tool_end', toolCall: tc, result };
        return { toolCallId: tc.id, toolName: tc.name, result };
      })
    );

    // 6. 将工具结果加入对话
    for (const { toolCallId, toolName, result } of results) {
      conversation.push({
        role: 'toolResult',
        toolCallId,
        toolName,
        content: result.content,
        isError: result.isError,
      });
    }

    yield { type: 'round_end', round };
  }

  // 超过最大轮次
  throw new Error(`Agent exceeded max tool rounds: ${agent.config.maxToolRounds}`);
}
```

### 3.3 内置工具

```typescript
// ============================================================
// 内置工具集
// ============================================================

/** 委托工具 - 将任务委托给其他 Agent */
class DelegateTool implements ToolExecutor {
  name = 'delegate';
  definition = {
    name: 'delegate',
    description: 'Delegate a task to another agent',
    parameters: Type.Object({
      agentId: Type.String({ description: 'Target agent ID' }),
      task: Type.String({ description: 'Task description' }),
    }),
  };

  async execute(args, context): Promise<ToolResult>;
}

/** 终止工具 - Agent 主动结束 */
class TerminateTool implements ToolExecutor {
  name = 'terminate';
  definition = {
    name: 'terminate',
    description: 'Terminate and return the final answer',
    parameters: Type.Object({
      answer: Type.String({ description: 'Final answer to return' }),
    }),
  };

  async execute(args, context): Promise<ToolResult>;
}

/** 读写文件工具 */
class ReadFileTool implements ToolExecutor { ... }
class WriteFileTool implements ToolExecutor { ... }

/** 执行代码工具 */
class ExecuteCodeTool implements ToolExecutor { ... }

/** 搜索工具 */
class WebSearchTool implements ToolExecutor { ... }
```

---

## 四、Layer 3: Orchestrator 编排层

> 参考项目：[open-multi-agent](https://github.com/open-multi-agent/open-multi-agent)

### 4.1 设计理念

- **目标优先 (Goal-First)**：工程师只描述目标，框架运行时自动构建任务 DAG
- **节点多态**：DAG 节点可以是 Function、Preset Agent 或 SubAgent
- **数据契约**：通过 Artifacts 声明节点间的数据流
- **人机协作**：支持执行前审批和执行中干预

### 4.2 三种运行模式

| 模式 | 方法 | DAG 来源 | 类比 |
|------|------|----------|------|
| **目标优先** | `RunTeamAsync(goal)` | LLM 自动拆解 | `/goal` |
| **手动 DAG** | `RunTasksAsync(nodes)` | 代码/配置定义 | `plan + execute` |
| **单 Agent** | `RunAgentAsync(config, input)` | 无 DAG | 最简入口 |

### 4.3 节点类型

```csharp
/// <summary>
/// 节点类型枚举
/// </summary>
public enum NodeKind
{
    Function,     // 纯函数/委托，无 LLM 调用
    PresetAgent,  // 预配置工具的 Agent（如数据分析 Agent: Jupyter + DuckDB）
    SubAgent      // LLM 动态分配工具的 Agent，不支持嵌套
}
```

| 类型 | 执行方式 | 工具来源 | 典型用途 |
|------|----------|----------|----------|
| **Function** | `Func<NodeContext, Task<object?>>` | 无 | 数据转换、格式化、API 调用 |
| **Preset Agent** | L2 Agent | 构建时静态绑定 | 专业领域 Agent（数据分析、代码审查等） |
| **SubAgent** | L2 Agent | 编排层 LLM 动态分配 | 通用任务 Agent |

> Preset Agent 和 SubAgent 均复用 L2 已实现的 Agent 运行时。SubAgent 不支持嵌套。

### 4.4 核心接口

```csharp
// ============================================================
// DAG 节点（多态）
// ============================================================

/// <summary>
/// DAG 节点基类
/// </summary>
public abstract class DAGNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string[] DependsOn { get; init; } = [];
    public NodeKind Kind { get; init; }

    // 数据契约
    public string[] InputArtifacts { get; init; } = [];   // 我需要什么
    public string[] OutputArtifacts { get; init; } = [];  // 我产出什么
}

/// <summary>
/// 函数节点 - 纯函数/委托
/// </summary>
public class FunctionNode : DAGNode
{
    public NodeKind Kind => NodeKind.Function;
    public required Func<NodeContext, Task<object?>> Execute { get; init; }
}

/// <summary>
/// Agent 节点 - Preset 或 SubAgent
/// </summary>
public class AgentNode : DAGNode
{
    public required string AgentId { get; init; }         // 引用 Team 中的 Agent 配置
    public string[]? ToolNames { get; init; }             // null = SubAgent 动态分配
    public string? SystemPrompt { get; init; }            // 可覆盖 Agent 默认 prompt
    public NodeKind Kind => ToolNames == null ? NodeKind.SubAgent : NodeKind.PresetAgent;
}

// ============================================================
// 节点执行上下文
// ============================================================

/// <summary>
/// 节点执行上下文
/// </summary>
public class NodeContext
{
    /// <summary>节点输入（来自依赖节点的输出，自动注入）</summary>
    public string Input { get; init; }

    /// <summary>依赖节点的输出字典 { nodeId → output }</summary>
    public IReadOnlyDictionary<string, object?> Dependencies { get; init; }

    /// <summary>共享内存（全局读写）</summary>
    public SharedMemory Memory { get; init; }

    /// <summary>Artifact 存储（数据契约）</summary>
    public ArtifactStore Artifacts { get; init; }
}

// ============================================================
// Team（编排基础设施）
// ============================================================

/// <summary>
/// Team - 编排基础设施容器
/// </summary>
public class Team
{
    public required string Name { get; init; }
    public required AgentConfig[] Agents { get; init; }
    public SharedMemory SharedMemory { get; } = new();
    public MessageBus MessageBus { get; } = new();
}

// ============================================================
// Orchestrator（编排入口）
// ============================================================

/// <summary>
/// 编排器主入口
/// </summary>
public class Orchestrator
{
    private readonly Team? _team;

    public Orchestrator(Team? team = null)
    {
        _team = team;
    }

    /// <summary>目标优先：LLM 自动拆解 DAG 并执行</summary>
    public Task<TeamResult> RunTeamAsync(string goal, CancellationToken ct = default);

    /// <summary>手动定义 DAG 并执行</summary>
    public Task<TeamResult> RunTasksAsync(DAGNode[] nodes, CancellationToken ct = default);

    /// <summary>单 Agent 执行</summary>
    public Task<AgentResult> RunAgentAsync(AgentConfig config, string input, CancellationToken ct = default);

    /// <summary>从计划恢复执行（跳过规划阶段）</summary>
    public Task<TeamResult> RunFromPlanAsync(DAGPlan plan, CancellationToken ct = default);

    /// <summary>创建可序列化的计划</summary>
    public DAGPlan CreatePlan(DAGNode[] nodes);

    // ===== Human-in-the-loop =====

    /// <summary>执行前审批整个计划</summary>
    public event Func<PlanApprovalContext, Task<PlanApprovalResult>>? OnPlanReady;

    /// <summary>每个任务完成后回调</summary>
    public event Func<TaskApprovalContext, Task<TaskApprovalResult>>? OnTaskComplete;

    /// <summary>取消令牌</summary>
    public CancellationTokenSource Cts { get; } = new();
}
```

### 4.5 SharedMemory

```csharp
/// <summary>
/// 共享内存 - 全局 KV 存储，节点间共享状态
/// </summary>
public class SharedMemory
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    public T? Get<T>(string key);
    public void Set<T>(string key, T value);
    public bool Has(string key);
    public void Delete(string key);
    public IReadOnlyDictionary<string, object?> Snapshot();
}
```

### 4.6 ArtifactStore（数据契约）

```csharp
/// <summary>
/// Artifact 定义
/// </summary>
public record Artifact
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Type? DataType { get; init; }
}

/// <summary>
/// Artifact 存储 - 节点间声明式数据流
/// </summary>
public class ArtifactStore
{
    /// <summary>读取 artifact</summary>
    public T? Get<T>(string name);

    /// <summary>写入 artifact（节点完成后自动存储）</summary>
    public void Set<T>(string name, T value);

    /// <summary>检查依赖是否满足</summary>
    public bool AreDependenciesMet(string[] required);

    /// <summary>获取所有 artifact 快照</summary>
    public IReadOnlyDictionary<string, object?> Snapshot();
}
```

**数据传递三层次**：

| 机制 | 用途 | 生命周期 |
|------|------|---------|
| **DependsOn** | 节点执行顺序 | DAG 静态定义 |
| **Artifacts** | 数据契约，声明式传参 | 节点间流转 |
| **SharedMemory** | 全局状态，任意读写 | 整个 Team 生命周期 |

### 4.7 Human-in-the-loop

```csharp
/// <summary>
/// 计划审批上下文
/// </summary>
public class PlanApprovalContext
{
    public DAGNode[] Nodes { get; init; }
    public string? Goal { get; init; }
    public SharedMemory Memory { get; init; }
}

/// <summary>
/// 计划审批结果
/// </summary>
public class PlanApprovalResult
{
    public bool Approved { get; init; }
    public DAGNode[]? ModifiedNodes { get; init; }  // 可修改 DAG
}

/// <summary>
/// 任务审批上下文
/// </summary>
public class TaskApprovalContext
{
    public DAGNode Node { get; init; }
    public object? Result { get; init; }
    public SharedMemory Memory { get; init; }
}

/// <summary>
/// 任务审批结果
/// </summary>
public enum TaskApprovalResult
{
    Continue,  // 继续执行下一个
    Pause,     // 暂停，等待人工干预
    Abort      // 终止整个流程
}
```

**使用模式**：

```csharp
// 模式1：全自动
var orchestrator = new Orchestrator(team);
await orchestrator.RunTeamAsync("分析销售数据");

// 模式2：执行前审批
orchestrator.OnPlanReady += async ctx =>
{
    PrintDAG(ctx.Nodes);
    var ok = await AskUser("是否执行？");
    return new PlanApprovalResult { Approved = ok };
};
await orchestrator.RunTeamAsync("分析销售数据");

// 模式3：每个任务完成后审批
orchestrator.OnTaskComplete += async ctx =>
{
    Console.WriteLine($"任务 {ctx.Node.Name} 完成");
    return TaskApprovalResult.Continue;
};
await orchestrator.RunTasksAsync(nodes);
```

### 4.8 DAG 调度器

```csharp
/// <summary>
/// DAG 调度器 - 拓扑排序 + 并行调度
/// </summary>
public class DAGScheduler
{
    private readonly DAGNode[] _nodes;
    private readonly Dictionary<string, DAGNode> _nodeMap;
    private readonly Dictionary<string, TaskState> _states;
    private readonly Dictionary<string, object?> _results;

    public DAGScheduler(DAGNode[] nodes);

    /// <summary>获取所有就绪的任务（依赖已完成）</summary>
    public DAGNode[] GetReadyTasks();

    /// <summary>标记任务完成</summary>
    public void MarkComplete(string nodeId, object? result);

    /// <summary>标记任务失败</summary>
    public void MarkFailed(string nodeId, Exception error);

    /// <summary>检查是否所有任务完成</summary>
    public bool IsComplete { get; }

    /// <summary>验证 DAG（循环检测）</summary>
    public ValidationResult Validate();
}
```

### 4.9 计划持久化

```csharp
/// <summary>
/// 可序列化的 DAG 计划
/// </summary>
public class DAGPlan
{
    public string? Goal { get; init; }
    public DAGNodeDto[] Nodes { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// DAG 节点 DTO（用于序列化）
/// </summary>
public class DAGNodeDto
{
    public string Id { get; init; }
    public string Name { get; init; }
    public NodeKind Kind { get; init; }
    public string[] DependsOn { get; init; }
    public string[] InputArtifacts { get; init; }
    public string[] OutputArtifacts { get; init; }

    // Agent 节点特有
    public string? AgentId { get; init; }
    public string[]? ToolNames { get; init; }
    public string? SystemPrompt { get; init; }
}
```

### 4.10 执行流程

```
goal / nodes
    │
    ▼
┌─────────────┐
│ TaskPlanner │ (RunTeamAsync 时，LLM 自动拆解)
└──────┬──────┘
       │
       ▼
┌─────────────┐     ┌──────────────┐
│ OnPlanReady │────►│ 用户审批/修改 │
└──────┬──────┘     └──────────────┘
       │ Approved? → No → Abort
       │ Yes
       ▼
┌─────────────┐
│ DAGScheduler│ 拓扑排序 + 并行调度
└──────┬──────┘
       │
  ┌────┼────┐
  ▼    ▼    ▼
Func Preset SubAgent  ← 按节点类型分发执行
  └────┼────┘
       │
       ▼
┌──────────────┐     ┌──────────────┐
│OnTaskComplete│────►│ Continue/    │
└──────┬──────┘     │ Pause/Abort  │
       │            └──────────────┘
       ▼
   TeamResult
```

### 4.11 使用示例

#### 目标优先模式

```csharp
var team = new Team
{
    Name = "数据分析团队",
    Agents =
    [
        new AgentConfig { Id = "analyst", SystemPrompt = "你是数据分析专家", Tools = [jupyterTool, duckdbTool] },
        new AgentConfig { Id = "writer", SystemPrompt = "你是报告撰写专家", Tools = [fileWriteTool] }
    ]
};

var orchestrator = new Orchestrator(team);
var result = await orchestrator.RunTeamAsync("分析上季度销售趋势，生成可视化报告");
```

#### 手动 DAG 模式

```csharp
var nodes = new DAGNode[]
{
    new FunctionNode
    {
        Id = "fetch",
        Name = "获取数据",
        Execute = async ctx => await FetchSalesDataAsync()
    },
    new AgentNode
    {
        Id = "analyze",
        Name = "分析数据",
        AgentId = "analyst",           // Preset Agent，工具已预配置
        DependsOn = ["fetch"]
    },
    new AgentNode
    {
        Id = "report",
        Name = "生成报告",
        AgentId = "writer",
        DependsOn = ["analyze"]
    }
};

var orchestrator = new Orchestrator(team);
var result = await orchestrator.RunTasksAsync(nodes);
```

#### SubAgent 模式

```csharp
var nodes = new DAGNode[]
{
    new FunctionNode
    {
        Id = "fetch",
        Name = "获取数据",
        Execute = async ctx => await FetchDataAsync()
    },
    new AgentNode
    {
        Id = "process",
        Name = "处理数据",
        AgentId = "general",           // SubAgent，工具由 LLM 动态分配
        ToolNames = null,              // null = 动态分配
        DependsOn = ["fetch"]
    }
};
```

#### 计划持久化

```csharp
// 创建计划
var plan = orchestrator.CreatePlan(nodes);

// 序列化（可保存到文件/数据库）
var json = JsonSerializer.Serialize(plan);

// 从计划恢复执行
var savedPlan = JsonSerializer.Deserialize<DAGPlan>(json);
var result = await orchestrator.RunFromPlanAsync(savedPlan);
```

---

## 六、架构总览

```
┌─────────────────────────────────────────────────────────────────────────┐
│ L3: Orchestrator                                                        │
│                                                                         │
│  Orchestrator(Team?)                                                    │
│  ├── RunTeamAsync(goal)    → TaskPlanner → DAG → 执行                   │
│  ├── RunTasksAsync(nodes)  → 直接执行                                   │
│  ├── RunAgentAsync(config) → 单 Agent                                   │
│  └── RunFromPlanAsync()    → 从持久化计划恢复                            │
│                                                                         │
│  OnPlanReady (审批)  │  OnTaskComplete (干预)  │  Cts (取消)            │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                    ┌───────────▼───────────┐
                    │     DAGScheduler      │
                    │  拓扑排序 + 并行调度   │
                    └───────────┬───────────┘
                                │
         ┌──────────────────────┼──────────────────────┐
         │                      │                      │
   ┌─────▼─────┐         ┌─────▼─────┐         ┌─────▼─────┐
   │FunctionNode│         │AgentNode  │         │AgentNode  │
   │            │         │(Preset)   │         │(SubAgent) │
   │ Func<T,R>  │         │ Agent     │         │ Agent     │
   │            │         │ +固定工具  │         │ +动态工具  │
   └────────────┘         └─────┬─────┘         └─────┬─────┘
                                │                      │
                    ┌───────────▼──────────────────────▼───┐
                    │ Team                                  │
                    │ ├── AgentConfig[] (Agent 池)          │
                    │ ├── SharedMemory   (全局 KV 存储)     │
                    │ ├── ArtifactStore  (数据契约)         │
                    │ └── MessageBus     (Agent 间通信)     │
                    └───────────────────────┬──────────────┘
                                │
┌───────────────────────────────┼─────────────────────────────────────────┐
│ L2: Agent 运行时                                                        │
│                                                                         │
│  Agent.RunAsync(input) → AgentStream                                    │
│  ├── SystemPrompt                                                      │
│  ├── ToolRegistry (IToolExecutor[])                                     │
│  └── LlmClient                                                         │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
┌───────────────────────────────┼─────────────────────────────────────────┐
│ L1: LLM 抽象层                                                          │
│                                                                         │
│  ILlmClient                                                             │
│  ├── Stream(request) → LlmStream (IAsyncEnumerable<StreamEvent>)        │
│  ├── Complete(request) → LlmResponse                                    │
│  └── IProviderAdapter (OpenAI / Anthropic / Custom)                     │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 七、实现状态

| 层级 | 项目 | 状态 | 说明 |
|------|------|------|------|
| **L1: LLM 抽象层** | `InsightaAI.LLM` | ✅ 已实现 | ILlmClient, IProviderAdapter, StreamEvent |
| **L2: Agent 运行时** | `InsightaAI.Agent` | ✅ 已实现 | Agent, ToolRegistry, IToolExecutor, MCP 集成 |
| **L3: Orchestrator** | `InsightaAI.Orchestrator` | ❌ 待实现 | Team, DAGNode, DAGScheduler, SharedMemory |
| **CLI** | `InsightaAI.Agent.Cli` | ✅ 已实现 | 终端交互界面，EventRenderer |

### L3 待实现清单

```
InsightaAI.Orchestrator/
├── Core/
│   ├── Orchestrator.cs           // 编排入口
│   ├── Team.cs                   // Team 基础设施
│   └── DAGScheduler.cs           // DAG 调度器
├── Nodes/
│   ├── DAGNode.cs                // 节点基类
│   ├── FunctionNode.cs           // 函数节点
│   └── AgentNode.cs              // Agent 节点
├── Storage/
│   ├── SharedMemory.cs           // 共享内存
│   └── ArtifactStore.cs          // Artifact 存储
├── Planning/
│   ├── TaskPlanner.cs            // LLM 任务分解
│   ├── DAGPlan.cs                // 计划 DTO
│   └── Prompts/
│       └── TaskPlanner.txt       // 规划 prompt
├── HumanInTheLoop/
│   ├── PlanApprovalContext.cs    // 计划审批
│   └── TaskApprovalContext.cs    // 任务审批
└── Results/
    ├── TeamResult.cs             // Team 执行结果
    └── NodeResult.cs             // 节点执行结果
```
