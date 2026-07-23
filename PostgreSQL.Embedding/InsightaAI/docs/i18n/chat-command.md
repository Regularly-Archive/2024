# ChatCommand 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Commands/ChatCommand.cs`
> 关联文件：`src/InsightaAI.Agent.Cli/UI/ChatRenderer.cs`、`src/InsightaAI.Agent.Cli/UI/EventRenderer.cs`

## 国际化状态：待处理

当前存在 41 处硬编码字符串（17 中文、19 英文、5 中英混合），需要提取到 resx 并提供中英双语翻译。

## 已引用的 CliStrings 资源 key

| Key | 用途 | 位置 |
|-----|------|------|
| ErrorPrefix | 错误前缀（通过 ChatRenderer.ShowError） | ChatRenderer L81 |

---

## 硬编码字符串清单

### 1. 命令描述（3 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L52 | `"开始对话"` | 中文 | ChatDescription | 无 |
| L53 | `"指定会话 ID（继续已有会话）"` | 中文 | ChatSessionOptionDescription | 无 |
| L54 | `"继续当前目录的最近一次会话"` | 中文 | ChatContinueOptionDescription | 无 |

### 2. 配置验证（2 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L77 | `"请先运行 'config' 命令进行配置"` | 中文 | ChatConfigRequiredHint | 无 |
| L93 | `$"创建 LLM 客户端失败: {ex.Message}"` | 中文 | ChatLlmClientFailedFormat | `{0}` = ex.Message |

### 3. 会话管理（7 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L123 | `$"Session saved: {session.SessionId}"` | 英文 | ChatSessionSavedFormat | `{0}` = sessionId |
| L124 | `$"Resume with: insighta chat --session {session.SessionId}"` | 英文 | ChatSessionResumeHintFormat | `{0}` = sessionId |
| L125 | `"See you again!"` | 英文 | ChatGoodbye | 无 |
| L583 | `$"会话 {sessionId} 不存在"` | 中文 | ChatSessionNotFoundFormat | `{0}` = sessionId |
| L595 | `$"当前目录没有历史会话: {workDir}"` | 中文 | ChatNoHistoryForWorkDirFormat | `{0}` = workDir |
| L601 | `$"会话 {record.Id} 数据损坏"` | 中文 | ChatSessionCorruptedFormat | `{0}` = sessionId |
| L604 | `$"已恢复会话: {session.SessionId}"` | 中文 | ChatSessionResumedFormat | `{0}` = sessionId |

### 4. 聊天命令处理（1 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L158 | `"上下文已清空"` | 中文 | ChatContextCleared | 无 |

### 5. /model 命令（8 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L210 | `"用法: /model provider/model_key"` | 中文 | ChatModelUsageHint | 无 |
| L211 | `$"当前模型: {config.PrimaryModel}"` | 中文 | ChatCurrentModelFormat | `{0}` = model |
| L214 | `"可用模型:"` | 中文 | ChatAvailableModels | 无 |
| L217 | `" ← current"` | 混合 | ChatCurrentModelMarker | 无 |
| L241 | `$"Provider '{newProviderName}' 未在 auth.json 中配置"` | 混合 | ChatProviderNotConfiguredFormat | `{0}` = providerName |
| L248 | `$"Model '{modelRef}' 未在 config.json 中配置"` | 混合 | ChatModelNotConfiguredFormat | `{0}` = modelRef |
| L262 | `$"创建 LLM 客户端失败: {ex.Message}"` | 中文 | （复用 ChatLlmClientFailedFormat） | `{0}` = ex.Message |
| L272 | `$"已切换到 {newProviderName}/{newModel.ModelId}"` | 中文 | ChatModelSwitchedFormat | `{0}` = provider, `{1}` = modelId |

### 6. /compact 命令（4 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L288 | `$"[yellow]⟳[/] Compacting context ([dim]{strategy}[/])..."` | 英文 | ChatCompactingFormat | `{0}` = strategy |
| L303-305 | `"Compacted ({strategy}): {pre} → {post} messages, ~{pre} → ~{post} tokens"` | 英文 | ChatCompactedFormat | `{0}` = strategy, `{1}` = preMsg, `{2}` = postMsg, `{3}` = preTokens, `{4}` = postTokens |
| L309 | `"[dim]Context is clean, nothing to compact.[/]"` | 英文 | ChatNothingToCompact | 无 |
| L314 | `$"[red]Compact failed: {ex.Message}[/] "` | 英文 | ChatCompactFailedFormat | `{0}` = ex.Message |

