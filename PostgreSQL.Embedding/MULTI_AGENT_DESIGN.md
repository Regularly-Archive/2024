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

## 四、Layer 3: Multi-Agent 编排

### 4.1 核心概念

```typescript
// ============================================================
// 共享内存
// ============================================================

interface SharedMemory {
  /** 获取值 */
  get<T>(key: string): T | undefined;

  /** 设置值 */
  set<T>(key: string, value: T): void;

  /** 删除值 */
  delete(key: string): boolean;

  /** 检查是否存在 */
  has(key: string): boolean;

  /** 按前缀搜索 */
  keys(prefix?: string): string[];

  /** 清空 */
  clear(): void;
}

// ============================================================
// 消息总线
// ============================================================

type MessageType = 'task' | 'result' | 'broadcast' | 'request' | 'response';

interface BusMessage {
  id: string;
  type: MessageType;
  from: string;      // sender agent ID
  to: string | '*';  // receiver agent ID or '*' for broadcast
  payload: unknown;
  timestamp: number;
  replyTo?: string;  // 用于 request/response 模式
}

interface MessageBus {
  /** 发送消息 */
  send(message: Omit<BusMessage, 'id' | 'timestamp'>): void;

  /** 订阅消息 */
  subscribe(
    filter: { from?: string; type?: MessageType },
    handler: (message: BusMessage) => void
  ): () => void; // 返回取消订阅函数

  /** 请求-响应模式 */
  request(to: string, payload: unknown, timeout?: number): Promise<BusMessage>;
}

// ============================================================
// 任务定义
// ============================================================

type TaskStatus = 'pending' | 'ready' | 'running' | 'completed' | 'failed' | 'skipped';

interface Task {
  id: string;
  name: string;
  agentId: string;        // 执行此任务的 Agent
  input: string | Message[];
  dependsOn?: string[];   // 依赖的任务 ID 列表
  condition?: (results: Map<string, AgentResult>) => boolean; // 条件执行
  metadata?: Record<string, unknown>;
}

interface TaskResult {
  taskId: string;
  agentId: string;
  result: AgentResult;
  startTime: number;
  endTime: number;
}
```

### 4.2 Team 编排器

```typescript
// ============================================================
// Team 配置
// ============================================================

interface TeamConfig {
  /** Team 名称 */
  name: string;

  /** Agent 配置列表 */
  agents: AgentConfig[];

  /** 任务列表 (DAG) */
  tasks: Task[];

  /** 全局共享内存配置 */
  sharedMemory?: {
    /** 初始数据 */
    initialData?: Record<string, unknown>;
  };

  /** 编排策略 */
  strategy?: 'parallel' | 'sequential' | 'dag' | 'auto';

  /** 全局超时 (ms) */
  timeout?: number;

  /** 最大并发 Agent 数 */
  maxConcurrency?: number;
}

// ============================================================
// Team 运行时
// ============================================================

/** Team 执行状态 */
type TeamStatus = 'idle' | 'running' | 'completed' | 'failed' | 'aborted';

/** Team 事件 */
type TeamEventType =
  | 'team_start'
  | 'task_ready'
  | 'task_start'
  | 'agent_start'
  | 'agent_event'      // 透传 Agent 事件
  | 'task_complete'
  | 'task_failed'
  | 'team_complete'
  | 'team_failed';

interface TeamEvent {
  type: TeamEventType;
  timestamp: number;
  taskId?: string;
  agentId?: string;
  data?: unknown;
}

interface TeamStream extends AsyncIterable<TeamEvent> {
  result(): Promise<TeamResult>;
  abort(): void;
  readonly status: TeamStatus;
}

interface TeamResult {
  status: TeamStatus;
  taskResults: Map<string, TaskResult>;
  totalUsage: TokenUsage;
  duration: number;
}

// ============================================================
// OpenMultiAgent 主入口
// ============================================================

class OpenMultiAgent {
  private llmFactory: LlmClientFactory;
  private toolRegistry: ToolRegistry;

  constructor(options?: {
    llmFactory?: LlmClientFactory;
    toolRegistry?: ToolRegistry;
  });

  /** 创建 Team */
  createTeam(config: TeamConfig): Team;

  /** 运行 Team (流式) */
  runTeam(team: Team): TeamStream;

  /** 运行 Team (非流式) */
  runTeamSync(team: Team): Promise<TeamResult>;

  /** 运行单个 Agent (便捷方法) */
  runAgent(agentConfig: AgentConfig, input: string): Promise<AgentResult>;
}
```

### 4.3 DAG 任务调度器

