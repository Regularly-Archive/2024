# Claude Code 内部分析报告

分析日期: 2026-06-04

---

## 一、Compact (会话压缩) 机制分析

### 1.1 整体架构

Compact 功能在 `src/commands/compact/compact.ts` 中作为本地命令 (`/compact`) 实现。核心压缩逻辑位于 `src/services/compact/` 目录下。

**关键文件:**
- `compact.ts` — `/compact` 命令入口
- `compact.ts` (services) — 核心压缩逻辑 `compactConversation()`
- `microCompact.ts` — 微压缩（清理旧工具结果）
- `sessionMemoryCompact.ts` — 基于会话记忆的压缩
- `reactiveCompact.ts` — 响应式压缩（被动触发）
- `prompt.ts` — 压缩提示词模板
- `grouping.ts` — 消息分组

### 1.2 触发时机

Claude Code 在以下时机触发 compact：

1. **手动触发**: 用户输入 `/compact` 命令
2. **自动触发 (Auto-compact)**: 当 token 使用量接近上下文窗口限制时自动触发
3. **响应式触发 (Reactive compact)**: 当 API 返回 `prompt_too_long` 错误时触发
4. **时间触发的微压缩**: 当距上次回复的时间间隔超过阈值时，清理旧的工具结果

### 1.3 压缩流程 (`compactConversation()`)

```
1. 验证消息列表不为空
2. 执行 PreCompact 钩子 (executePreCompactHooks)
3. 合并用户自定义指令和钩子指令
4. 生成压缩提示词 (getCompactPrompt)
5. 调用 LLM 生成摘要:
   a. 首选: Forked Agent (复用主会话的 prompt cache)
   b. 备选: 直接流式请求
6. 处理 prompt_too_long 重试 (最多 3 次)
7. 保存压缩前的文件读取状态
8. 清除 readFileState 缓存
9. 生成压缩后附件:
   - 最近访问的文件 (最多 5 个, 50K token 预算)
   - Plan 文件
   - Plan Mode 指令
   - 已调用的 Skill 内容
   - 异步 Agent 状态
   - Deferred Tools 重通知
   - Agent Listing 重通知
   - MCP Instructions 重通知
10. 执行 SessionStart 钩子 (恢复 CLAUDE.md 等上下文)
11. 执行 PostCompact 钩子
12. 创建压缩边界标记 (boundary marker)
13. 返回 CompactionResult
```

### 1.4 三级压缩策略

#### Level 1: MicroCompact (微压缩)
- **目的**: 清理旧的工具调用结果，减少 token 占用
- **策略**:
  - **Cached MicroCompact**: 使用 API 的 `cache_edits` 能力删除旧工具结果，不破坏 prompt cache
  - **Time-based MicroCompact**: 当时间间隔超过阈值时，直接清除旧工具结果内容（因为 cache 已冷）
- **可压缩的工具**: `Read`, `Bash`, `PowerShell`, `Grep`, `Glob`, `WebSearch`, `WebFetch`, `Edit`, `Write`
- **保留策略**: 保留最近 N 个工具结果

#### Level 2: Session Memory Compact (会话记忆压缩)
- **前提**: 需要 `tengu_session_memory` 和 `tengu_sm_compact` feature flags
- **原理**: 利用后台提取的会话记忆（Session Memory）作为摘要，替代 LLM 调用
- **优势**: 无需额外的 API 调用，速度快
- **配置**:
  ```typescript
  {
    minTokens: 10_000,        // 最少保留 token
    minTextBlockMessages: 5,   // 最少保留消息数
    maxTokens: 40_000          // 最多保留 token
  }
  ```
- **流程**:
  1. 等待会话记忆提取完成
  2. 读取会话记忆内容
  3. 计算需要保留的消息索引
  4. 构建压缩结果（无需 LLM 调用）

#### Level 3: Traditional Compact (传统压缩)
- **原理**: 调用 LLM 对整个对话历史进行摘要
- **提示词**: "You are a helpful AI assistant tasked with summarizing conversations."
- **模型配置**:
  - 禁用 thinking
  - 最大输出 token: `COMPACT_MAX_OUTPUT_TOKENS`
  - 工具: 仅 `FileRead` (或 + `ToolSearch` + MCP tools)
- **缓存优化**: 使用 forked agent 复用主会话的 prompt cache

