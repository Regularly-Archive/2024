# Agent 交互协议设计（v0 草案）

- 状态：草案（待评审）
- 日期：2026-09-16
- 关联：`docs/handoff/2026-09-15-non-interactive-run-protocol.md`（run JSONL 协议现状）
- 目标读者：InsightaAI 协作者；下一阶段 Web/桌面客户端与子代理设计的输入

## 1. 目标

把 `insighta run` 的单向 JSONL 事件流升级为**双向会话协议**，使同一 Agent 内核可以被四类对端驱动：

1. **CLI（非交互）**——cron/CI/脚本，无人在场
2. **TUI（chat 交互）**——现有终端交互
3. **Web / 桌面客户端**——富 UI，可渲染弹窗与表单
4. **父代理（subagent 场景）**——`delegate` 本质 = 非交互 run + 结构化结果回传

核心洞察：权限批准（approve/deny）与用户提问（ask_user）是同一个协议原语——"Agent 挂起 + 客户端解析（resolve）"。区别只在 payload。协议的另一端抽象为 **peer**（人类 UI、父代理、CI 管道），由能力协商决定挂起行为。

## 2. 现行行为标本（为什么必须做）

### 2.1 实验

任务：`向用户提出一个澄清问题（问他偏好哪种输出格式：表格还是列表），然后等他回答后再继续任务`，无 profile 直接 `run`。

结果（exit 0）：

- 工具层：ask_user 在注册表即被剥离（`RunApplication.cs:268-275`，`CreateHostToolRegistry` 无条件 `Exclude(["ask_user", "delegate"])`；无 profile 时另有 `NonInteractiveDefaultDeniedTools` 更长的默认拒绝清单，`RunApplication.cs:31-37`）
- 行为层：模型 thinking 中自述"我只有 whereami, read_file, glob, grep"，随后**用纯文本伪装提问**：
  > 请确认一下：你希望接下来的内容用哪种格式输出——表格还是列表？我等你回复后再继续。
- 协议层：进程随即正常退出，`run.completed`、`status: "completed"`。**"我等你回复"是协议层谎言**（进程已不存在），且一个显式要求"等回答再继续"的任务被标记为已完成。

### 2.2 结论

现行"剥离工具 + 祈祷模型自觉"策略导致**静默造假**：比挂死更糟——挂死暴露问题，造假污染会话记录并给出错误的完成状态。若消费端是 Web UI，用户会看到一个永远等不到答案的假提问。协议缺失处，模型必然发明伪协议。

## 3. 业界对照（2026-09-16 调研）

| | 传输 | 权限批准 | 提问 | 非交互降级 |
|---|---|---|---|---|
| Codex app-server | JSON-RPC over JSONL（stdio/WebSocket） | 服务端请求 `execCommandApproval`，turn 挂起，客户端回 accept/decline/cancel | 同机制 | `ApprovalsReviewer=user/auto_review/guardian` 三档代答 |
| ACP（Zed） | JSON-RPC 2.0 over stdio | `session/request_permission`，options 数组（kind: allow_once/allow_always/reject_once/reject_always），回 `selected+optionId` 或 `cancelled` | 同为服务端请求 | `initialize` 声明 clientCapabilities；cancel 时 pending 请求必须回 Cancelled |
| Claude Code / Agent SDK | stdio JSON | 分层评估：hook → deny 规则 → mode → allow 规则 → `canUseTool` 回调；deny 永远赢 | `AskUserQuestion` 工具 + MCP `_meta[requiresUserInteraction]` | `dontAsk` 模式把 prompt 变 deny（fail closed）；`--permission-prompts none` 防 CI 挂死 |
| MCP Elicitation | MCP 内 | — | `elicitation/create`：form（JSON Schema）+ url（带外，elicitationId 关联） | 客户端能力协商决定是否发出 |

参考：Codex App Server 文档、InfoQ《OpenAI Publishes Codex App Server Architecture》、agentclientprotocol.com/protocol/v1/schema、code.claude.com/docs/en/agent-sdk/permissions、modelcontextprotocol.io spec 2025-11-25。

收敛出的共同模式（五条）：

1. 双向信封 + id 关联（请求/响应/通知三类消息）
2. turn 挂起语义（阻塞请求 + 超时 + 取消应答）
3. 策略层决定无人在场时的行为（fail closed，绝不悬等）
4. 能力协商（initialize 时声明能否交互）
5. 决策可记忆（allow_once vs allow_always = 本次决定 vs 沉淀为规则）

## 4. 设计原则

