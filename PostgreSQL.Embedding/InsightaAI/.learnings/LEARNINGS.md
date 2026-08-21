# Learnings

## [LRN-20260820-001] invocation-agent-identity

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: high
**Status**: pending
**Area**: backend

### Summary
CLI's fixed `cli-agent` identity must be a default only; an Invocation must be able to supply its Agent identity.

### Details
The current CLI AgentFactory always creates an `AgentConfig` with `Id = "cli-agent"`. This would collapse Explorer and future Subagent profiles into the same identity, making configuration, traces, usage records and policy decisions ambiguous.

### Suggested Action
Add an explicit, host-validated invocation profile/identity input to CLI Agent creation. Preserve `cli-agent` only as the interactive-chat fallback; do not allow an untrusted tool argument to arbitrarily override security or host configuration.

### Metadata
- Source: user_feedback
- Related Files: src/InsightaAI.Agent.Cli/Services/AgentFactory.cs, docs/agent-invocation-design.md
- Tags: invocation, subagent, cli, identity

---

## [LRN-20260820-002] subagent-static-preauthorization

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
Subagents must not depend on the interactive ToolPermissionHook; their capabilities are preauthorized at assembly time while SecurityPolicyHook remains mandatory.

### Details
An interactive confirmation belongs to the CLI/Desktop/Web host, not to an isolated child invocation. Treating a missing confirmation as automatic approval would make the child boundary ambiguous. The host must instead register only the preauthorized tool subset and omit ToolPermissionHook for that child.

### Suggested Action
Add an explicit AgentCreationOptions switch that defaults to interactive confirmation for the primary CLI agent. CliInsightaSubagentAdapter must disable that hook, while AgentFactory always registers SecurityPolicyHook.

### Metadata
- Source: user_feedback
- Related Files: src/InsightaAI.Agent.Cli/Services/AgentCreationOptions.cs, src/InsightaAI.Agent.Cli/Services/AgentFactory.cs, src/InsightaAI.Agent.Cli/Services/CliInsightaSubagentAdapter.cs
- Tags: subagent, security, permissions, hooks

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: `AgentCreationOptions.EnableInteractiveToolPermission` defaults to true; `CliInsightaSubagentAdapter` sets it false. AgentFactory conditionally registers ToolPermissionHook and always registers SecurityPolicyHook. Focused AgentFactory tests passed 4/4.

---

## [LRN-20260821-003] memory-service-and-memory-capability-separation

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
MemoryManager availability must be independent from automatic memory behavior and memory-tool exposure.

### Details
AgentFactory created a MemoryManager only when `EnableMemory` was true and then omitted `WithMemoryManager` entirely. This disconnected default CLI memory snapshots and memory tools. A subagent may need the host-provided service while still being prohibited from automatic memory injection and the public memory tools.

### Suggested Action
Always create and register `IMemoryManager` in AgentFactory. Keep `EnableMemory` as the gate for SessionMemoryHook, active snapshots and `MemoryTools.RegisterAll`.

### Metadata
- Source: user_feedback, review
- Related Files: src/InsightaAI.Agent.Cli/Services/AgentFactory.cs, tests/InsightaAI.Agent.Tests/AgentFactoryTests.cs
- Tags: memory, subagent, dependency-injection, capabilities

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: AgentFactory now always passes MemoryManager to AgentBuilder; focused tests assert default memory tools and service registration while restricted agents expose neither memory tool.

---

## [LRN-20260821-004] subagent-restrictions-at-tool-boundary

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
Subagent capability restrictions should hide and block tools without removing shared runtime infrastructure from DI.

### Details
`AgentRuntimeCapabilities` currently disables Skill/MCP prompt content, memory behavior and project instructions in addition to tool registration. This conflates infrastructure availability, prompt composition and tool authorization. A child Agent must retain the host-provided services, while its effective ToolRegistry determines what it can ask the LLM to invoke.

### Suggested Action
Always register Skill/MCP/Memory infrastructure and their built-in tools, then use `AgentConfig.ExcludedToolNames` to restrict child exposure and execution. Make dynamic prompt sections conditional on the corresponding effective tool availability, and keep project instruction inclusion as an explicit prompt/profile choice.

### Metadata
- Source: user_feedback
- Related Files: src/InsightaAI.Agent/Agent.cs, src/InsightaAI.Agent.Cli/Services/CliInsightaSubagentAdapter.cs, src/InsightaAI.Agent/Context/SystemPrompt/SystemPromptBuilder.cs
- Tags: subagent, tools, skills, mcp, memory, prompt, dependency-injection

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Removed AgentRuntimeCapabilities. Subagent tool-group requests now become ExcludedToolNames; Skill/MCP/Memory services and default memory behavior remain available. Agent prompt sections follow effective tool availability, while project instructions use a separate Definition option.

---

## [LRN-20260821-005] subagent-audit-and-adapter-test-boundaries

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: high
**Status**: in_progress
**Area**: backend

### Summary
Subagent security boundaries must be tested through the CLI adapter, and invocation identity must be created before execution and remain traceable to child-session storage.

### Details
Review identified an untested parent-session reuse path and an invocation result ID generated only after execution. The effective tool boundary is implemented in ToolRegistry, but the adapter is the composition point that must prove session isolation, whitelist intersection, exclusions and cancellation behavior.

### Suggested Action
Reject a parent session supplied as a child session, create a traceable invocation ID before execution, remove stale cancellation mapping, and add focused CLI-adapter tests for security-critical paths.

### Metadata
- Source: user_feedback, review
- Related Files: src/InsightaAI.Agent.Cli/Services/CliInsightaSubagentAdapter.cs, src/InsightaAI.Agents.Subagents/Invocation
- Tags: subagent, audit, session-isolation, tests, cancellation

---
