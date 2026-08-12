# Chat CLI 国际化资源清单

> 关联代码：`Services/ChatApplication.cs`、`UI/ChatRenderer.cs`、`UI/EventRenderer.cs`

## 当前状态

Chat 主流程的用户可见文本已迁移到 `CliStrings.resx` 和 `CliStrings.zh-CN.resx`。代码侧只保留终端布局、Spectre markup、事件标记和动态参数拼接。

本文不记录源码行号：交互渲染迭代频繁，行号很快失效。检查具体使用点时，应按资源 key 在代码中搜索。

## 对话与会话

| Key | 用途 | 格式参数 |
| --- | --- | --- |
| `ChatDescription` | chat 命令描述 | 无 |
| `ChatSessionOption` | 指定会话 ID | 无 |
| `ChatContinueOption` | 继续当前目录最近会话 | 无 |
| `ChatConfigRequiredHint` | 缺少配置提示 | 无 |
| `ChatSessionSavedFormat` | 会话已保存 | `{0}` = sessionId |
| `ChatSessionResumeHintFormat` | 恢复命令提示 | `{0}` = sessionId |
| `ChatSessionNotFoundFormat` | 会话不存在 | `{0}` = sessionId |
| `ChatNoHistoryForWorkDirFormat` | 当前目录无历史会话 | `{0}` = workDir |
| `ChatSessionCorruptedFormat` | 会话数据损坏 | `{0}` = sessionId |
| `ChatSessionResumedFormat` | 会话已恢复 | `{0}` = sessionId |
| `ChatGoodbye` | 退出提示 | 无 |

## 欢迎页与命令

| Key | 用途 | 格式参数 |
| --- | --- | --- |
| `ChatWelcomeProviderModelFormat` | Provider / Model | `{0}` = provider, `{1}` = model |
| `ChatWelcomeSessionIdFormat` | Session ID | `{0}` = sessionId |
| `ChatWelcomeToolsSkillsFormat` | 工具和 Skills 数量 | `{0}` = toolCount, `{1}` = skillCount |
| `ChatWelcomeExitHint` | 退出命令提示 | 无 |
| `ChatWelcomeClearHint` | 清空命令提示 | 无 |
| `ChatContextCleared` | 上下文已清空 | 无 |
| `ChatModelUsageHint` | `/model` 用法 | 无 |
| `ChatCurrentModelFormat` | 当前模型 | `{0}` = model |
| `ChatAvailableModels` | 可用模型标题 | 无 |
| `ChatCurrentModelMarker` | 当前模型标记 | 无 |
| `ChatProviderNotConfiguredFormat` | Provider 未配置 | `{0}` = provider |
| `ChatModelNotConfiguredFormat` | Model 未配置 | `{0}` = modelRef |
| `ChatModelSwitchedFormat` | 模型已切换 | `{0}` = provider, `{1}` = model |

## 压缩、用量与中断

| Key | 用途 | 格式参数 |
| --- | --- | --- |
| `ChatCompactingFormat` | 正在压缩 | `{0}` = strategy |
| `ChatCompactedFormat` | 手动压缩结果 | 压缩前后消息/token 统计 |
| `ChatNothingToCompact` | 无需压缩 | 无 |
| `ChatCompactFailedFormat` | 压缩失败 | `{0}` = error |
| `ChatAutoCompactedFormat` | 自动压缩结果 | 策略与压缩前后统计 |
| `ChatTokenUsageLabel` | Usage 标签 | 无 |
| `ChatInterruptedTitle` | 用户中断标题 | 无 |
| `ChatInterruptedHint` | 中断后提示 | 无 |
| `ChatThinkingInitial` | Thinking 初始状态 | 无 |
| `ChatThinkingProgressFormat` | Thinking 进度 | `{0}` = dots |

## AskUser

| Key | 用途 | 格式参数 |
| --- | --- | --- |
| `ChatAskUserPromptFormat` | AskUser 问题标题 | `{0}` = escaped question |
| `ChatAskUserYes` | 默认肯定选项 | 无 |
| `ChatAskUserNo` | 默认否定选项 | 无 |
| `ChatAskUserMultiSelectTitle` | 多选提示 | 无 |
| `ChatAskUserNoSelection` | 无选择结果 | 无 |
| `ChatAskUserSelectTitle` | 单选提示 | 无 |

AskUser 与权限确认统一使用 `◆` 作为“需要用户参与”的事件标记。SelectionPrompt 标题和选项的缩进由代码侧 `UseConverter` 提供，不应写入资源文本。

## 事件标记与翻译边界

| 标记 | 语义 |
| --- | --- |
| `●` | 助手文本片段 |
| `○` | 工具调用 |
| `⎿` | 工具结果，缩进为调用的子节点 |
| `◆` | 权限确认、AskUser 或系统交互事件 |

标记、颜色 markup、缩进和布局属于渲染契约，留在代码侧；可翻译的自然语言文本属于 `CliStrings` 资源。动态问题和错误消息在插入 Spectre markup 前必须进行 `Markup.Escape`。

## 维护检查

1. 新增用户可见文本时，同时更新英文和中文 resx。
2. 不要将颜色、缩进或事件标记嵌入纯文本资源，除非该 key 本身已明确定义为完整 markup 模板。
3. 不要用本地化 Label 作为分支判断值；交互选择应返回稳定枚举或模型值。
4. 更改标记或缩进时，同步检查 `ChatApplication`、`ToolPermissionHook`、`ChatRenderer` 和 `EventRenderer`。
