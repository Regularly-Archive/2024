# AgentBuilder 与 CLI DI 探索

> 状态：探索记录，不代表已经批准的实现方案。
>
> 更新时间：2026-07-28

## 1. 探索目标

本次探索关注三个问题：

1. 当前 CLI 和 `AgentBuilder` 如何创建对象，以及对象之间的依赖关系是什么。
2. 哪些对象应该跨整个 CLI 进程复用，哪些对象应该绑定到一次聊天会话或一次 Agent 生命周期。
3. 是否可以从 `Program.cs` 开始建立统一 DI Composition Root，并逐步替代 CLI 中的手动对象创建。

本阶段只记录发现和候选方案，不修改运行时代码。

## 2. 当前对象创建关系

当前流程大致如下：

```text
Program.Main
├── 静态创建 JsonlMessageStorage
├── 手动创建 ConfigCommand / ChatCommand / SessionsCommand / ...
├── 初始化 Serilog
└── 手动初始化 OpenTelemetry

ChatCommand.ExecuteAsync
├── CliConfig.Load / AuthConfig.Load
├── LlmClientFactory.Create
│   └── 每次创建 LlmClientFactory，并注册四个 Adapter
├── CreateToolRegistry
│   ├── new ToolRegistry
│   ├── AddBuiltInTools
│   └── 手动注册 AskUserTool
├── CreateSkillRegistry
│   └── new SkillRegistry + LocalSkillProvider
├── CreateMcpRegistry
│   └── new SimpleMcpConnectionPool + McpRegistry + Json provider
├── GetOrCreateSessionAsync
│   └── new ChatSession
├── CreateSummaryService
├── CreateAgentAsync
│   ├── new SessionMemoryHook
│   ├── new ContextManager + CompactionStrategy
│   ├── new MemoryManager
│   ├── new AgentBuilder
│   └── AgentBuilder.Build
│       ├── BuildServiceProvider（内部容器）
│       └── new Agent
└── 手动添加 ToolHook / AgentHook / Telemetry
```

另外，`Agent` 的旧构造函数会再次创建内部 `ServiceProvider`；新的 `Agent(AgentConfig, IServiceProvider)` 则要求调用方提前注册 `ToolRegistry` 等依赖。这样目前至少存在三种依赖来源：

- `Program.cs` 的静态字段和手动构造；
- `ChatCommand` 的工厂方法；
- `AgentBuilder` 的局部 `ServiceCollection` 和局部 `ServiceProvider`。

## 3. 当前主要问题

### 3.1 Composition Root 不集中

`Program.cs` 是应用入口，但没有真正负责构建应用对象图。命令、存储、日志和 Telemetry 分散在不同位置创建，导致应用生命周期无法由一个容器统一管理。

CLI 项目已经引用 `Microsoft.Extensions.Hosting`，但当前尚未使用 Host 或统一的根容器。

### 3.2 AgentBuilder 会创建第二个容器

`AgentBuilder.Build()` 当前执行 `BuildServiceProvider()`，因此每次构建 Agent 都会产生独立容器。这样会带来几个问题：

- 根容器中的 Singleton 无法自然共享到 Agent 容器；
- Disposable 服务的释放边界不直观；
- 同一个依赖可能被实例化多次；
- 测试需要同时理解外部容器和 AgentBuilder 内部容器；
- `ConfigureServices` 容易被误解为配置应用级容器，实际上只配置局部容器。

### 3.3 AgentBuilder 的职责混合

当前 `AgentBuilder` 同时承担：

- 保存 `AgentConfig`；
- 注册依赖；
- 注册 LLM Adapter；
- 校验必需服务；
- 构建 ServiceProvider；
- 创建 Agent。

未来如果继续向其中加入 Storage、Memory、MCP、Skill 和 Tool 生命周期，它会变成一个难以维护的总装配器。

### 3.4 CLI 的运行时状态与基础设施混合

