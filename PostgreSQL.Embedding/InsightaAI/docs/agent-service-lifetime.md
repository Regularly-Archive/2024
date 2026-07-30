# Agent 服务生命周期约定

## 适用范围

本约定适用于 `AgentBuilder` 通过 `ConfigureServices()` 注册的服务，以及工具和 Hook 从 `ToolExecutionContext.Services`、`AgentEventHookContext.Services` 获取的服务。

`AgentBuilder` 创建的是仅属于当前 `Agent` 的私有 `ServiceProvider`，不是 CLI Host 容器的子容器，也不会为 Turn、Round 或工具调用创建 DI scope。

## 支持的生命周期

| 生命周期 | 约定 |
| --- | --- |
| Singleton | 支持。在单个 Agent 内共享；当 `Agent.Dispose()` 时随私有 Provider 一并释放。它不是进程级 Singleton。 |
| Transient | 支持。每次从 `Services` 解析时创建；由 Agent 私有 Provider 在释放时处理其可释放实例。 |
| Scoped | 不支持。不要通过 `ConfigureServices()` 注册，也不要在工具或 Hook 中解析 Scoped 服务。 |

## 为什么不支持 Scoped

当前 Agent 的主交互流程会复用同一个 Provider；同时 `IAgentEventHook` 使用 fire-and-forget 调度。系统既没有稳定的 Turn/Tool scope，也不会跟踪或等待后台 Hook 完成。

在这种模型下，把 Scoped 服务从根 Provider 解析会使其实际存活到 Agent 被释放，违背 Scoped 的预期。为 DbContext、事务、请求上下文等资源创建伪 scope 还会引入后台 Hook 与 scope 释放竞争。

## Tool 与 Hook 的使用规则

- `ToolRegistry`、LLM、上下文管理器等核心 Agent 依赖由 Agent 显式持有；不要把 `Services` 当作核心流程的依赖注入入口。
- `Services` 仅用于可选的 Agent 级扩展服务，例如环境读取器、日志辅助组件或用户注入的无状态服务。
- Tool Hook 的权限检查在主流程中执行；Agent Event Hook 是后台任务。两者都只能依赖 Singleton 或 Transient 服务。
- 需要明确完成顺序、同一事务或请求级资源的工作，不应放入 fire-and-forget Hook；应设计为主流程中可等待的步骤。

## 未来演进

当项目需要真实 Scoped 服务时，应先定义并验证完整的作用域边界（例如 Host、Chat Session、Turn），再将 AgentBuilder 改为使用 Host 容器创建的 scope。不要在现有私有 Provider 上局部增加 scope，以免造成工具、Hook 和会话资源的生命周期语义不一致。
