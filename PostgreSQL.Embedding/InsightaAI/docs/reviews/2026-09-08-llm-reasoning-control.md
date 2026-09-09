# Code Review: LLM Reasoning Control（2026-09-08）

- **Reviewer**：Insighta
- **Reviewer of**：Codex（同日主笔）
- **Branch**：`feature/llm-thinking-control`
- **Commits**：`d7f0e0e` `feat(llm): add reasoning controls with conservative off policy`、`ee76ecd` `docs(llm): define stable reasoning preferences`
- **Working tree**：另有大量未提交改动（capability catalog、resolver、CLI wrapper、新测试工程 `InsightaAI.Agent.Cli.Tests`）

## 基线验证

| 项目 | 结果 |
|------|------|
| `dotnet build InsightaAI.sln` | ✅ 0 warning / 0 error |
| `InsightaAI.Agent.Cli.Tests` | ✅ 7/7 通过 |
| `InsightaAI.LLM.Tests` | ⚠️ 118/121 通过，3 个失败均为 `GlmAnthropicTests` 真实 API 用例（额度不足，符合预期，见 `docs/architecture/llm-reasoning-control-handoff.md` §3.4） |

## 总体判断

**方向正确**，四层切分（产品偏好 → 能力目录 → 原生 ReasoningConfig → Adapter 序列化）落地干净，Adapter 不再按模型名猜协议，这个目标达成。校验逻辑严格（`Supported`↔`Mappings` 双向、`off` 必须映射 `ReasoningControl.Off`、禁止映射 `ProviderDefault`、`OffMode` 与 adapter 兼容）。

但有 8 处问题，其中 2 处属 P0。

---

## 问题清单

### P0-1：`AgentConfig.ReasoningPreference` 未接线，主路径是死代码

`AgentLoop.cs:107` 与 `:263` 读取 `_config.ReasoningPreference`，但 `AgentFactory.cs:48-60` 构造 `AgentConfig` 时从未赋值，永远是枚举默认值 `Default`。同时 `CliConfig` 也无对应字段。

**实际后果**：功能目前只影响 `SummaryService` 的两处辅助请求（`Off` + fallback），主 Agent 请求永远不带控制字段。用户配了 reasoning 也不会生效。

**建议**：`CliConfig` 增加 `reasoning_preference` 字段（默认 `default`），`AgentFactory.CreateAsync` 中 `defaultAgentConfig` 补 `ReasoningPreference = options.Config.ReasoningPreference`。

---

### P0-2：`ModelReasoningResolver` 4 参构造漏校验 `modelReference`

`ModelReasoningResolver.cs:12-26`：4 参构造收了 `modelReference` 却未 `ThrowIfNullOrWhiteSpace`。传空串能静默构造成功，一旦 `Resolve(Off)` 才报 `Model '' does not declare support...`，错误定位困难。

而 2 参构造（`Resolve` 实际使用的入口）有 `ThrowIfNullOrWhiteSpace(modelReference)`。两个入口行为不一致。

**建议**：4 参构造补 `ArgumentException.ThrowIfNullOrWhiteSpace(modelReference);`

---

### P1-3：能力覆盖是「全有或全无」，与注释语义不符

`CliConfig.cs:74-77` 注释写「完整替代内置目录中同一 adapter/model 的记录，适用于兼容网关或自定义模型」。代码实际是整段 `ModelReasoningCapability` 替换。

**问题**：用户想给内置模型（如 gpt-5.2）加一个 `fast` 档位、保留已有的 `off`，必须把整块 mappings 复制到配置，漏一个就丢一档。按 preference 粒度合并才是「覆盖」的语义。

**建议（二选一）**：
1. 改为按 preference 粒度合并（内置 mappings 为基底，配置中同 preference 的 mapping 覆盖）；
2. 保持整段替换，但注释改为「完整替代」，并在校验时给出「覆盖后丢失了 X 个 preference」的警告。

---

### P1-4：配置写回会静默丢失 reasoning 字段

`CliConfig.Save()` 走 `CreateJsonOptions()`（camelCase），能写出 reasoning。但用户手写 kebab 或其他写法（如 `off-mode` 而非 `offMode`），反序列化会静默忽略，字段变 null，下次 `Save()` 把配置写回时**永久抹掉**用户未被解析的内容。

`PropertyNameCaseInsensitive` 只解决大小写，不解决连字符差异。`offMode` 尤其容易踩。

**建议**：在 `CreateJsonOptions()` 增加一个校验式反序列化选项（或自定义 `JsonSerializerOptions` 的未识别 token 处理），对顶层 reasoning 相关字段做严格匹配；或至少在 `Save()` 前比较序列化前后的字段集合，发现丢失时告警。

---

### P2-5：未知偏好静默丢弃