`ChatCommand` 同时负责命令行参数、配置读取、会话恢复、LLM 创建、工具注册、Agent 创建、Hook 注册和聊天循环。尤其是模型切换时，`ChatCommand` 直接 Dispose 旧 Agent 并重新创建新 Agent，说明 Agent 本身是可替换的会话运行时对象，不适合作为全局 Singleton。

### 3.5 部分服务通过 Service Locator 获取依赖

`ToolCallExecutor` 从 `IServiceProvider` 获取 `ToolRegistry`、`IFileSystem`、Artifact Store 和 Tool Result Processor，并在缺失时自行创建默认实现。这会掩盖注册遗漏，也使真实依赖关系无法从构造函数看出来。

`BuiltInToolsExtensions.AddBuiltInTools(IServiceProvider)` 和 `SummaryOptions.ClientFactory` 也保留了运行时查找或委托创建依赖的模式。

## 4. 初步生命周期判断

以下是候选生命周期，不是最终实现承诺。判断依据是当前对象的可变状态、是否绑定会话，以及是否持有外部资源。

| 对象/服务 | 当前状态 | 候选生命周期 | 原因与注意事项 |
|---|---|---|---|
| `ILogger` / Serilog | `Program` 手动初始化 | Singleton / Host 管理 | 进程级基础设施，应统一由 Host 创建和释放 |
| OpenTelemetry Provider | `Program` 手动创建 | Singleton / Host 管理 | 进程级资源，退出时统一 Dispose |
| `CliConfig` / `AuthConfig` | 每次 Chat 重新读取 | Singleton 或启动时加载 | 如果支持运行中 `config` 修改，需要明确刷新策略，不能盲目缓存 |
| `IMessageStorage` | `Program` 静态持有 | Singleton（CLI） | JSONL 存储内部有锁；PostgreSQL 需确认 SqlSugar 客户端线程安全和释放边界 |
| LLM Adapter / LLM Factory | 每次 LLM Client 创建时注册 | Singleton | Adapter 通常无会话状态，可复用；Factory 应避免每次重复注册 |
| `ILlmClient` | 每次 Chat 创建，模型切换时重建 | Agent/模型生命周期 | Client 绑定 provider 配置和模型适配器；不建议强行做全局 Singleton |
| `ToolRegistry` | 每次 Chat 创建，MCP 激活会修改 | Chat Session 或 Agent 生命周期 | 包含动态工具注册状态，不能在多个独立会话之间共享 |
| 内置 Tool 实例 | 注册到 `ToolRegistry` | 与 ToolRegistry 同生命周期 | `FileReadState` 等状态需要和同一组工具一起创建 |
| `SkillRegistry` | 每次 Chat 创建，保存激活 Skill | Chat Session 生命周期 | `_activeSkills` 是可变状态，不应跨聊天共享 |
| `McpRegistry` / ConnectionPool | 每次 Chat 创建，保存缓存和激活工具 | Chat Session 生命周期 | 连接池内部可能复用连接，但注册表和激活工具绑定当前会话 |
| `ChatSession` | 每次加载/创建会话 | Chat Session 生命周期 | 包含当前消息缓存、标题生成状态和会话 ID |
| `SessionMemoryHook` | 每个 Session 创建 | Chat Session/Agent 生命周期 | 保存 SessionId、用户信息和摘要状态，不应做 Singleton |
| `SummaryService` | 每次 Agent 创建时创建 | 暂定 Singleton 或 Chat Session | 本身主要持有配置和 ClientFactory，但配置刷新和并发安全需要确认 |
| `ContextManager` | 每个 Agent 创建 | Agent 生命周期 | 持有上下文预算、压缩锁和策略列表 |
| `ICompactStrategy` | 每个 ContextManager 创建 | Agent 生命周期 | 策略可能绑定 SessionMemoryHook、SummaryService、ToolRegistry |
| `Agent` | 模型切换时 Dispose 并重建 | Agent/模型生命周期 | 持有可变 Skill、Hook、Provider 和当前 Agent 配置 |
| `AgentLoop` | 每次 `RunStreamAsync` 内创建 | Turn 生命周期 | 当前代码已经体现出它不应被长时间复用 |
| `ToolCallExecutor` | 每个 Agent Turn 创建 | Turn 生命周期 | 持有 SessionId、Handler 和本轮工具结果 |
| `CancellationToken` | 方法参数传递 | Turn/操作生命周期 | 不应注册到容器；由调用方显式传递 |
| `ChatRenderer` / `EventRenderer` | CLI 手动创建 | Command 或 Chat 生命周期 | `EventRenderer` 持有 spinner 和 CTS，不能做全局共享状态 |