### 1.5 压缩结果结构

```typescript
interface CompactionResult {
  boundaryMarker: SystemMessage          // 压缩边界标记
  summaryMessages: UserMessage[]         // 摘要消息
  attachments: AttachmentMessage[]       // 压缩后恢复的附件
  hookResults: HookResultMessage[]       // 钩子结果
  messagesToKeep?: Message[]             // 保留的消息（部分压缩）
  userDisplayMessage?: string            // 显示给用户的消息
  preCompactTokenCount?: number          // 压缩前 token 数
  postCompactTokenCount?: number         // 压缩后 token 数
  truePostCompactTokenCount?: number     // 实际压缩后 token 数
  compactionUsage?: TokenUsage           // 压缩 API 调用的 token 用量
}
```

### 1.6 部分压缩 (Partial Compact)

支持两种方向：
- **`from`**: 从指定消息开始向后压缩，保留前面的消息（保留 prompt cache）
- **`up_to`**: 压缩指定消息之前的内容，保留后面的消息（破坏 prompt cache）

### 1.7 关键设计决策

1. **图片剥离**: 压缩前移除图片（用 `[image]` 标记替代），避免压缩请求本身超长
2. **附件重注入**: 压缩后恢复最近读取的文件、Plan、Skills 等上下文
3. **Tool Use/Result 配对**: 压缩时确保不拆分 tool_use 和 tool_result 对
4. **Thinking Block 处理**: 保持共享 message.id 的 thinking block 完整性
5. **Prompt Cache 共享**: 通过 forked agent 复用主会话的 cache prefix

---

## 二、BashTool 分析

### 2.1 基本信息
- **文件**: `src/tools/BashTool/BashTool.tsx`
- **名称**: `bash`
- **最大结果大小**: 30,000 字符
- **搜索提示**: `execute shell commands`

### 2.2 Input Schema
```typescript
{
  command: string,           // 必需：要执行的命令
  timeout?: number,          // 可选：超时毫秒数
  description?: string,      // 可选：命令描述（用于 UI 显示）
  run_in_background?: boolean,  // 可选：后台运行
  dangerouslyDisableSandbox?: boolean  // 可选：禁用沙箱
}
```

### 2.3 核心功能

#### 命令执行
- 使用 `exec()` 函数执行 shell 命令
- 支持 bash shell
- 支持沙箱模式 (`shouldUseSandbox`)
- 合并 stdout 和 stderr (merged fd)

#### 进度显示
- **阈值**: 2 秒后显示进度
- **间隔**: 每秒更新进度
- **Assistant 模式**: 15 秒后自动后台化阻塞命令

#### 后台任务
- `run_in_background: true` 显式后台化
- 超时后自动后台化
- 用户 Ctrl+B 手动后台化
- Assistant 模式自动后台化（15 秒预算）

#### 安全检查
- AST 解析验证 (`parseForSecurity`)
- 只读命令检测 (`checkReadOnlyConstraints`)
- Sleep 模式检测 (`detectBlockedSleepPattern`)
- 危险命令阻止

#### 输出处理
- 大输出持久化到磁盘 (>30K 字符)
- 最大持久化大小: 64 MB
- 图片输出检测和压缩
- 搜索/读取命令的可折叠显示

### 2.4 命令分类

```typescript
// 搜索命令 (可折叠)
BASH_SEARCH_COMMANDS = ['find', 'grep', 'rg', 'ag', 'ack', 'locate', 'which', 'whereis']

// 读取命令 (可折叠)
BASH_READ_COMMANDS = ['cat', 'head', 'tail', 'less', 'more', 'wc', 'stat', ...]

// 列出命令 (可折叠)
BASH_LIST_COMMANDS = ['ls', 'tree', 'du']

// 静默命令 (显示 "Done")
BASH_SILENT_COMMANDS = ['mv', 'cp', 'rm', 'mkdir', ...]
```

### 2.5 权限模型

```typescript
// 分层权限检查
1. validateInput() — 基础验证
2. checkPermissions() — 权限检查 (bashToolHasPermission)
3. preparePermissionMatcher() — 准备权限匹配器
4. isReadOnly() — 只读检测
```

### 2.6 与 Claude Code 的差异