1. **协议与执行循环同构**：Agent 本来就运行在"发出请求 → 挂起 → 拿结果 → 继续"的循环上；工具调用、权限批准、用户提问都是请求，对端是工具进程还是人只是 peer 不同。
2. **人类是对端，不是特例**：ask_user 与工具调用共用请求/响应机制。
3. **权限决策在 harness 层执行**：模型无法自我设限，deny 规则与模式必须在我（模型）之外的策略引擎里。
4. **无交互端 fail closed**：绝不悬等；解析方式要么诚实失败，要么策略默认答案。
5. **挂起是可持久化状态，不是活进程阻塞**（详见 §8）。

## 5. 消息信封（提案）

沿用现有自定义信封（`protocol` + `sequence`），新增双向请求能力，避免破坏现有消费者：

```jsonc
// 通知（现状事件，不变）：服务端 → 客户端，无 id
{"protocol":1, "sequence":42, "type":"agent.event", "event":{...}}
{"protocol":1, "sequence":662, "type":"run.completed", ...}

// 服务端请求（新增）：服务端 → 客户端，带 id，turn 挂起直至收到响应
{"protocol":1, "id":"req_7", "method":"permission.request", "params":{...}}
{"protocol":1, "id":"req_8", "method":"elicitation.request", "params":{...}}

// 客户端 → 服务端（stdin / WebSocket 下行，新增）
{"id":"req_7", "result":{"outcome":"allow_once"}}          // 请求响应（同 id 关联）
{"id":"c_1", "method":"initialize", "params":{...}}        // 握手
{"id":"c_2", "method":"run.cancel"}                        // 取消
```

向后兼容：旧客户端不声明交互能力 → 服务端永不发出 `*.request` → 旧消费者把新服务端当纯事件流用，行为不变。新旧互通通过 initialize 协商，协议版本号随之推进。

### 5.1 permission.request

```jsonc
{
  "toolCallId": "call_01",
  "tool": "bash",
  "kind": "execute",                  // read | edit | delete | execute | other
  "summary": "git push --force origin main",
  "options": [
    {"optionId": "allow_once",   "kind": "allow_once",   "name": "仅本次允许"},
    {"optionId": "allow_always", "kind": "allow_always", "name": "总是允许 bash"},
    {"optionId": "deny",         "kind": "reject_once",  "name": "拒绝"}
  ],
  "timeoutMs": 120000,
  "onTimeout": "deny"                 // deny（默认，fail closed）| allow | fail
}
```

响应：`{"outcome":"selected","optionId":"allow_once"}` 或 `{"outcome":"cancelled"}`（run.cancel 时服务端自行生成，不需客户端逐个应答）。

### 5.2 elicitation.request

形状与 MCP elicitation 对齐（form 模式），便于未来 MCP 工具的 elicitation 透传复用同一 UI：

```jsonc
{
  "message": "输出格式偏好？",
  "mode": "form",
  "schema": {
    "type": "object",
    "properties": {"format": {"type": "string", "enum": ["表格", "列表"]}},
    "required": ["format"]
  },
  "timeoutMs": null,                  // null = 可持久挂起（见 §8）
  "suspendible": true
}
```

响应（沿用 MCP 动作语义）：`{"action":"accept","content":{"format":"表格"}}` | `{"action":"decline"}` | `{"action":"cancel"}`。

### 5.3 initialize（能力协商）

```jsonc
{"id":"c_1", "method":"initialize", "params":{
  "clientInfo": {"name":"insighta-web", "version":"0.1"},
  "capabilities": {
    "canPrompt": true,          // 能弹权限批准
    "canDisplayForm": true,     // 能渲染 JSON Schema 表单
    "interactive": true         // 总开关：false 则服务端不发任何 *.request
  }
}}
```

CLI 非交互模式 = `interactive:false`；TUI = `interactive:true, canDisplayForm:false`（用选项列表代替表单）；Web/桌面全开。

## 6. 权限引擎（分层评估，学 Claude Code）

在现有 profile 工具白名单之上增加规则与模式层。评估顺序（先到先决，deny 永远赢）：

1. **deny 规则**（profile / session 级，支持参数模式如 `bash(git push*)`）→ 直接拒绝，任何模式都压不住
2. **权限模式**：`ask`（默认，未匹配即挂起请求）| `acceptEdits`（文件类自动批准）| `dontAsk`（该问的一律 deny）| `bypass`（跳过检查，deny 规则仍生效）
3. **allow 规则** → 自动批准
4. **交互请求** → 上述都未命中且客户端可交互时发出

非交互入口新增策略参数（v0 提案）：

```
insighta run --on-permission deny|allow        # 默认 deny
insighta run --on-ask fail|answer:"<文本>"     # 默认 fail（run.failed: interaction_required）
```

`allow_always` 的本质：把一次决定沉淀为一条 allow 规则（写入 profile 或 session，位置待定 → 开放问题 §12）。