## 5. 值得保留的边界

### 5.1 Session、Agent、Turn 不是同一个生命周期

建议明确区分：

```text
进程（Host）
└── Chat Session
    ├── ChatSession / ToolRegistry / SkillRegistry / McpRegistry
    ├── Agent（当前模型）
    │   └── 多次 Turn
    │       ├── AgentLoop
    │       └── ToolCallExecutor
    └── 模型切换时替换 Agent，保留 Chat Session
```

会话恢复和模型切换都说明 `ChatSession` 不应和 `Agent` 强绑定：恢复会话可以使用新的 Agent，模型切换也不应重新创建消息存储和会话对象。

### 5.2 动态注册表需要显式拥有者

`ToolRegistry`、`SkillRegistry` 和 `McpRegistry` 都包含可变注册或激活状态。它们可以由 DI 创建，但不能简单地注册为应用级 Singleton。第一版迁移应先把它们绑定到一个明确的 Chat Session Scope。

### 5.3 配置对象与运行时对象分离

`AgentConfig`、`ContextBudget`、`SummaryOptions` 属于构建参数或配置快照；`Agent`、`ContextManager`、`SessionMemoryHook` 属于运行时对象。未来不应让 `AgentBuilder` 通过隐藏的 ServiceProvider 把两者混在一起。

## 6. 候选目标结构

目标不是让所有对象都通过构造函数自动解析，而是建立一个清晰的根容器和明确的工厂边界：

```text
Program.cs
└── HostApplicationBuilder
    ├── AddInsightaConfiguration
    ├── AddInsightaLogging
    ├── AddInsightaTelemetry
    ├── AddInsightaStorage
    ├── AddInsightaLlm
    ├── AddInsightaSkills
    ├── AddInsightaMcp
    └── AddInsightaChat
        └── IAgentFactory
```

候选职责：

- `Program.cs`：创建 Host、注册命令、启动和释放 Host。
- `IChatApplication`：协调配置校验、会话选择、聊天循环和命令行交互。
- `IAgentFactory`：基于模型和 Session 上下文创建 Agent，不创建根容器。
- `AgentBuilder`：保留为公开 API 的兼容层，或降级为 Agent 参数/注册选项，不再调用 `BuildServiceProvider()`。
- `ToolRegistryFactory`：创建当前 Chat Session 的工具注册表，并注册内置工具、`ask_user` 和 MCP 相关工具。
- `ChatSessionFactory`：封装创建/加载/恢复会话的逻辑。

一种可行的容器分层是：

```text
Root Host Provider
├── 日志、Telemetry、配置、Storage、LLM Adapter Factory
└── Chat Session Scope
    ├── ToolRegistry / SkillRegistry / McpRegistry
    ├── ChatSession
    ├── SummaryService / SessionMemoryHook
    └── AgentFactory 创建 Agent
```

这里的 Session Scope 只是当前候选方案。由于 `System.CommandLine` 命令处理器和聊天循环是手动组织的，如何让 Scope 覆盖完整的交互过程，需要在实现前单独验证。

## 7. 需要特别验证的风险