| 特性 | Claude Code BashTool | 我们的 BashTool |
|------|---------------------|----------------|
| Shell | bash (Linux/Mac) | PowerShell (Windows) |
| 沙箱 | bwrap/sandbox-exec | 无 |
| 后台任务 | 完整支持 | 无 |
| 进度显示 | 实时进度 | 无 |
| AST 安全解析 | 有 | 无 |
| 命令分类 | 搜索/读取/列出/静默 | 无 |
| 输出持久化 | 磁盘文件 | 内存截断 |

---

## 三、PowerShellTool 分析

### 3.1 基本信息
- **文件**: `src/tools/PowerShellTool/PowerShellTool.tsx`
- **名称**: `powershell`
- **最大结果大小**: 30,000 字符
- **搜索提示**: `execute Windows PowerShell commands`

### 3.2 Input Schema
```typescript
{
  command: string,           // 必需：PowerShell 命令
  timeout?: number,          // 可选：超时毫秒数
  description?: string,      // 可选：命令描述
  run_in_background?: boolean,  // 可选：后台运行
  dangerouslyDisableSandbox?: boolean  // 可选：禁用沙箱
}
```

### 3.3 与 BashTool 的主要差异

1. **Shell**: 使用 `pwsh` (PowerShell Core)
2. **路径验证**: Windows 特定的路径验证
3. **沙箱策略**: Windows 原生不支持沙箱，企业策略要求沙箱时拒绝执行
4. **命令解析**: 使用 PowerShell 特定的 AST 解析
5. **Sleep 检测**: `Start-Sleep` 替代 `sleep`
6. **Git 安全**: 额外的 git 操作安全检查

### 3.4 PowerShell 特定功能

```typescript
// 搜索命令
PS_SEARCH_COMMANDS = ['select-string', 'get-childitem', 'findstr', 'where.exe']

// 读取命令
PS_READ_COMMANDS = ['get-content', 'get-item', 'test-path', 'resolve-path', ...]

// 语义中性命令
PS_SEMANTIC_NEUTRAL_COMMANDS = ['write-output', 'write-host']

// 禁止自动后台化的命令
DISALLOWED_AUTO_BACKGROUND_COMMANDS = ['start-sleep', 'sleep']
```

### 3.5 Windows 沙箱策略

```typescript
// Windows 原生不支持沙箱
// 如果企业策略要求沙箱且不允许非沙箱命令，拒绝执行
const WINDOWS_SANDBOX_POLICY_REFUSAL =
  'Enterprise policy requires sandboxing, but sandboxing is not available on native Windows.'
```

---

## 四、FileEditTool 分析

### 4.1 基本信息
- **文件**: `src/tools/FileEditTool/FileEditTool.ts`
- **名称**: `edit` (通过 `FILE_EDIT_TOOL_NAME`)
- **最大结果大小**: 100,000 字符
- **搜索提示**: `modify file contents in place`

### 4.2 Input Schema
```typescript
{
  file_path: string,      // 必需：文件路径
  old_string: string,     // 必需：要替换的字符串
  new_string: string,     // 必需：替换为的字符串
  replace_all?: boolean   // 可选：替换所有匹配（默认 false）
}
```

### 4.3 核心功能

#### 验证流程
1. **Secret 检查**: 拒绝向 team memory 文件添加 secrets
2. **相同内容检查**: `old_string === new_string` 时拒绝
3. **权限拒绝规则**: 检查文件是否在拒绝目录中
4. **UNC 路径安全**: 跳过 UNC 路径的文件系统操作（防止 NTLM 泄露）
5. **文件大小限制**: 最大 1 GiB
6. **编码检测**: 支持 UTF-8 和 UTF-16LE
7. **文件存在性**: 文件不存在时尝试建议相似文件
8. **Jupyter Notebook**: 重定向到 NotebookEditTool
9. **Read 状态检查**: 必须先读取文件才能编辑
10. **修改时间检查**: 检测文件是否在读取后被修改
11. **字符串匹配**: 使用 `findActualString` 处理引号规范化
12. **多匹配检查**: `replace_all=false` 时多个匹配会拒绝
13. **Settings 文件验证**: 额外的 Claude settings 文件验证

