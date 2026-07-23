# SessionsCommand 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Commands/SessionsCommand.cs`

## 国际化状态：已完成

所有用户可见字符串均已通过 `CliStrings` 引用，无硬编码字符串。

## 引用的 CliStrings 资源 key

### 命令与选项描述

| Key | 用途 | 位置 |
|-----|------|------|
| SessionsDescription | sessions 命令描述 | L28 |
| SessionsListDescription | list 子命令描述 | L29 |
| SessionsDeleteDescription | delete 子命令描述 | L32 |
| SessionsDeleteSessionIdOption | --sessionId 选项描述 | L33 |

### 运行时消息

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| SessionIdEmpty | 会话 ID 为空提示 | L107 | 无 |
| SessionNotFoundFormat | 会话未找到 | L114 | `{0}` = sessionId |
| SessionDeletedFormat | 会话已删除 | L119 | `{0}` = sessionId |

### 通过 ChatRenderer 间接引用

| Key | 用途 |
|-----|------|
| SessionListEmpty | 会话列表为空 |
| SessionListFieldId | 表格列：ID |
| SessionListFieldTitle | 表格列：标题 |
| SessionListFieldProvider | 表格列：供应商 |
| SessionListFieldModel | 表格列：模型 |
| SessionListFieldMessages | 表格列：消息数 |
| SessionListFieldCreatedAt | 表格列：创建时间 |
| SessionListPageFormat | 页码格式 | 
| SessionListPrevious | 上一页 |
| SessionListNext | 下一页 |
| SessionListQuit | 退出 |
| SessionListContinueHint | 继续会话提示 |
| ErrorPrefix | 错误前缀（通过 ShowError） |

## 硬编码字符串

无

## 共享资源

| Key | 用途 |
|-----|------|
| CommonCancelled | 已取消（本命令未直接使用，但 ChatRenderer 中可能间接使用） |
