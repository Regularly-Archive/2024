## [ERR-20260804-001] powershell-validation-quoting

**Logged**: 2026-08-04T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
PowerShell parsed a documentation verification command incorrectly because an embedded quote in the regular-expression argument was not escaped safely.

### Error
```text
字符串缺少终止符: ".
```

### Suggested Fix
Use single-quoted PowerShell arguments or separate simple search commands when verifying documentation text that contains quoted tokens.

### Metadata
- Reproducible: yes
- Related Files: docs/memory-system-design.md

### Resolution
- **Resolved**: 2026-08-04T00:00:00+08:00
- **Notes**: Replaced the complex expression with simple single-quoted checks.

---

## [ERR-20260805-001] powershell-rg-unescaped-braces

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A ripgrep alternation failed because a literal `{userId}` token was interpreted as an invalid regex quantifier.

### Error
```text
rg: regex parse error: repetition quantifier expects a valid decimal
```

### Context
- Command attempted to review memory-design terms with one combined ripgrep expression.
- Input contained the literal documentation path fragment `private/{userId}`.

### Suggested Fix
Use fixed-string search (`rg -F`) for literal documentation fragments, or escape braces before combining terms into a regex.

### Metadata
- Reproducible: yes
- Related Files: docs/memory-system-design.md
- See Also: ERR-20260804-001

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Switched the remaining review searches to individual fixed-string queries.

---

## [ERR-20260805-002] toolresult-definition-path

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Assumed the ToolResult type was in the LLM models directory instead of locating its actual definition first.

### Error
```text
Get-Content: ... InsightaAI.LLM\\Models\\ToolResult.cs ... path does not exist
```

### Suggested Fix
Use `rg -n "public sealed record ToolResult" src` to locate a type before opening a presumed file path.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Abstractions/ToolExecutionContext.cs

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Located the type in ToolExecutionContext.cs.

---

## [ERR-20260805-003] webfetch-link-rendering-recursion

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The DOM Markdown renderer recursively rendered an anchor element while trying to obtain its text.

### Error
```text
Stack overflow in WebFetchTool.AppendLink -> RenderContent -> RenderNode
```

### Suggested Fix
Render only the anchor's child nodes into a temporary builder before wrapping the resulting text in Markdown link syntax.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn/WebFetchTool.cs

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: AppendLink now renders child nodes only.

---

## [ERR-20260805-004] webfetch-body-root-rendering

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The renderer did not emit content when the selected root was the document body rather than an article element.

### Error
```text
Expected extracted Markdown content was absent when the fallback root was `body`.
```

### Suggested Fix
At the root level, render body child nodes directly; retain the normal element renderer for article and main roots.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn/WebFetchTool.cs

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: RenderContent now explicitly traverses body child nodes.

---

## [ERR-20260805-005] webfetch-empty-readonly-dictionary

**Logged**: 2026-08-05T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
An empty collection expression cannot construct an IReadOnlyDictionary constructor argument.

### Error
```text
CS9174: Cannot initialize type 'IReadOnlyDictionary<string, string>' with a collection expression because the type is not constructible.
```

### Suggested Fix
Use an explicit empty Dictionary when constructing a record whose parameter type is IReadOnlyDictionary.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn/WebFetchTool.cs

### Resolution
- **Resolved**: 2026-08-05T00:00:00+08:00
- **Notes**: Replaced the collection expression with `new Dictionary<string, string>()`.

---

## [ERR-20260820-001] functions-exec-tool-name

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Project status inspection used a nonexistent nested tool name.

### Error
```text
TypeError: tools.shell_command is not a function
```

### Context
- Attempted operation: inspect recent Git changes and TODO items through `functions.exec`.
- The available nested command is `tools.exec_command`, not `tools.shell_command`.

### Suggested Fix
Use `tools.exec_command` for terminal work invoked through `functions.exec`.

### Metadata
- Reproducible: yes
- Related Files: .learnings/ERRORS.md

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Corrected the command before any repository operation was performed.

---

## [ERR-20260820-002] git-index-lock-sandbox

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The sandbox could not create the parent repository Git index lock while staging documentation changes.

### Error
```text
fatal: Unable to create 'D:/Projects/2024/.git/index.lock': Permission denied
```

### Context
- Attempted operation: stage and commit `docs/TODO.md` and `.learnings/ERRORS.md`.
- The repository Git directory is above the writable workspace root.

