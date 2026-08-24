# Subagents 与 Invocation 设计

## 目标

`Invocation` 表示一次边界明确、可取消、可审计的子 Agent 工作单元。它服务于两类需求：

- 命名的长期 Subagent，例如 Explorer；定义可以来自全局 `~/.insighta/subagents/{id}/`，也可以来自数据库或服务端。
- 临时的 DAG 工作单元；定义由编排计划内联提供，任务完成后不必持久化。

两类需求共享同一份 `SubagentDefinition`。是否持久化是 Definition 的来源和 Catalog 的责任，不是模型类型本身的属性。

## 分层与依赖

```text
InsightaAI.LLM
      ↑
InsightaAI.Agent                 单 Agent 运行时
      ↑
InsightaAI.Agents.Subagents      定义、调用契约、目录、adapter、dispatcher
      ↑
InsightaAI.Agents.Orchestrator   DAG / Team
      ↑
CLI / Desktop / Web / Server     组合根与宿主 adapter
```

`InsightaAI.Agent` 保持单 Agent：不包含 Subagent、Catalog 或编排依赖。`InsightaAI.Agents.Subagents` 是无宿主契约包，既不引用 CLI，也不引用 Agent 运行时。Orchestrator 只依赖 dispatcher，不能直接构造 `Agent`。

## 通用模型

`SubagentDefinition` 是来源无关的抽象，只有稳定身份、显示名、描述和 `AdapterKey`。`InsightaSubagentDefinition` 是当前唯一落地的实现，包含模型、指令、预算和工具白名单。

`SubagentInvocationRequest` 包含已解析的 Definition、任务输入、用户/会话/父调用关联和可选的进一步工具收紧。它不携带 `AgentConfig`、`AgentResult` 或父会话全文。父子会话是两条独立历史线；`ParentSessionId` / `ParentInvocationId` 只用于导航、审计和保留策略。

`SubagentInvocationResult` 是宿主无关结果：状态、输出、错误、InvocationId 与宿主可选的 SessionId。它不伪造 token usage，也不泄漏特定 Agent 运行时对象。

## 调度与适配

`ISubagentAdapter` 声明可处理的 Definition 类型，并执行调用；`SubagentDispatcher` 必须路由到恰好一个 adapter，缺失或歧义都明确失败。

第一期仅实现 CLI 的 `CliInsightaSubagentAdapter`：

1. 验证宿主已提供的 user ID；
2. 创建或恢复独立子会话，并只加载该子会话历史；
3. 将 `InsightaSubagentDefinition` 映射为内部 `AgentConfig`；
4. 以 Definition 工具白名单为上限，并和请求级白名单求交集；
5. 由既有 `AgentFactory` 装配 Skill、MCP、Hook、安全策略、Telemetry 和 Memory；
6. 运行并释放子 Agent，再映射为通用结果。

`AgentConfig` 只在第 3 步存在。CLI 仍保留 user ID、工作目录和 deny rule 的最终决定权，Definition 不能扩大这些权限。

`InsightaSubagentCapabilities` 是 Definition 的声明式工具组请求；当前提供 Skill、MCP 与 Memory 三组。CLI 将未请求、或宿主当前不可用的工具组写入子 Agent 的 `AgentConfig.ExcludedToolNames`，Definition 因而只能收紧工具、不能扩权。SkillRegistry、McpRegistry、MemoryManager 与默认记忆行为始终保留在 Agent 私有 DI 中；`ToolRegistry` 负责同时隐藏和拒绝被排除工具。Skill/MCP 的动态 Prompt 区块以相应工具实际可用为条件，避免提示模型调用已排除的工具。CLI 还会根据最终排除组在 Layer 3 追加 Runtime constraints，明确覆盖 Layer 1 的通用 Skill/MCP 指引；这不是 descriptor 的静态文本。项目 `AGENTS.md` 不属于工具权限，改由 Definition 顶层 `includeProjectInstructions` 单独控制；当前三个预置子 Agent 均显式启用。

## 工具权限

Subagent 使用静态预授权，而不是交互式确认：Definition 的工具白名单先与宿主允许范围、再与调用级限制求交集，未注册的工具不可调用。`CliInsightaSubagentAdapter` 创建 Agent 时显式关闭 `ToolPermissionHook`；这表示子 Agent 没有确认能力，不表示它获得自动同意或无限权限。

主 Agent 通过核心 `delegate` 工具调用宿主提供的 `IAgentDelegationHandler`。CLI handler 将 `agent_id` 和有界 `task` 解析到本地 Catalog，并由宿主填充 user ID、父 session 和父 tool-call ID；子 Agent 工具白名单不含 `delegate`，因此第一期最大委派深度为 1。返回文本继续经过父 Agent 的 `ToolResultProcessor` 脱敏和上下文投影，且宿主最多向父 Agent 暴露 12,000 个字符。

`SecurityPolicyHook` 对主 Agent 和 Subagent 都必须注册。deny list、敏感路径等强制规则仍在每次工具调用前执行，且不能被预授权、AllowAlways 或 Definition 覆盖。高风险工具默认不进入 Subagent 白名单；若未来需要开放，应先提供更细粒度的受限工具或策略，而非继承主 Agent 的完整工具集。

## Catalog 与本地定义

`ISubagentCatalog` 只提供按 ID 查询和枚举命名 Definition；它不规定存储形式。第一期不急于抽象多个 provider：真正实现本地目录读取时，直接提供 `LocalSubagentCatalog` 即可。等数据库、远程或组合来源同时出现后，再把文件解析提取为 `LocalSubagentDefinitionProvider`。

CLI 已提供 `LocalSubagentCatalog`，默认使用全局 `~/.insighta/subagents/{id}/subagent.json`。Subagent 是可复用的独立工作流程，不绑定某一个项目或工作目录。descriptor 的 `id` 必须与目录名一致，避免路径混淆；找不到根目录时视为没有本地定义。仓库内的 `reviewer`、`explorer`、`planner` 是预置模板，安装/初始化流程负责将其提供到全局目录。

Subagent 契约、dispatcher 与本地 catalog 的单元测试位于独立项目 `tests/InsightaAI.Agents.Subagents.Tests/`；Orchestrator 如何委派 Subagent 的测试仍归属 `InsightaAI.Agents.Orchestrator.Tests`。

## 外部 Agent 边界

外部 Codex、Claude Code 等暂不实现。未来可新增新的 Definition 子类和 adapter；外部系统自己的工具、Skill、MCP 与会话协议由其 adapter 负责，Insighta 不假设能够注入或管理它们。

目前不为 Codex 非交互式调用设计命令行参数、进程模型或会话恢复协议。先以内部 Subagent 验证会话隔离、受控能力和编排调用，避免把未确认的外部约束固化到公共契约中。

## 后续顺序

1. 补 CLI adapter 的会话隔离、白名单交集和结果映射测试。
2. 实现全局 `~/.insighta/subagents/{id}/` 的 `LocalSubagentCatalog` 与 descriptor 格式。
3. 让 CLI 显式注册 dispatcher，并通过核心 `delegate` 工具提供受限的 named subagent 委派。
4. 再根据真实的外部 Agent 协议评估 adapter。
