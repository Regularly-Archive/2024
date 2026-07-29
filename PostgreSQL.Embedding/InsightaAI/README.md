# InsightaAI

InsightaAI 是一个基于 .NET 9 的 AI Agent 框架，提供 LLM 多模型适配、工具调用、上下文压缩、记忆、Skills、MCP 和多 Agent 编排能力。CLI 全局命令为 `insighta`。

## 快速开始

要求：安装 .NET 9 SDK。

从 NuGet 安装最新发布版：

```powershell
.\install-insighta.ps1
```

如果需要构建当前仓库并安装本地开发版：

```powershell
.\build-insighta.ps1
```

首次使用先运行配置向导，然后开始对话：

```powershell
insighta config
insighta chat
```

`insighta` 不带参数时同样会进入聊天。

## 文件搜索行为

`glob` 按传入的 glob pattern 匹配文件；`*.md` 仅匹配顶层，`**/*.md` 递归匹配子目录。为减少构建产物带来的噪声，它默认排除 `bin/`、`obj/` 和 `node_modules/`。

- 传递 `excludes` 可追加排除模式数组，例如 `["generated/**", "*.min.js"]`。
- 传递 `include_ignored: true` 可取消默认排除，以搜索这些目录中的文件。

`grep` 同样使用 `excludes` 字符串数组排除文件或目录，例如 `["*.log", "node_modules/**"]`。

## 常用命令

```powershell
insighta -c                         # 继续当前目录最近一次会话
insighta chat --session <id>        # 继续指定会话
insighta sessions                   # 查看历史会话
insighta skills list                # 查看 Skills
insighta mcp list                   # 查看 MCP 服务
```

聊天中的常用命令：

```text
/compact                            自动选择第一个有实际收益的压缩策略
/compact micro                      仅尝试 MicroCompact
/compact sessionMemory              仅尝试 SessionMemoryCompact
/compact traditional                仅尝试 TraditionalCompact
/model provider/model               切换模型
/clear                              清空当前会话上下文
/exit                               退出
```

## 上下文管理

上下文占用率和压缩阈值统一基于可用输入预算：

```text
AvailableInputTokens = MaxContextTokens - ReservedForOutput
```

默认压缩层级：

| 层级 | 阈值 | 行为 |
|------|------|------|
| MicroCompact | 45% | 工具结果按 Full → Preview → Placeholder → Removed 渐进降级 |
| SessionMemoryCompact | 65% | 使用会话记忆替换较旧历史 |
| TraditionalCompact | 80% | 调用摘要模型压缩历史消息 |

每个策略先在消息副本上试算，只有 token 或消息数量实际下降时才提交。手动 `/compact auto` 按优先级逐个尝试，提交第一个有效策略。

超过 30 KiB 的文本工具结果会由 Runtime 保存原始内容，并向上下文提供 Preview。工具只负责定义语义化投影，落盘和生命周期由 Agent Runtime 统一管理。

## 配置与数据目录

```text
~/.insighta/config.json                    模型和 Agent 配置
~/.insighta/auth.json                      Provider 认证信息
~/.insighta/sessions/                      会话消息与工具结果 Artifact
<project>/.insighta/mcp-servers.json       项目级 MCP 配置
~/.agents/mcp-servers.json                 全局 MCP 配置
```

模型的 `context_window` 表示完整上下文窗口，`max_tokens` 同时作为输出上限和默认输出预留；未配置 `max_tokens` 时预留 16,384 tokens。

## 开发与验证

```powershell
dotnet test tests/InsightaAI.Agent.Tests/InsightaAI.Agent.Tests.csproj
dotnet test tests/InsightaAI.LLM.Tests/InsightaAI.LLM.Tests.csproj
dotnet test tests/InsightaAI.Agents.Orchestrator.Tests/InsightaAI.Agents.Orchestrator.Tests.csproj
dotnet build src/InsightaAI.Agent.Cli/InsightaAI.Agent.Cli.csproj
```

修改 CLI 后重新执行 `build-insighta.ps1` 即可更新全局命令。

## 设计文档

- [项目愿景](docs/VISION.md)
- [待办事项](docs/TODO.md)
- [工具结果生命周期 v2](docs/tool-result-lifecycle-design-v2.md)
- [系统提示词设计](docs/core-instructions-design.md)
- [Agent Loop 研究](docs/agent-loop-research.md)
- [可观测性设计](docs/observability-design.md)

`docs/context-compression-design.md`、`docs/tool-result-truncation-design.md` 和对应 Review 为历史设计，当前实现以工具结果生命周期 v2 文档和代码为准。