#### 编辑执行
```typescript
async call(input, context) {
  1. 获取绝对路径
  2. 发现 Skill 目录
  3. 激活条件 Skill
  4. 通知 diagnostic tracker
  5. 确保父目录存在
  6. 记录文件历史（用于 undo）
  7. 读取当前文件内容
  8. 验证修改时间（原子性检查）
  9. 查找实际字符串（引号规范化）
  10. 保留引号风格
  11. 生成 patch
  12. 写入磁盘
  13. 通知 LSP 服务器
  14. 通知 VSCode
  15. 更新 readFileState
  16. 记录分析事件
}
```

#### 输出格式
```typescript
// 成功
"The file ${filePath} has been updated successfully."
"The file ${filePath} has been updated. All occurrences were successfully replaced."

// 用户修改了建议
"The file ${filePath} has been updated. The user modified your proposed changes before accepting them."
```

### 4.4 安全特性

1. **原子性保证**: 验证和写入之间无异步操作
2. **文件历史**: 支持 undo 操作
3. **LSP 集成**: 通知 LSP 服务器文件变更
4. **VSCode 集成**: 通知 VSCode 显示 diff
5. **Stale Write 检测**: 检测读取后的文件修改
6. **引号风格保留**: 保持文件原有的引号风格

### 4.5 与我们 EditTool 的差异

| 特性 | Claude Code FileEditTool | 我们的 EditTool |
|------|------------------------|----------------|
| 文件大小限制 | 1 GiB | 无 |
| 编码支持 | UTF-8, UTF-16LE | UTF-8 |
| 引号规范化 | findActualString | 无 |
| 文件历史 | 支持 | 无 |
| LSP 集成 | 有 | 无 |
| VSCode 集成 | 有 | 无 |
| UNC 路径安全 | 有 | 无 |
| Secret 检查 | 有 | 无 |
| Skill 发现 | 有 | 无 |

---

## 五、设计模式总结

### 5.1 工具定义模式
```typescript
export const SomeTool = buildTool({
  name: 'tool_name',
  searchHint: 'description for search',
  maxResultSizeChars: 30_000,
  strict: true,

  // 元数据
  async description(input) { ... },
  async prompt() { ... },
  userFacingName(input) { ... },
  getToolUseSummary(input) { ... },
  getActivityDescription(input) { ... },

  // Schema
  get inputSchema() { return inputSchema(); },
  get outputSchema() { return outputSchema(); },

  // 验证
  async validateInput(input) { ... },
  async checkPermissions(input, context) { ... },
  isReadOnly(input) { ... },
  isConcurrencySafe(input) { ... },

  // 执行
  async call(input, context, canUseTool, parentMessage, onProgress) { ... },

  // 结果映射
  mapToolResultToToolResultBlockParam(output, toolUseID) { ... },

  // UI 渲染
  renderToolUseMessage,
  renderToolResultMessage,
  renderToolUseErrorMessage,
});
```

### 5.2 权限分层模式
```
1. validateInput()     — 基础输入验证
2. checkPermissions()  — 权限检查
3. isReadOnly()        — 只读检测（用于并发安全）
4. preparePermissionMatcher() — 准备权限匹配器
```

### 5.3 后台任务模式
```
1. 显式后台化 (run_in_background: true)
2. 超时后台化 (onTimeout callback)
3. 手动后台化 (Ctrl+B)
4. 自动后台化 (Assistant mode, 15s budget)
```

### 5.4 输出处理模式
```
1. 内存累积 (EndTruncatingAccumulator)
2. 大输出持久化 (>30K chars → disk)
3. 图片检测和压缩
4. 搜索/读取命令的可折叠显示
```

---

## 六、可借鉴的设计

### 6.1 从 Compact 学习
1. **三级压缩策略**: MicroCompact → Session Memory → Traditional
2. **缓存友好的压缩**: 使用 cache_edits 保持 prompt cache
3. **压缩后上下文恢复**: 重新注入最近读取的文件、Plan、Skills
4. **Tool Use/Result 配对保护**: 压缩时不拆分关联的消息对

### 6.2 从 BashTool/PowerShellTool 学习
1. **命令分类**: 搜索/读取/列出/静默命令的可折叠显示
2. **进度显示**: 长时间运行命令的实时进度
3. **后台任务**: 多种后台化策略
4. **AST 安全解析**: 深度命令分析

### 6.3 从 FileEditTool 学习
1. **文件大小限制**: 防止 OOM
2. **引号规范化**: 处理不同编码风格
3. **原子性保证**: 验证和写入之间无异步操作
4. **LSP/VSCode 集成**: 实时诊断和 diff 显示