### Suggested Fix
Use the approved elevated Git operation for repository index updates; retain explicit, scoped paths.

### Metadata
- Reproducible: yes
- Related Files: docs/TODO.md, .learnings/ERRORS.md

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Retried with elevated repository-index permission.

---

## [ERR-20260821-003] git-index-root-permission

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: config

### Summary
Git staging from the InsightaAI subdirectory cannot create the repository-level index lock under the parent workspace root.

### Error
```text
fatal: Unable to create 'D:/Projects/2024/.git/index.lock': Permission denied
```

### Suggested Fix
Run the authorized Git staging and commit operation with access to the parent repository `.git` directory.

### Metadata
- Reproducible: yes
- Related Files: D:/Projects/2024/.git/index

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Re-ran the authorized staging and commit operations with parent repository index access.

---

## [ERR-20260821-002] powershell-multiple-path-enumeration

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A diagnostic file listing passed two paths to `Get-ChildItem` without a separating parameter, causing the second path to be resolved relative to the first.

### Error
```text
Get-ChildItem : Could not find part of the path '...\\tests\\InsightaAI.Agents.Subagents.Tests\\src'.
```

### Suggested Fix
Use separate `Get-ChildItem` invocations or the explicit `-Path` array parameter when inspecting multiple roots.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agents.Subagents.Tests

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: The error occurred only in a read-only diagnostic command and did not affect source or test results.

---

## [ERR-20260821-001] subagent-test-agent-namespace-shadowing

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The CLI subagent adapter test namespace contains `Agents`, which shadowed the imported `InsightaAI.Agent.Agent` runtime type.

### Error
```text
CS0118: 'Agent' is a namespace but is used like a type.
```

### Suggested Fix
Use the fully qualified `InsightaAI.Agent.Agent` type in test implementations of `IAgentFactory`.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agents.Subagents.Tests/Invocation/CliInsightaSubagentAdapterTests.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Qualified the test factory return type and construction site.

---

## [ERR-20260820-003] orchestrator-test-team-namespace

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new Orchestrator AgentNode test referenced `Team` without importing its Core namespace.

### Error
```text
CS0246: The type or namespace name 'Team' could not be found
```

### Suggested Fix
Import `InsightaAI.Agents.Orchestrator.Core` in the test file.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agents.Orchestrator.Tests/Core/OrchestratorTests.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Added the missing test namespace before rerunning the focused suite.

---

## [ERR-20260820-004] session-storage-test-patch-context

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A combined session-storage patch assumed an incorrect JSONL test-class declaration and was rejected before any files changed.

### Error
```text
apply_patch verification failed: expected JsonlMessageStorageTests : IDisposable declaration was not found
```

### Suggested Fix
Read the exact test fixture declaration and apply the production and test edits in smaller patches.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/Storage/JsonlMessageStorageTests.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: No partial edit occurred; the fixture will be inspected before retrying.

---

## [ERR-20260820-005] agent-creation-options-read-path

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A source inspection used `InsightaAI.Agent/Cli` instead of the actual `InsightaAI.Agent.Cli` project path.

### Error
```text
Get-Content: path src\\InsightaAI.Agent\\Cli\\Services\\AgentCreationOptions.cs was not found
```

### Suggested Fix
Use the exact project directory `src/InsightaAI.Agent.Cli` when reading CLI sources.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent.Cli/Services/AgentCreationOptions.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: The required storage conversion source was read successfully; the incorrect secondary path was not used.

---

## [ERR-20260820-006] orchestrator-test-file-path

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An inspection assumed the Orchestrator test file was at the test-project root instead of its `Core/` folder.

### Error
```text
Get-Content: ... tests\\InsightaAI.Agents.Orchestrator.Tests\\OrchestratorTests.cs path was not found
```

### Suggested Fix
Use `rg --files` to locate nested test files before reading a specific path.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agents.Orchestrator.Tests/Core/OrchestratorTests.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Located and inspected the test under `Core/` before editing.

---

## [ERR-20260820-007] apply-patch-replace-file-operation

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
An attempt to delete and add the same design document in one patch was rejected by the patch tool.

### Error
```text
apply_patch verification failed: invalid patch: multiple operations target docs/agent-invocation-design.md
```

### Suggested Fix
Replace a complete file through separate delete and add patches, or use a single verified update hunk.

### Metadata
- Reproducible: yes
- Related Files: docs/agent-invocation-design.md

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: The rejected patch was atomic; replacement will be split into two operations.