```typescript
// ============================================================
// DAG 调度器
// ============================================================

class DagScheduler {
  private tasks: Map<string, Task>;
  private results: Map<string, TaskResult> = new Map();
  private completed: Set<string> = new Set();

  constructor(tasks: Task[]);

  /** 获取所有就绪的任务 (依赖已完成) */
  getReadyTasks(): Task[];

  /** 标记任务完成 */
  markComplete(taskId: string, result: TaskResult): void;

  /** 标记任务失败 */
  markFailed(taskId: string, error: Error): void;

  /** 检查是否所有任务完成 */
  isComplete(): boolean;

  /** 获取任务执行顺序 (拓扑排序) */
  getExecutionOrder(): string[][];

  /** 检查是否有循环依赖 */
  validate(): { valid: boolean; errors: string[] };
}

// ============================================================
// 执行引擎
// ============================================================

class TeamExecutionEngine {
  private scheduler: DagScheduler;
  private agentPool: AgentPool;
  private messageBus: MessageBus;
  private sharedMemory: SharedMemory;

  /** 并行执行所有就绪任务 */
  async execute(config: TeamConfig): AsyncIterable<TeamEvent> {
    // 1. 验证 DAG
    const validation = this.scheduler.validate();
    if (!validation.valid) throw new Error(validation.errors.join('\n'));

    // 2. 循环执行
    while (!this.scheduler.isComplete()) {
      const readyTasks = this.scheduler.getReadyTasks();

      // 3. 并行执行就绪任务 (受 maxConcurrency 限制)
      await this.agentPool.runParallel(readyTasks, async (task) => {
        // 创建 Agent
        const agent = this.createAgent(task.agentId);

        // 执行
        const stream = agent.run(task.input);

        // 转发事件
        for await (const event of stream) {
          yield { type: 'agent_event', agentId: task.agentId, data: event };
        }

        // 获取结果
        const result = await stream.result();
        this.scheduler.markComplete(task.id, { taskId: task.id, result, ... });
      });
    }
  }
}
```

### 4.4 并发控制

```typescript
// ============================================================
// Agent 池 (并发控制)
// ============================================================

class AgentPool {
  private semaphore: Semaphore;
  private agents: Map<string, Agent> = new Map();

  constructor(maxConcurrency: number);

  /** 并行执行任务，受信号量限制 */
  async runParallel<T>(
    tasks: T[],
    executor: (task: T) => Promise<void>
  ): Promise<void>;

  /** 获取或创建 Agent */
  getAgent(config: AgentConfig): Agent;
}

/** 信号量实现 */
class Semaphore {
  private permits: number;
  private queue: Array<() => void> = [];

  constructor(permits: number);

  async acquire(): Promise<void>;
  release(): void;
}
```

---

## 五、使用示例

### 5.1 基础 Agent 使用

```typescript
// 创建 LLM Client
const client = factory.create('openai', { apiKey: 'sk-xxx' });

// 定义工具
const weatherTool: ToolExecutor = {
  name: 'get_weather',
  definition: {
    name: 'get_weather',
    description: 'Get weather for a location',
    parameters: Type.Object({
      location: Type.String(),
    }),
  },
  async execute(args) {
    return { content: [{ type: 'text', text: `Weather in ${args.location}: Sunny, 25°C` }] };
  },
};

// 创建 Agent
const agent = new Agent(
  {
    id: 'assistant',
    name: 'Weather Assistant',
    systemPrompt: 'You are a helpful weather assistant.',
    model: 'gpt-4o',
    maxToolRounds: 5,
  },
  client,
  new ToolRegistry([weatherTool])
);

// 执行
const stream = agent.run('What is the weather in Beijing?');

for await (const event of stream) {
  switch (event.type) {
    case 'llm_stream':
      // 透传 LLM 事件
      if (event.data.type === 'text_delta') {
        process.stdout.write(event.data.delta);
      }
      break;
    case 'tool_start':
      console.log(`\n[Calling ${event.toolCall.name}...]`);
      break;
    case 'complete':
      console.log('\n[Done]');
      break;
  }
}
```

### 5.2 Multi-Agent 协作

