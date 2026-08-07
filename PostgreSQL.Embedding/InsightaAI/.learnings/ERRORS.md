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