---

## [ERR-20260820-008] todo-invocation-block-context

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The TODO Invocation block differed from the assumed historical wording, so its broad replacement was rejected.

### Error
```text
apply_patch verification failed: Failed to find expected lines in docs/TODO.md
```

### Suggested Fix
Anchor a small documentation update on a unique verified ASCII link rather than replacing an assumed multilingual block.

### Metadata
- Reproducible: yes
- Related Files: docs/TODO.md

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: The rejected patch was atomic; the follow-up uses the design-document link as its anchor.

---

## [ERR-20260820-009] new-project-no-restore

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first focused test run used `--no-restore` after adding a new project reference, so NuGet had no project metadata for it.

### Error
```text
NU1105: Unable to find project information for InsightaAI.Agents.Subagents.csproj
```

### Suggested Fix
Run restore once after adding a project, then use `--no-restore` for subsequent focused test runs.

### Metadata
- Reproducible: yes
- Related Files: InsightaAI.sln, src/InsightaAI.Agents.Subagents/InsightaAI.Agents.Subagents.csproj

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: A restore will refresh the solution graph before the rerun.

---

## [ERR-20260820-010] nuget-restore-sandbox-network

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Sandboxed restore could not reach NuGet's repository-signature endpoint after the new project required a restore.

### Error
```text
NU1301: Unable to retrieve repository signature information from api.nuget.org
```

### Suggested Fix
Retry the required restore with the scoped elevated network permission, then rerun focused tests without restore.

### Metadata
- Reproducible: yes
- Related Files: InsightaAI.sln

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Escalated restore is requested only to refresh the solution dependency graph.

---

## [ERR-20260820-011] orchestrator-toolregistry-using

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
Removing obsolete Invocation imports also removed the `ToolRegistry` namespace used by Orchestrator's retained compatibility method.

### Error
```text
CS0246: The type or namespace name 'ToolRegistry' could not be found
```

### Suggested Fix
Keep `InsightaAI.Agent.Abstractions` imported until `RunAgentAsync` is removed or migrated.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agents.Orchestrator/Core/Orchestrator.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Restored only the required abstraction namespace.

---

## [ERR-20260820-012] powershell-bash-or-syntax

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
A final verification command used Bash `||` syntax in the PowerShell shell.

### Error
```text
ParserError: The token '||' is not a valid statement separator in this version.
```

### Suggested Fix
Use separate commands or PowerShell conditional syntax when an optional ripgrep search may return no matches.

### Metadata
- Reproducible: yes
- Related Files: none

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: No repository operation ran; verification will use separate commands.

---

## [ERR-20260820-013] agentfactory-test-toolhook-namespace

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new AgentFactory composition test referenced `IToolHook` without importing its Hooks namespace.

### Error
```text
CS0246: The type or namespace name 'IToolHook' could not be found
```

### Suggested Fix
Import `InsightaAI.Agent.Hooks` in tests that inspect the Agent tool-hook collection.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/AgentFactoryTests.cs

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: Add the test-only import and rerun the focused suite.

---

## [ERR-20260820-014] local-subagent-directory-absent

**Logged**: 2026-08-20T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: config

### Summary
Initial discovery confirmed that the project has no `.insighta` directory yet.

### Error
```text
rg: .insighta: The system cannot find the file specified
```

### Suggested Fix
Treat the absent optional local catalog directory as first-run state; create it through the scoped catalog implementation and descriptors.

### Metadata
- Reproducible: yes
- Related Files: .insighta/subagents

### Resolution
- **Resolved**: 2026-08-20T00:00:00+08:00
- **Notes**: The directory will be added as the project-local named Subagent catalog.

---

## [ERR-20260821-001] subagent-test-solution-context

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The first combined patch for the Subagents test project assumed an outdated solution nested-project entry.

### Error
```text
apply_patch verification failed: Failed to find expected lines in InsightaAI.sln
```

### Suggested Fix
Read the exact solution project and nested-project sections, then add the project in focused patches.

### Metadata
- Reproducible: yes
- Related Files: InsightaAI.sln

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: The failed patch was atomic; no test project or solution changes were applied.

---

## [ERR-20260821-002] chat-subagent-integration-context

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A broad ChatApplication integration patch used an imprecise comment context and was rejected atomically.

### Error
```text
apply_patch verification failed: Failed to find expected lines in ChatApplication.cs
```