```typescript
// 定义多个 Agent
const teamConfig: TeamConfig = {
  name: 'Research Team',
  strategy: 'dag',
  maxConcurrency: 3,
  agents: [
    {
      id: 'researcher',
      name: 'Researcher',
      systemPrompt: 'You research topics and gather information.',
      model: 'gpt-4o',
      tools: [webSearchTool, readFileTool],
    },
    {
      id: 'analyst',
      name: 'Analyst',
      systemPrompt: 'You analyze data and provide insights.',
      model: 'gpt-4o',
      tools: [executeCodeTool],
    },
    {
      id: 'writer',
      name: 'Writer',
      systemPrompt: 'You write polished reports based on analysis.',
      model: 'claude-3-5-sonnet',
      tools: [writeFileTool],
    },
  ],
  tasks: [
    {
      id: 'research',
      name: 'Research Phase',
      agentId: 'researcher',
      input: 'Research the latest trends in AI agents',
    },
    {
      id: 'analyze',
      name: 'Analysis Phase',
      agentId: 'analyst',
      input: 'Analyze the research results',
      dependsOn: ['research'],  // 依赖研究完成
    },
    {
      id: 'write',
      name: 'Writing Phase',
      agentId: 'writer',
      input: 'Write a report based on the analysis',
      dependsOn: ['analyze'],  // 依赖分析完成
    },
  ],
};

// 运行
const orchestrator = new OpenMultiAgent();
const team = orchestrator.createTeam(config);
const stream = orchestrator.runTeam(team);

for await (const event of stream) {
  console.log(`[${event.type}] ${event.agentId || ''} ${event.taskId || ''}`);
}

const result = await stream.result();
console.log('Total cost:', result.totalUsage.totalCost);
```

### 5.3 对话式多 Agent

```typescript
// 对话式协作：多个 Agent 轮流对话
const discussionConfig: TeamConfig = {
  name: 'Discussion',
  strategy: 'sequential',
  agents: [
    { id: 'moderator', name: 'Moderator', model: 'gpt-4o', systemPrompt: '...' },
    { id: 'expert-a', name: 'Expert A', model: 'claude-3-5-sonnet', systemPrompt: '...' },
    { id: 'expert-b', name: 'Expert B', model: 'gpt-4o', systemPrompt: '...' },
  ],
  tasks: [
    {
      id: 'round-1-a',
      agentId: 'expert-a',
      input: 'Share your perspective on: {{topic}}',
    },
    {
      id: 'round-1-b',
      agentId: 'expert-b',
      input: 'Respond to Expert A\'s view: {{round-1-a.result}}',
      dependsOn: ['round-1-a'],
    },
    {
      id: 'round-2-a',
      agentId: 'expert-a',
      input: 'Consider Expert B\'s response and refine: {{round-1-b.result}}',
      dependsOn: ['round-1-b'],
    },
    {
      id: 'summary',
      agentId: 'moderator',
      input: 'Summarize the discussion: {{round-1-a.result}} {{round-1-b.result}} {{round-2-a.result}}',
      dependsOn: ['round-2-a'],
    },
  ],
};
```

---

## 六、架构总览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        OpenMultiAgent (入口)                           │
│                                                                         │
│  createTeam()  runTeam()  runAgent()                                   │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                    ┌───────────▼───────────┐
                    │  TeamExecutionEngine  │
                    │                       │
                    │  DagScheduler         │
                    │  AgentPool            │
                    │  MessageBus           │
                    │  SharedMemory         │
                    └───────────┬───────────┘
                                │
                ┌───────────────┼───────────────┐
                │               │               │
        ┌───────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐
        │    Agent A   │ │  Agent B   │ │  Agent C    │
        │              │ │            │ │             │
        │  AgentRunner │ │ AgentRunner│ │ AgentRunner │
        │  ToolRegistry│ │ ToolRegistry│ │ ToolRegistry│
        └───────┬──────┘ └─────┬──────┘ └──────┬──────┘
                │               │               │
                └───────────────┼───────────────┘
                                │
                    ┌───────────▼───────────┐
                    │      LlmClient       │
                    │                       │
                    │  stream() / complete()│
                    │  ProviderAdapter      │
                    └───────────┬───────────┘
                                │
                ┌───────────────┼───────────────┐
                │               │               │
        ┌───────▼──────┐ ┌─────▼──────┐ ┌──────▼──────┐
        │   OpenAI     │ │ Anthropic  │ │  Custom     │
        │  Compatible  │ │ Compatible │ │  Provider   │
        └──────────────┘ └────────────┘ └─────────────┘
```

---

## 七、与现有项目集成

对于 PostgreSQL.Embedding (.NET) 项目，可以：

1. **移植核心概念** - 将 TypeScript 设计转换为 C# 接口
2. **利用现有基础设施** - 复用 Semantic Kernel、Polly 等
3. **事件流使用 IAsyncEnumerable<T>** - C# 的异步迭代器

```csharp
// C# 对应设计
public interface ILlmClient
{
    IAsyncEnumerable<StreamEvent> StreamAsync(LlmRequest request, CancellationToken ct = default);
    Task<Message> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

public interface IAgent
{
    IAsyncEnumerable<AgentEvent> RunAsync(string input, CancellationToken ct = default);
    Task<AgentResult> ExecuteAsync(string input, CancellationToken ct = default);
}
```
