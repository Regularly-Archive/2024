# Token 用量统计设计

## 1. 背景

现有 OpenTelemetry 指标适合观察整体吞吐、延迟、错误率和模型维度的 token 消耗，但 Prometheus 不适合以 `userId` 或 `sessionId` 作为 label：两者会持续增长，造成高基数时间序列。

需要一条独立的、可审计的用量记录链路，用于精确回答：某个用户、某个会话、某个模型在一段时间内实际消耗了多少 input、output 和 cache-hit token。

## 2. 决策

- 每个完成的 LLM Round 产生一条不可变 `UsageRecord`；Tool 调用导致的后续 Round 也分别记录。
- `UserId` 来自 `AgentContext`，而非 `AgentConfig`。同一个 Agent 可以服务多个用户，配置对象不承担请求归属。
- Agent Core 定义模型、接口、写入时机和 SQLite 实现；CLI 仅负责决定默认数据库路径、注入实现和提供查询命令。
- 第一版默认数据库路径由 CLI 提供：`~/.insighta/usage/usage.db`。其他宿主可传入自己的 SQLite 路径。
- Prometheus 保持低基数聚合指标；用量明细不写入 metric label。Trace 可保留 `session.id` 以便从记录跳转排查，但不作为指标维度。

## 3. 数据模型

```csharp
public sealed record UsageRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? UserId { get; init; }
    public string? SessionId { get; init; }
    public required string AgentId { get; init; }
    public required int RoundNumber { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required int CacheHitTokens { get; init; }
    public string? TraceId { get; init; }
}
```

`UserId` 与 `SessionId` 可空，以支持尚未建立身份体系、或模型未返回用量的宿主。不存在 usage 的 LLM 响应不应伪造为 0；第一版只记录实际收到 `TokenUsage` 的 Round。

建议 SQLite schema：

```sql
CREATE TABLE usage_records (
    id                TEXT PRIMARY KEY,
    occurred_at       TEXT NOT NULL,
    user_id           TEXT NULL,
    session_id        TEXT NULL,
    agent_id          TEXT NOT NULL,
    round_number      INTEGER NOT NULL,
    provider          TEXT NULL,
    model             TEXT NULL,
    input_tokens      INTEGER NOT NULL,
    output_tokens     INTEGER NOT NULL,
    cache_hit_tokens  INTEGER NOT NULL,
    trace_id          TEXT NULL
);

CREATE INDEX ix_usage_session_time ON usage_records(session_id, occurred_at);
CREATE INDEX ix_usage_user_time ON usage_records(user_id, occurred_at);
CREATE INDEX ix_usage_model_time ON usage_records(model, occurred_at);
```

## 4. 接口与职责

`InsightaAI.Agent/Usage/`：

```csharp
public interface IUsageStore
{
    Task RecordAsync(UsageRecord record, CancellationToken cancellationToken = default);
    Task<UsageSummary> GetSummaryAsync(UsageQuery query, CancellationToken cancellationToken = default);
}
```

- `SqliteUsageStore`：初始化 schema、原子写入和按过滤条件聚合。
- Agent Loop：在每个 LLM Round 的最终 `LlmResponse.Usage` 已确定后 await `RecordAsync`，保证继续生成前用量已持久化；不依赖 Telemetry 开关或 exporter 成功与否。
- `AgentContext`：新增可空 `UserId`，并将 `SessionId` / `UserId` 传入该 Turn 所有 Round 的记录。
- CLI：创建 `SqliteUsageStore` 并通过 `AgentBuilder.ConfigureServices()` 注入；后续 `insighta usage` 只负责读取、筛选与渲染，不直接理解 Agent Loop。

## 5. 查询能力

第一阶段提供可组合的 `UsageQuery`：`UserId`、`SessionId`、`Model`、起止时间。`UsageSummary` 返回：请求/Round 数、input/output/cache-hit token 总数及总 token。

CLI 后续命令建议：

```text
insighta usage --session <id>
insighta usage --user <id> --from <date> --to <date>
insighta usage --model <provider/model>
```

`--session` 是 CLI 当前可立即提供的精确查询。`--user` 只在调用方为 `AgentContext.UserId` 提供真实身份时出现结果；不以机器名、工作目录或模型名伪造用户身份。

## 6. 可靠性与边界

- 用量记录失败应像消息持久化失败一样阻止 Agent 继续，避免静默产生不可审计缺口；存储异常需以 `AgentErrorEvent` 收束本轮。
- 每条记录以随机 ID 写入；如需要请求级幂等，后续可增加 provider request ID 或 `(sessionId, turnId, roundNumber)` 唯一约束。目前 Agent 未引入稳定 `TurnId`，不在第一版假造。
- 数据库只存归属、模型和数量，不存 prompt、completion、工具参数或密钥。
- 会话删除与用量删除策略应独立决策。默认保留用量记录作为审计数据；若引入隐私删除，则按 `session_id` / `user_id` 显式级联清理。

## 7. 验收标准

- 单个 Turn 有多次 LLM Round 时，每个返回 usage 的 Round 恰好一条记录。
- 同一会话可准确聚合 input、output、cache-hit 和总 token。
- 两个用户使用同一 Agent 时记录可按 `UserId` 隔离聚合。
- Telemetry 关闭或 OTLP exporter 失败时，SQLite 用量仍正常写入。
- 无 usage 的流式模型不会被记成真实 0 token。
- Prometheus 中不新增 `userId`、`sessionId` 维度。