1. `System.CommandLine` 当前通过 `SetHandler` 捕获委托，命令对象如何从 Host Scope 获取，不能直接假设框架会自动支持构造函数注入。
2. `ChatCommand` 的模型切换会重建 LLM Client 和 Agent，但应复用或重新绑定哪些 Session 级服务，需要定义清楚。
3. `McpRegistry` 和 `ToolRegistry` 之间存在动态注册关系；如果 Registry 是 Scoped，Agent 的 ServiceProvider 必须能访问同一实例。
4. `Agent` 当前的兼容构造函数和 `AgentBuilder` 私有容器需要保留多久，决定了迁移期间的复杂度。
5. `PostgresMessageStorage` 内部持有 SqlSugar 客户端，不能只按接口名称决定 Singleton/Scoped，需要确认线程安全、连接管理和 Dispose 行为。
6. `SummaryService` 通过 `ClientFactory` 按模型动态创建客户端；这与“LLM Client 由 DI 直接注入”的简单模式不同，可能需要保留专用工厂。
7. `ToolCallExecutor` 的 Service Locator 和默认 `new` 逻辑应在迁移中逐步移除，否则 DI 注册遗漏仍然会被隐藏。

## 8. 建议的后续探索顺序

在开始修改代码前，建议先完成以下小范围验证：

1. 画出 CLI 一次完整 Chat 的对象图，并标记模型切换时保留/替换的对象。
2. 为 `Root Host` 和 `Chat Session Scope` 写一个最小测试组合，只验证实例共享和 Dispose 边界。
3. 先设计 `IAgentFactory` 的输入模型，不立即改造全部命令。
4. 选择 Chat 命令作为试点，暂时保留其他命令的现有创建方式。
5. 验证 `AgentBuilder` 是否适合改为兼容层；如果不适合，再考虑引入新的公共 API。

## 9. 当前结论

统一 DI 在当前 CLI 中是可行的，且项目已经具备必要基础：CLI 已引用 `Microsoft.Extensions.Hosting`，核心服务也大量使用 `Microsoft.Extensions.DependencyInjection`。

但第一步不应直接把所有 `new` 替换成注册代码。真正需要先解决的是生命周期边界：

- Root Host 管理进程级资源；
- Chat Session 管理动态注册表和会话状态；
- Agent 管理当前模型运行时；
- Turn 管理 Loop、工具执行器和取消操作。

如果这四层边界确定，`AgentBuilder` 才能从“局部容器构建器”收敛为清晰的 Agent 创建 API；否则只是把现有的手动创建移动到另一个更大的注册文件中。

## 10. System.CommandLine 与 DI 的可行性

### 10.1 当前版本与原生能力

CLI 当前使用 `System.CommandLine 2.0.0-beta4.22272.1`。

该版本没有像 ASP.NET Core 那样直接支持 Microsoft DI 的命令类构造函数注入。`SetHandler` 主要负责绑定命令行选项、参数和 `InvocationContext`，不会自动从 `IServiceProvider` 创建 `ChatCommand` 或其他命令对象。

但 System.CommandLine 自身提供了可用的桥接点：

- `InvocationContext` 可访问当前解析结果和取消令牌；
- `InvocationContext.BindingContext` 是当前调用的绑定上下文；
- `BindingContext` 实现 `IServiceProvider`，并支持 `AddService` 注册服务；
- Handler 可以使用 `InvocationContext` 获取当前调用所需的服务。

参考官方 beta4 源码：

