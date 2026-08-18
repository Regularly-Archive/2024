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
