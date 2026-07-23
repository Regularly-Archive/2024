# McpCommand 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Commands/McpCommand.cs`

## 国际化状态：已完成

所有用户可见字符串均已通过 `CliStrings` 引用，无硬编码字符串。

## 引用的 CliStrings 资源 key

### 命令与选项描述

| Key | 用途 | 位置 |
|-----|------|------|
| McpDescription | mcp 命令描述 | L30 |
| McpListDescription | list 子命令描述 | L33 |
| McpAddDescription | add 子命令描述 | L39 |
| McpRemoveDescription | remove 子命令描述 | L60 |
| McpNameArgumentDescription | name 参数描述 | L40, L61 |
| McpTransportOptionDescription | --transport 选项描述 | L41 |
| McpCommandOptionDescription | --command 选项描述 | L42 |
| McpArgsOptionDescription | --args 选项描述 | L43 |
| McpUrlOptionDescription | --url 选项描述 | L44 |
| McpDescriptionOptionDescription | --description 选项描述 | L45 |
| ScopeOptionDescription | --scope 选项描述 | L34, L62 |
| ScopeOptionDescriptionWithDefault | --scope 选项描述（带默认值） | L46 |

### 运行时消息

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| McpListEmpty | MCP 列表为空 | L109 | 无 |
| McpListFieldName | 表格列：名称 | L115 | 无 |
| McpListFieldTransport | 表格列：传输方式 | L116 | 无 |
| McpListFieldEndpoint | 表格列：端点/命令 | L117 | 无 |
| McpListFieldDescription | 表格列：描述 | L118 | 无 |
| McpOverwritePromptFormat | 覆盖确认提示 | L165 | `{0}` = name |
| McpSseUrlRequired | SSE 需要 --url | L175 | 无 |
| McpStdioCommandRequired | stdio 需要 --command | L181 | 无 |
| McpAddedFormat | 已添加提示 | L198 | `{0}` = name, `{1}` = scope |
| McpRemovedFormat | 已移除提示 | L219 | `{0}` = scope, `{1}` = name |
| McpNotFoundFormat | 未找到提示 | L229 | `{0}` = name |

### 共享资源

| Key | 用途 | 位置 |
|-----|------|------|
| CommonCancelled | 已取消 | L168 |
| ErrorPrefix | 错误前缀 | L175, L181 |
| ScopeGlobal | 全局 | L238 |
| ScopeProject | 项目 | L237 |

## 硬编码字符串

无