`ModelConfiguredLlmClient.cs:30-31`：`ReasoningPreference is not { } preference` 时原样透传。若上游未来新增档位但忘记同步，请求会带着无人处理的 `ReasoningPreference` 字段落到 Adapter，表现为「配了但没生效」，且无日志。

**建议**：加一行 `Logger.Debug`（或 Trace）记录透传场景，字段包括 preference 原始值与 adapter 名。

---

### P2-6：能力目录覆盖面偏窄，且 Claude/Gemini 只声明 `off`

打包的 `Assets/model-reasoning-capabilities.json` 仅 8 条：

- `openai` / `openai-response` × gpt-5.1 / gpt-5.2（4 条，支持 default/fast/off/balance/deep）
- `gemini` × gemini-2.5-flash / gemini-2.5-flash-lite（2 条，仅 default/off）
- `anthropic` × claude-sonnet-4-5 / claude-sonnet-4-20250514（2 条，仅 default/off）

**问题**：使用 Claude 或 Gemini 的用户**完全无法**调整思考强度，只能 default 或 off，中间三档对他们不存在。若用户在 `CliConfig` 里配了 `balance`，运行时会报「模型不支持」——体验与「balance 是通用能力」的直觉不符。

**建议**：属产品决策，需明确 Claude/Gemini 的 fast/balance/deep 是否要做；若不做，文档中需明确标注为「仅 OpenAI 系可用」。

另：`docs/architecture/llm-reasoning-control-handoff.md` 第 10 行仍留 8 月旧模型 ID（`claude-sonnet-4-20250514`），与当前配置不完全一致，建议对齐或标注「历史记录」。

---

### P3-7：`LlmClientFactory.Create()` 每次都 `LoadDefault()` 读盘

`LlmClientFactory.cs:47-48` 每次调用都读 JSON 文件并反序列化整个目录。一次 CLI 启动通常只建一个 client，影响不大，但子 Agent / 多 provider 场景会重复读。

**建议**：提到工厂级或进程级 `Lazy<T>` 缓存。

---

### P3-8：`ReasoningOffPolicy.ResolutionKey` 定义了无读者

`ReasoningOffPolicy.ResolutionKey`（`HttpRequestOptionsKey<ReasoningOffMode>`）被 `Record()` 里 `httpRequest.Options.Set(...)` 写入，但目前无读取方——真正消费的是 `Activity` tag。

**建议**：若确为预留，注释里说明给谁用；否则删除，避免"死配置"。

---

## 补充观察

**测试覆盖盲区**：`InsightaAI.Agent.Cli.Tests` 只覆盖 `ModelReasoningResolver` 与 `ModelConfiguredLlmClient`。`LlmClientFactory` 的接线、`AgentFactory` 的配置传递均未测——问题 P0-1 就是从这里漏出去的。建议补一条：从 `CliConfig.ReasoningPreference` → `AgentConfig.ReasoningPreference` 的传递断言。

**`ProviderOptions` 边界**：`LlmRequest.cs:156-166` 的 `ProviderOptions` 是非推理的供应商扩展（`extra_body` 风格），handoff 文档明确禁止通过 `ProviderOptions.Custom` 传递 reasoning 控制字段。当前代码未见违反，但后续若加自定义字段需保持这条约束。

---

## 方向判断

**短期（一至两周）**：方向对，继续。先修 P0-1 与 P0-2 让功能真正生效，形成验证闭环。

**中期**：需先定两件事：

1. **产品档位语义**：`fast`/`balance`/`deep` 是「用户意图的表达」（接受各供应商实现不同），还是「跨供应商近似等价的质量档」（需更大矩阵保证体验一致）？当前 `balance → effort:medium`、`deep → effort:xHigh` 的映射在不同供应商下语义不等价，产品层承诺"均衡"但未说明是哪个含义。
2. **能力目录维护责任**：内置 JSON 编译期打进二进制，模型能力变更与软件发布周期不同步。可能方向：可更新（配置文件 + 内置默认叠加）、或收窄产品档位承诺（只承诺 `default` 与 `off`，把 `fast`/`balance`/`deep` 降级为"尽力而为"）。二者产品含义完全不同。

**不建议现在动** A 的映射语义——先让 5 档在单一供应商（OpenAI）上跑通、有真实使用反馈，再决定是否扩展到其他供应商。

---

## 建议修复顺序

1. **P0-1、P0-2** — 必修，成本低，影响功能生效与错误定位。
2. **P2-5** — 加一行日志，成本极低。
3. **P1-3、P1-4** — 决定配置模型是否现在调整（合并 vs 替代）。
4. **P2-6** — 产品决策，Claude/Gemini 档位要不要做。
5. **P3-7、P3-8** — 小优化，可放。

---

*本 review 基于 2026-09-08 16:30 工作区状态。Codex 后续若有改动，需重新核对。*