### Suggested Fix
Apply ChatApplication changes around stable method calls and object initializer fields in focused patches.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent.Cli/Services/ChatApplication.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: No production file changed; integration will be split into verified edits.

---

## [ERR-20260821-003] delegate-tool-test-handler-namespace

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The migrated DelegateTool integration test did not import the CLI service namespace that contains its delegation handler.

### Error
```text
CS0246: The type or namespace name 'CliSubagentDelegationHandler' could not be found
```

### Suggested Fix
Add `using InsightaAI.Agent.Cli.Services;` to the focused test fixture and rerun the suite.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agents.Subagents.Tests/Tools/SubagentToolTests.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Added the test-only using directive.

---

## [ERR-20260821-004] chatapplication-path-assumption

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A source check assumed ChatApplication was at the CLI project root instead of locating its actual services path.

### Error
```text
rg: src\\InsightaAI.Agent.Cli\\ChatApplication.cs: The system cannot find the file specified
```

### Suggested Fix
Use `rg --files src/InsightaAI.Agent.Cli` before addressing a concrete CLI source path.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent.Cli/Services/ChatApplication.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Subsequent verification uses the confirmed Services path.

---

## [ERR-20260821-005] mcp-tools-source-path-assumption

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A capability review assumed the MCP tool registrar was located under the Mcp source directory.

### Error
```text
Get-Content: src\\InsightaAI.Agent\\Mcp\\McpTools.cs path was not found
```

### Suggested Fix
Use repository discovery before opening a colocated tool-registration implementation.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Follow-up inspection uses `rg --files` to locate the registrar.

---

## [ERR-20260821-006] mcp-tools-built-in-path-assumption

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
After discovery identified the BuiltIn location, the follow-up read still used the parent Tools path.

### Error
```text
Get-Content: src\\InsightaAI.Agent\\Tools\\McpTools.cs path was not found
```

### Suggested Fix
Use the discovered path exactly: `src/InsightaAI.Agent/Tools/BuiltIn/McpTools.cs`.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn/McpTools.cs
- See Also: ERR-20260821-005

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: The missing file was not modified; inspection resumes at the confirmed path.

---

## [ERR-20260821-007] removed-runtime-namespace-using

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
The runtime-capability type was removed but AgentBuilder retained its namespace import.

### Error
```text
CS0234: InsightaAI.Agent.Runtime does not exist in AgentBuilder.cs
```

### Suggested Fix
After removing a namespace-owned type, run a repository reference search and remove all remaining imports before testing.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/AgentBuilder.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Removed the obsolete using directive; subsequent verification checks for remaining Runtime references.

---

## [ERR-20260821-008] agentfactory-test-mcp-namespace

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new DI retention assertion referenced McpRegistry without importing its namespace.

### Error
```text
CS0246: The type or namespace name 'McpRegistry' could not be found
```

### Suggested Fix
Import `InsightaAI.Agent.Mcp` in the AgentFactory test fixture.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/AgentFactoryTests.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Added the test-only namespace reference.

---

## [ERR-20260821-009] agentfactory-test-skill-service-type

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The infrastructure-retention test requested SkillRegistry by its concrete type although AgentBuilder intentionally registers it as ISkillRegistry.

### Error
```text
Assert.NotNull() Failure: GetService<SkillRegistry>() returned null
```

### Suggested Fix
Assert the public DI contract (`ISkillRegistry`) rather than an unregistered concrete implementation.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/AgentFactoryTests.cs

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: The AgentFactory registration was correct; only the test requested the wrong service type.

---

## [ERR-20260821-010] insighta-version-option-absent

**Logged**: 2026-08-21T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
The installed Insighta CLI does not expose a `--version` command-line option.

### Error
```text
Unrecognized command or argument '--version'.
```

### Suggested Fix
Verify a local global-tool installation through its store DLL timestamp or package metadata rather than invoking an unsupported version option.

### Metadata
- Reproducible: yes
- Related Files: build-insighta.ps1

### Resolution
- **Resolved**: 2026-08-21T00:00:00+08:00
- **Notes**: Installation verification now reads the installed CLI assembly metadata.

---

## [ERR-20260818-004] file-edit-test-context-namespace

**Logged**: 2026-08-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new FileEditTool placeholder test omitted the namespace that defines ToolExecutionContext.

### Error
```text
CS0246: ToolExecutionContext could not be found
```

### Suggested Fix
Import InsightaAI.Agent.Abstractions in direct built-in tool tests that construct execution contexts.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/Tools/FileEditToolTests.cs

