# Agent Evaluation Suite

## Purpose

Unit and integration tests verify local contracts. The evaluation suite verifies that a
real `Agent` invocation still completes a representative task after changes to the loop,
tools, hooks, context, storage, or prompts.

It is a developer and CI tool. It is deliberately **not** an `insighta` CLI command and
is not packed into the global CLI tool.

## Entry point

The standalone runner is at `tools/InsightaAI.Agent.Evals`:

```powershell
dotnet run --project tools/InsightaAI.Agent.Evals -- --list
dotnet run --project tools/InsightaAI.Agent.Evals --
dotnet run --project tools/InsightaAI.Agent.Evals -- --scenario replay-tool-loop
dotnet run --project tools/InsightaAI.Agent.Evals -- --report artifacts/evals/report.json
```

Without `--report`, the runner prints a JSON report to standard output. CI supplies an
explicit artifact path; local runs should avoid writing generated reports into the
working tree unless a report is intentionally being retained. With no `--scenario`, the
runner executes every discovered scenario.

## Execution model

```text
Scenario JSON
  → recorded LLM transcript
  → real AgentBuilder / Agent Loop / ToolRegistry
  → Agent event stream
  → contract assertions + metrics
  → JSON report
```

Replay scenarios are deterministic: only the LLM decisions are recorded. The Agent Loop,
tools, hooks, result processing, storage, and event pipeline are the real implementations.
They are suitable for local development and every CI run because they have no provider
cost or output variance.

Live scenarios are a later mode. They will run the same scenario contracts against a
pinned model and isolated fixture workspace; they must not compare assistant prose
byte-for-byte and should run on a controlled schedule rather than every commit.

## Scenario contract

Scenarios live under `tools/InsightaAI.Agent.Evals/Scenarios/`. A replay scenario declares:

- prompt, optional isolated fixture directory, and fixture tools;
- the recorded LLM steps (text and tool calls);
- expected final Agent status, ordered tool calls, and optional final text.

`replay-tool-loop` proves the minimal vertical slice: a recorded tool call
executes through the real `ToolRegistry`, its result returns to the real Agent Loop, and a
second LLM round produces the final answer.

`replay-secret-redaction-edit` copies a fixture to a fresh temporary directory, invokes the
real `read_file` and `edit_file` tools, and verifies both sides of the contract: public Agent
events do not leak fixture secrets, the redacted persisted tool result contains `[REDACTED]`,
while the original file keeps its secret values and the requested non-sensitive edit succeeds.
Fixture paths in a recorded transcript use `${fixture}` and are expanded only inside that
isolated directory. Artifacts use the same temporary workspace and are removed after the run.

`replay-large-tool-result` returns more than the normal persistence threshold from a default
fixture tool. It proves that the real result processor writes an artifact and exposes its
reference through `AgentToolEndEvent`, rather than retaining the complete large result in the
conversation projection.

`replay-security-policy-deny` uses a recorded `bash` call against a registered fixture tool.
The real `SecurityPolicyHook` must reject it before that implementation executes; the scenario
expects the resulting tool error and policy explanation while the Agent still completes its turn.

`replay-allow-always-non-bypass` registers a simulated permission hook that approves `bash` for
the whole session, then records one non-denied and one denied command. The non-denied command
executes normally (proving the pre-approval works), while the denied command is still rejected
by the real `SecurityPolicyHook` (proving deny rules cannot be bypassed by session-level
permissions).

`replay-artifact-rehydration` persists messages through the real `JsonlMessageStorage`. After
the Agent completes its turn, the runner reads the restored session records and verifies that
the persisted `ToolResultState` still references the same artifact exposed on `AgentToolEndEvent`,
and that the referenced file contains the full original tool output.

`replay-context-compaction` overrides the context window to a deliberately small budget and
replays three large tool results. The real `MicroCompactStrategy` degrades older results while
the Agent completes its turn; the scenario asserts that a real `AgentContextCompactedEvent` fired
with the `MicroCompact` strategy.

`replay-subagent-delegation` registers a `DelegateTool` backed by a stub `IAgentDelegationHandler`.
Because `DelegateTool` declares `PreferPersistence`, even a short subagent result is persisted as
an artifact; the scenario verifies the artifact reference, the full output in the persisted file,
and that text beyond the event preview boundary appears only in the artifact.

## Report and gates

Every report includes pass/fail state plus LLM request count, turns, rounds, tool calls,
tool errors, Agent errors, and duration. A scenario contract failure is a hard failure.
Metric changes are initially diagnostic signals, not universal thresholds: token count,
rounds, and duration only become gates after a baseline and an explicit task-specific
budget are approved.

## Planned coverage

1. Replay tool loop (implemented).
2. Secret redaction while preserving exact file editing (implemented).
3. Large tool output persistence, artifact reference, and historical rehydration (implemented).
4. Security-policy rejection before tool execution, including allow-always non-bypass (implemented).
5. Context compaction via real `MicroCompactStrategy` (implemented).
6. Subagent delegation with persisted handoff result (implemented); isolated session state for
   real subagents remains a future live/isolated-workspace concern.

An optional future `$insighta-eval` Skill may help an agent choose scenarios, execute this
runner, compare reports, and summarize failures. The Skill must not become the source of
scenario definitions or evaluation policy.