- [BindingContext.cs](https://raw.githubusercontent.com/dotnet/command-line-api/2.0.0-beta4.22272.1/src/System.CommandLine/Binding/BindingContext.cs)
- [InvocationContext.cs](https://raw.githubusercontent.com/dotnet/command-line-api/2.0.0-beta4.22272.1/src/System.CommandLine/Invocation/InvocationContext.cs)
- [ICommandHandler.InvokeAsync API](https://learn.microsoft.com/en-us/dotnet/api/system.commandline.invocation.icommandhandler.invokeasync?view=net-9.0-pp)

这里的 `BindingContext` 是 System.CommandLine 自己的绑定容器，不会自动等同于 Microsoft.Extensions.DependencyInjection 的根容器。因此我们需要显式建立桥接，而不是期待框架自动完成依赖注入。

### 10.2 推荐的集成方案

推荐采用“System.CommandLine 负责解析，Host DI 负责创建应用服务”的模式：

```text
Program.cs
└── HostApplicationBuilder
    └── Root IServiceProvider
        └── System.CommandLine Handler
            └── 创建当前命令的 Scope
                └── 解析 IChatApplication / ICommandService
```

Handler 的职责保持很小：

1. 从 `InvocationContext` 获取命令行参数和取消令牌；
2. 创建当前命令或会话所需的 DI Scope；
3. 从 Scope 中解析应用服务；
4. 将参数传给应用服务并返回退出码。

示意代码：

```csharp
command.SetHandler(async (InvocationContext context) =>
{
    await using var scope = rootServices.CreateAsyncScope();

    var application = scope.ServiceProvider
        .GetRequiredService<IChatApplication>();

    return await application.RunAsync(
        context.ParseResult,
        context.GetCancellationToken());
});
```

也可以通过 invocation middleware 将 Host 的 `IServiceProvider` 注册进 `BindingContext`，让 Handler 从 `context.BindingContext` 取出根容器。但这更适合作为 CLI 适配层，业务服务不应依赖 System.CommandLine 的 `BindingContext` 类型。

### 10.3 对 InsightaAI 的落地边界

建议逐步引入以下抽象：

- `IChatApplication`：负责 Chat 命令的完整用例，包括配置校验、会话恢复、聊天循环和退出码；
- `IAgentFactory`：根据当前模型和会话上下文创建 Agent，不创建根容器；
- `ICommandService` 或对应的命令应用服务：为 `sessions`、`skills`、`mcp`、`config` 等非 Chat 命令承载业务逻辑；
- `AddInsightaCli()`：注册 CLI 应用服务和命令适配器；
- `AddInsightaCore()`：注册 Agent、LLM、Storage、Skill、MCP 等核心服务。

最终结构可以是：

```text
Program.cs
├── 创建 HostApplicationBuilder
├── AddInsightaCore()
├── AddInsightaCli()
├── 构建 Host
└── 构建 System.CommandLine 命令树
    └── Handler 内解析 Scope 中的应用服务
```

命令类不应持有一组基础设施依赖并自行创建对象；命令定义可以捕获根容器或 Scope 工厂，但真正的 `ChatSession`、`Agent` 和动态注册表应在命令执行时创建。

### 10.4 版本策略

当前项目仍处于 beta4 API。官方文档已经展示了 beta5 及后续版本的解析和调用 API 变化，因此不建议把 DI 探索和 `System.CommandLine` 升级混在同一个步骤中。

建议顺序是：

1. 在现有 beta4 API 上验证 Host、Handler 和 Scope 的桥接；
2. 完成一个 Chat 命令的最小迁移；
3. 补充命令执行和 Scope Dispose 测试；
4. 再单独评估升级到稳定版 `System.CommandLine 2.0.0` 的收益和迁移成本。

### 10.5 结论

System.CommandLine 不提供“自动发现并构造 DI 命令类”的开关，但其 `InvocationContext` 和 `BindingContext` 足以支持统一 DI。对当前项目而言，最稳妥的解决方案不是替换命令框架，而是在 `Program.cs` 建立 Host，在 Handler 中创建作用域，并将实际业务交给从 DI 解析出的应用服务。

这样可以保留现有命令行语法和解析能力，同时逐步消除 `ChatCommand` 中的手动对象创建，并为 `AgentBuilder` 转型为 `IAgentFactory` 或兼容层留下空间。