### Resolution
- **Resolved**: 2026-08-18T00:00:00+08:00
- **Notes**: Added the test-only namespace reference.

---

## [ERR-20260818-003] combined-security-patch-context

**Logged**: 2026-08-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A combined implementation patch used an assumed using-block context and was atomically rejected before any change was applied.

### Error
```text
apply_patch verification failed: Failed to find expected lines in FileWriteTool.cs
```

### Suggested Fix
Read exact file headers and apply focused patches per component when modifying multiple security boundaries.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn/FileWriteTool.cs

### Resolution
- **Resolved**: 2026-08-18T00:00:00+08:00
- **Notes**: The rejection was atomic; no business files changed.

---

## [ERR-20260818-002] optional-file-edit-test-search

**Logged**: 2026-08-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An optional search for an existing FileEditTool test class returned no matches and made the combined inspection command exit non-zero.

### Error
```text
rg exit code: 1
```

### Suggested Fix
Treat absent optional test files as a discovery result and create focused coverage in the existing tools test area.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/Tools

### Resolution
- **Resolved**: 2026-08-18T00:00:00+08:00
- **Notes**: Added coverage will be placed with the tool tests instead of assuming a pre-existing class.

---

## [ERR-20260818-001] file-read-state-path-assumption

**Logged**: 2026-08-18T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
A source inspection assumed `FileReadState` was stored in its own file, but the type is colocated elsewhere.

### Error
```text
FileReadState.cs: The system cannot find the file specified.
```

### Suggested Fix
Use repository file discovery before requesting a concrete source path for a colocated type.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Tools/BuiltIn

### Resolution
- **Resolved**: 2026-08-18T00:00:00+08:00
- **Notes**: The relevant `FileReadTool` and `FileEditTool` paths were already inspected successfully.

---

## [ERR-20260817-011] secret-redaction-sample-leak

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: high
**Status**: resolved
**Area**: backend

### Summary
The installed CLI redaction pipeline left one sensitive sample value visible during a non-printing regression check.

### Error
```text
RESULT=FAIL LEAK_COUNT=1 REDACTION_MARKERS=19
```

### Suggested Fix
Identify the source format and key without logging the value, then add the smallest parser or pattern correction with a regression test.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Security/BuiltInSecretRedactors.cs

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Added explicit `SECRET_KEY` recognition and prevented the line matcher from crossing YAML line boundaries. Focused tests passed 8/8; the reinstalled CLI redacted all 16 checked values across five supplied file formats.

---

## [ERR-20260817-010] dotnet-script-availability-check

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The local verification probe treated an unavailable optional `dotnet-script` command as a failed combined environment check.

### Error
```text
dotnet-script.exe
Exit code: 1
```

### Suggested Fix
Use the existing `D:\test\asmcheck` project with `dotnet run`; do not require optional scripting tooling for secret-redaction verification.

### Metadata
- Reproducible: yes
- Related Files: D:\test\asmcheck

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Switched to the supplied verification helper.

---

## [ERR-20260814-001] grafana-provisioning-path

**Logged**: 2026-08-14T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Assumed Grafana dashboard provisioning used a `dashboards.yml` filename instead of locating the actual configuration first.

### Error
```text
Get-Content: ... provisioning\\dashboards\\dashboards.yml ... path does not exist
```

### Suggested Fix
Use `rg --files tools/observability` to locate provisioned Grafana configuration before opening a presumed path.

### Metadata
- Reproducible: yes
- Related Files: tools/observability/grafana/provisioning/dashboards/dashboards.yaml

### Resolution
- **Resolved**: 2026-08-14T00:00:00+08:00
- **Notes**: Confirmed the provisioner uses `dashboards.yaml` and automatically loads all JSON files in the dashboard directory.

---

## [ERR-20260814-002] grafana-search-api-authentication

**Logged**: 2026-08-14T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: infra

### Summary
Attempted to list provisioned dashboards through Grafana's search API without credentials after a successful health check.

### Error
```text
401 Unauthorized from /api/search
```

### Suggested Fix
Treat `/api/health` as the unauthenticated liveness check; use Grafana credentials only when dashboard API enumeration is necessary.

### Metadata
- Reproducible: yes
- Related Files: tools/observability/docker-compose.yml

### Resolution
- **Resolved**: 2026-08-14T00:00:00+08:00
- **Notes**: Provisioned dashboard JSON and Prometheus queries were validated before Grafana restart.