## 7. 两类请求的等待边界

| 场景 | 权限批准 | 用户提问 |
|---|---|---|
| 非交互（CI / cron / 子代理 v0） | 不等，`--on-permission` 立即解析 | 不等，`--on-ask` 立即解析 |
| 交互 peer 在场 | 进程内挂起 + 超时 deny | 进程内挂起 → 可转持久挂起 |
| peer 不响应 | 超时 deny | 超时按策略 / 转 `run.suspended` |
| run.cancel / 断连 | pending 全部 resolve 为 cancelled → `run.failed(cancelled)` | 同左 |

## 8. 挂起生命周期

**核心主张：挂起是可持久化、可超时、可取消的显式状态，而不是进程的死循环。**

- **进程内挂起**：`permission.request` 典型生命周期（秒级）。turn 阻塞在 TaskCompletionSource 上，stdin/WS 下行响应 resolve。必须有 `timeoutMs`。
- **持久挂起**：`elicitation.request` 可能等 3 秒或 3 天。支持把 pending 请求随 session 落盘，发 `run.suspended` 终态事件后**进程干净退出**：

```jsonc
{"protocol":1, "type":"run.suspended", "sessionId":"S", "pendingRequests":["req_8"], "status":"suspended"}
```

- **恢复**：`insighta resolve <session> <requestId> --answer <file>`（命令名待定），或 Web/桌面同一进程内直接 resolve。现有 `--session` 恢复 + `IMessageStorage` 落盘是现成地基，本设计只把"恢复会话"扩展为"恢复会话 + 补一个待决请求"。
- **状态枚举扩展**：turn 终态从 `completed | failed` 扩展为 `completed | failed | cancelled | suspended`。"等人回答"不是 completed（§2 标本的错误之一）。

## 9. 子代理适配（run 模式的第二使命）

**论点：run 模式天然是 subagent 基底。** `delegate` = 非交互 run + 结构化结果回传；`CliInsightaSubagentAdapter` 已经在用 profile run 当子代理调用；事件已携带 `agentId`。推论：协议的"客户端"抽象为 peer 后，父代理与人类 UI 在协议层无差别——都是"能 resolve 挂起请求的对端"。

分阶段界定：

- **v0（本设计）**：子代理内 ask_user / 权限请求一律 fail closed（`run.failed: interaction_required`，替代现行假提问）；`delegate` 仍从 run 工具表剥离（防递归调用）。
- **v1（后续阶段）**：
  - **ask 链式传播**：子代理的 elicitation.request 路由给父代理，父代理自答或升级给人类（传递挂起，每一跳都有超时）
  - **delegate 解禁**：run 模式允许派生子代理，需深度限制、环检测、预算/成本传递
  - **权限规则回写**：子代理的 allow_always 写回 profile 定义

## 10. 与现有实现的映射

| 现有资产 | 在新协议中的角色 |
|---|---|
| JSONL 事件流（`protocol`/`sequence`） | 通知层，原样保留 |
| `--allowed-tools` fail-fast 校验 | allow 规则层的静态部分 |
| profile（subagent.json） | 规则来源 + 子代理定义 |
| `--session` / `IMessageStorage` | 持久挂起的存储地基 |
| chat 的 ask 交互（AnsiConsole 选择器） | TUI peer 的 resolve 渲染器 |
| `run.failed` 语义（exit 1 + 明确错误） | 非交互 fail closed 的输出通道 |
| 事件中的 `agentId` | 子代理链路追踪 |

## 11. 迁移阶段

- **P0 协议地基**：initialize 握手 + 能力协商；两类请求的消息定义；非交互诚实失败（`interaction_required`）替代假提问；终态枚举扩展。纯增量，不破坏现有消费者。
- **P1 交互挂起**：TUI 接线（chat 现有 ask 交互改走协议）；run.cancel；超时策略。
- **P2 持久挂起**：`run.suspended` + pending 落盘 + resolve 命令 + session 恢复扩展。
- **P3 生态**：delegate 解禁、ask 链式传播、MCP elicitation 透传、（可选）ACP adapter 以接入编辑器。

## 12. 开放问题

1. 信封是否全量迁 JSON-RPC 2.0，还是保持自定义信封 + id 字段（本稿倾向后者，避免破坏现有消费者）
2. 权限规则持久化位置：profile 文件（跨会话）vs session 级（隔离）vs 两者并存
3. resolve 的鉴权：Web 场景下谁有权回答挂起请求（多用户/多会话隔离）
4. 子代理的预算/成本如何随挂起传递与分摊
5. `elicitation.request` 的 url 模式（OAuth 等带外场景）是否进 v0（倾向不进，MCP 透传时再议）