### 7. ask_user 工具回调（5 处）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L376 | `$"[yellow]●[/] Insighta wants to ask you: {question}"` | 英文 | ChatAskUserPromptFormat | `{0}` = question |
| L380 | `["Yes", "No"]` | 英文 | ChatAskUserYes / ChatAskUserNo | 无 |
| L388 | `"选择一个或多个选项（空格选择，回车确认）："` | 中文 | ChatAskUserMultiSelectTitle | 无 |
| L392 | `"(无选择)"` | 中文 | ChatAskUserNoSelection | 无 |
| L399 | `"选择一个选项："` | 中文 | ChatAskUserSelectTitle | 无 |

---

## ChatRenderer.cs 硬编码字符串（5 处）

### 8. 欢迎信息

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L22 | `$"Provider: {provider} \| Model: {model}"` | 英文 | ChatWelcomeProviderModelFormat | `{0}` = provider, `{1}` = model |
| L23 | `$"SessionId: {sessionId}"` | 英文 | ChatWelcomeSessionIdFormat | `{0}` = sessionId |
| L24 | `$"Tools: {toolCount} registered \| Skills: {skillCount} available"` | 英文 | ChatWelcomeToolsSkillsFormat | `{0}` = toolCount, `{1}` = skillCount |
| L25 | `"输入消息开始对话，输入 '/exit' 或 '/quit' 退出"` | 中文 | ChatWelcomeExitHint | 无 |
| L26 | `"输入 '/clear' 清空上下文"` | 中文 | ChatWelcomeClearHint | 无 |

---

## EventRenderer.cs 硬编码字符串（6 处）

### 9. 上下文压缩（自动触发）

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L170-172 | `"Context compacted ({strategy}): {pre} → {post} messages, ~{pre} → ~{post} tokens"` | 英文 | （复用 ChatCompactedFormat 或独立 Key） | `{0}` = strategy, `{1}` = preMsg, `{2}` = postMsg, `{3}` = preTokens, `{4}` = postTokens |

### 10. Token 用量

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L206 | `"Usage:"` | 英文 | ChatTokenUsageLabel | 无 |

### 11. 中断提示

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L230 | `"● The task has been cancelled by user"` | 英文 | ChatInterruptedTitle | 无 |
| L231 | `"⎿ Interrupted · What should Insighta do instead?"` | 英文 | ChatInterruptedHint | 无 |

### 12. Thinking 状态

| 行号 | 当前值 | 语言 | 建议 Key | 格式参数 |
|------|--------|------|---------|---------|
| L267 | `"Thinking."` | 英文 | ChatThinkingInitial | 无 |
| L273 | `$"Thinking (press esc to interrupt){dots}"` | 英文 | ChatThinkingProgressFormat | `{0}` = dots |

---

## 汇总

| 文件 | 硬编码数量 | 中文 | 英文 | 混合 |
|------|-----------|------|------|------|
| ChatCommand.cs | 30 | 15 | 10 | 5 |
| ChatRenderer.cs | 5 | 2 | 3 | 0 |
| EventRenderer.cs | 6 | 0 | 6 | 0 |
| **合计** | **41** | **17** | **19** | **5** |

## 注意事项

1. **Spectre.Console markup**：含 `[yellow]`、`[dim]`、`[red]`、`[green]` 等标签的字符串，标签本身不翻译，只翻译标签内的文本
2. **重复字符串**：L93 和 L262 是相同的 `"创建 LLM 客户端失败"` 消息，可共用一个 Key
3. **压缩消息**：ChatCommand L303-305（手动 /compact）和 EventRenderer L170-172（自动压缩）格式类似，可考虑统一或分别定义
4. **建议 Key 命名规范**：统一使用 `Chat` 前缀，与现有 `Config`、`Mcp`、`Skills`、`Sessions` 前缀保持一致
5. **format string 参数**：resx 中使用 `{0}`、`{1}` 占位符，与现有 Format 类资源一致