---

## [ERR-20260817-001] rg-file-name-filter

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
An optional file-name filter returned no matches and made a successful source search exit with code 1.

### Error
```text
rg exit code: 1
```

### Suggested Fix
Keep discovery searches separate from optional file-name filtering, or handle an empty filter result explicitly.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Used the already located paths for the subsequent inspection.

---

## [ERR-20260817-002] rg-optional-test-filter

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
An optional test-file filter had no matches and caused an otherwise useful inspection command to return code 1.

### Error
```text
rg exit code: 1
```

### Suggested Fix
Run broad test discovery independently, then open only confirmed paths.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: The Agent test project was located and will be used directly.

---

## [ERR-20260817-003] apply-patch-document-context

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A combined patch used an imprecise documentation context line and therefore was rejected atomically.

### Error
```text
apply_patch verification failed: Failed to find expected lines
```

### Suggested Fix
Inspect the exact local paragraph before applying documentation edits, and keep code and documentation patches separate.

### Metadata
- Reproducible: yes
- Related Files: docs/agent-security-design.md

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: No files were changed by the rejected patch; follow-up edits use verified context.

---

## [ERR-20260817-004] apply-patch-todo-context

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
A TODO patch was rejected because the expected block did not match the file's exact historical text.

### Error
```text
apply_patch verification failed: Failed to find expected lines
```

### Suggested Fix
Read UTF-8 source text around the exact heading before replacing a documentation block.

### Metadata
- Reproducible: yes
- Related Files: docs/TODO.md

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: The rejected patch was atomic; a follow-up uses verified text.

---

## [ERR-20260817-005] agent-tool-end-event-property

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
A new test assumed the result property name on AgentToolEndEvent instead of locating the event definition.

### Error
```text
CS1061: 'AgentToolEndEvent' does not contain a definition for 'Result'
```

### Suggested Fix
Locate event contracts before asserting their payload fields in integration tests.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/SecurityPolicyHookTests.cs

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: The follow-up reads the event contract and updates only the test assertion.

---

## [ERR-20260817-006] web-tool-result-shape

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: docs

### Summary
The web search wrapper returned a serialized value instead of the expected content-block object.

### Error
```text
TypeError: r.content is not iterable
```

### Suggested Fix
Treat web tool responses defensively and render the returned value directly when its content shape is unavailable.

### Metadata
- Reproducible: unknown
- Related Files: docs/agent-security-design.md

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Retried the same source query with direct result rendering.

---

## [ERR-20260817-007] json-redactor-namespace

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: backend

### Summary
The new JSON redactor caught JsonException without importing its namespace.

### Error
```text
CS0246: The type or namespace name 'JsonException' could not be found
```

### Suggested Fix
Add the explicit System.Text.Json using whenever catching JsonException alongside JsonNode.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Security/BuiltInSecretRedactors.cs

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Added the missing using and reran the targeted test set.

---

## [ERR-20260817-008] redactor-format-edge-cases

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: medium
**Status**: resolved
**Area**: tests

### Summary
Initial secret-redaction tests exposed incomplete prefixed-key matching, an XML root traversal omission, and a connection-string container false positive.

### Error
```text
Three SecretRedactionPipelineTests failures for DB_PASSWORD, XML root Password, and appsettings logging preservation.
```

### Suggested Fix
Recognize sensitive key suffixes, traverse XML root with DescendantsAndSelf, and only redact a singular connection-string value rather than its container.

### Metadata
- Reproducible: yes
- Related Files: src/InsightaAI.Agent/Security/BuiltInSecretRedactors.cs

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Corrected the matching and traversal rules before rerunning the focused test set.

---

## [ERR-20260817-009] tool-executor-test-event-namespace

**Logged**: 2026-08-17T00:00:00+08:00
**Priority**: low
**Status**: resolved
**Area**: tests

### Summary
The new ToolCallExecutor preview test referenced Agent event types without importing their namespace.

### Error
```text
CS0246: AgentEvent and AgentToolEndEvent could not be found
```

### Suggested Fix
Import InsightaAI.Agent.Models in tests that collect Agent lifecycle events.

### Metadata
- Reproducible: yes
- Related Files: tests/InsightaAI.Agent.Tests/Tools/ToolResultProcessorTests.cs

### Resolution
- **Resolved**: 2026-08-17T00:00:00+08:00
- **Notes**: Added the missing test-only namespace reference.

---
