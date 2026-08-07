# Agent 安全增强设计（DenyList 与敏感文件保护）

> 创建：2026-08-07，由元培与 Insighta 讨论产出，尚未实现

## 1. 背景与目标

Agent 可以执行 Shell 命令、读写文件、搜索内容，当前缺少用户可配置的安全防线。本设计目标是：

1. **危险操作拒绝**：为 `AgentConfig` 添加 `DenyList`，拒绝 `rm -rf`、`Remove-Item` 等危险操作，用户可自定义规则。
2. **敏感文件保护**：`.env`、配置文件、`.ssh` 私钥等可能含机密，不应被 Agent 直接读取。

### 非目标（第一版）

- 不追求对 `bash` 任意命令的完全约束（在"本地任意命令执行"前提下无法做到，见威胁模型）。
- 不引入 Docker / 沙箱执行器（独立大工程，见 L4）。
- 不设计交互式"放行"流程（见拦截语义）。

## 2. 现状盘点

### 已有安全机制

| 机制 | 位置 | 说明 |
|------|------|------|
| `BashTool.IsDangerousCommand()` | `src/InsightaAI.Agent/Tools/BuiltIn/BashTool.cs:139` | 硬编码危险命令列表（`rm -rf /`、`mkfs`、`dd if=`、fork bomb 等），substring 匹配，工具内部拦截 |
| `ToolPermissionHook` | `src/InsightaAI.Agent.Cli/Hooks/ToolPermissionHook.cs` | CLI 层交互式确认（bash / write_file / read_file / edit_file / web_fetch），Allow / AllowAlways / Reject |
| `MemoryManager.ContainsSensitiveData()` | `src/InsightaAI.Agent/Memory/MemoryManager.cs:430` | 仅在保存记忆时过滤 API key / 密码，不阻止读取 |

### 两个需求的现状缺口

**① DenyList（危险操作）**
- 现有 `IsDangerousCommand` 写死在 BashTool 内，**无法用户自定义**。
- 只覆盖 bash，不覆盖 FileWrite / FileEdit 等破坏性操作。
- 属于工具内部逻辑，Agent 内核与配置层不可感知。

**② 敏感文件保护 —— 完全没有**
- `read_file` / `grep` / `bash` 均可无阻读取任意文件。
- `~/.insighta/config.json`（含 API key）、`.env`、`.ssh/` 私钥均无保护。

## 3. 威胁模型

核心矛盾在于工具的**结构化程度不同**：

| 工具 | 参数形态 | 可拦截性 |
|------|---------|---------|
| `read_file` / `grep` | 显式 `file_path` / `path` 参数 | ✅ 结构化，可精确拦截 |
| `bash` | 任意命令字符串 | ❌ 无法结构化约束 |

`bash` 是"任意代码执行"的逃逸通道：`cat ~/.ssh/id_rsa`、`Get-Content .env`、`type C:\...\config.json` 都能绕过 read_file 的保护。且命令可无限变形（`cat .e*nv`、`$(echo cat) $HOME/.ssh/id_rsa`、base64 编码）——**在"本地任意命令执行"前提下，命令内容检测无法完全拦截**。

因此方案目标界定为：**防止敏感内容进入 LLM 上下文、防止结构化工具直接触碰敏感路径**，而非阻止 bash 执行敏感读取本身。

## 4. 配置链路设计

配置分为两条链路，职责分离：

```
CliConfig (~/.insighta/config.json)      ← 最终配置链路（Load()）
   ↓ AgentFactory.CreateAsync() 映射     ← AgentFactory.cs:45-55
AgentConfig                              ← 数据传播链路（传给 AgentBuilder）
   ↓ AgentBuilder.Build()
Agent → SecurityPolicyHook (IToolHook)   ← 消费（与 ToolPermissionHook 同层）
```

- **CliConfig**：最终配置链路，持有 JSON 映射形状（`SecurityConfig`）。
- **AgentConfig**：数据传播链路，是 Agent 内核的数据契约（`DenyRules`），任何宿主（CLI / 其他嵌入方）都能配置。
- 两者结构刻意解耦，Agent 内核不依赖 CLI 模型。

## 5. DenyList 设计

### 5.1 CliConfig（最终配置）

```csharp
[JsonPropertyName("security")]
public SecurityConfig Security { get; set; } = new();

public sealed class SecurityConfig
{
    [JsonPropertyName("deny_list")]
    public List<DenyEntry> DenyList { get; set; } = [];
}

public sealed class DenyEntry
{
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
    [JsonPropertyName("mode")]   public string Mode { get; set; } = "glob"; // exact | glob | regex
}
```

对应 JSON：

```json
{
  "security": {
    "deny_list": [
      { "pattern": "rm -rf /",     "mode": "exact" },
      { "pattern": "shutdown*",    "mode": "glob" },
      { "pattern": "del /s /q c:*","mode": "glob" }
    ]
  }
}
```

### 5.2 AgentConfig（传播模型）

```csharp
public IReadOnlyList<DenyRule>? DenyRules { get; init; }

public sealed record DenyRule(string Pattern, DenyMatchMode Mode);  // exact | glob | regex
```

### 5.3 匹配模式

| 模式 | 说明 | 误伤风险 |
|------|------|---------|
| `exact` | 命令规范化（trim + 小写 + 折叠空白）后整串比较 | 最低 |
| `glob` | `*` 通配符，直观覆盖大部分场景 | 低 |
| `regex` | 显式标注为"危险模式"，进阶用户使用 | 中（用户自担） |

### 5.4 拦截语义：强制拒绝

**DenyList 命中 → 直接拒绝，不提供交互放行。**

理由：
- 职责边界：交互确认是 `ToolPermissionHook` 的职责，DenyList 再叠加会造成语义重叠和双重打扰。
- 语义干净："拒绝列表"是**预先声明**（用户明确列出不想让 Agent 做的事），不是运行时临时决策。
- 避免放行范围/持久化的复杂度爆炸（仅本次、本会话、永久白名单各自都要设计存储）。

被拦截时返回明确错误信息（如"命令命中 DenyList 规则 `rm -rf /`，已拒绝"），反馈给 LLM 调整行为。用户真想执行，就去 `CliConfig` 删除该规则——显式、持久、可审计。

**分层语义：**
```
内置高危规则（rm -rf /、Remove-Item -Recurse -Force C:\…）→ 强制拒绝，不可放行
用户 DenyList（CliConfig.security.deny_list）              → 强制拒绝，不可放行
```

若未来需要"软拒绝"，可单独加 `deny_list_mode: "block" | "confirm"` 开关，默认 `block`。第一版不加。

## 6. 敏感文件保护：纵深防御分层

```
L1 结构化工具层（可靠）    read_file/grep/write_file 按 file_path 精确拦截敏感路径
L2 bash 命令内容启发式     检测明显敏感路径引用（只拦"明显"，承认拦不住"恶意"）
L3 bash 输出检测+打码      执行后扫 stdout，命中私钥头/AK 模式/KEY=value → 打码再给 LLM
L4 最小权限执行（根治）    IShellExecutor 换 Docker/受限用户，bash 根本碰不到敏感文件
```

各层能力评估：

- **L1**：可靠。`read_file` / `grep` 有显式路径参数，`SecurityPolicyHook` 按路径规则精确拦截——DenyList 对结构化工具真正有效。需要解析 `arguments` JSON 中的 `file_path` / `path` 参数。
- **L2**：启发式。只能拦 `cat ~/.ssh`、`Get-Content C:\Windows\...` 这类直白写法，**拦不住变形命令**（接受的事实）。
- **L3**：兜底有价值。`IToolHook.OnAfterExecutionAsync`（`Hooks/IToolHook.cs:32`）能拿到 `ToolResult`，命中敏感模式则替换为 `[REDACTED]` 再交给 LLM——**防止敏感数据进入上下文**。命令已执行，但 LLM 拿不到内容，达成"Agent 拿不到机密"的核心目的。
- **L4**：根治。`IShellExecutor`（`Abstractions/IShellExecutor.cs:5`）已是抽象，`LocalShellExecutor` 只是本地实现——可扩展 Docker/沙箱执行器，敏感文件物理上不可达。独立大工程，建议单独立项。

**推荐第一版范围：L1 + L3**（L1 精确拦截结构化工具，L3 兜底防上下文泄漏）；L2 只做最直白检测；L4 单独立项。

## 7. 落地顺序与工作量

### Phase 1：DenyList（危险命令拦截）
- `AgentConfig` 加 `DenyRules` 字段
- `CliConfig.SecurityConfig.DenyList` + JSON 映射
- `SecurityPolicyHook`（`IToolHook`）实现 exact/glob/regex 匹配
- 内置默认高危规则（用户 DenyList 追加而非覆盖）
- 单元测试 + 集成测试
- **工作量：约 0.5 天**

### Phase 2：敏感文件保护
- `SecurityConfig.SensitivePaths`（glob 模式，如 `**/.env`、`**/.ssh/**`、`~/.insighta/config.json`）
- L1：`SecurityPolicyHook` 按路径拦截 `read_file` / `grep` / `write_file`
- L3：`OnAfterExecutionAsync` 输出打码
- **工作量：约 0.5~1 天**

### 关键技术点
- bash 命令路径提取（`~` 展开 + 环境变量解析）是主要难点，Phase 1 可先只做命令匹配，路径高危检测放 Phase 2。
- bash / PowerShell 双语法规则表按运行平台维护。
- glob 对命令字符串匹配建议自写轻量通配转正则（项目 `Microsoft.Extensions.FileSystemGlobbing` 是文件系统匹配器，不适合命令字符串）。

## 8. 待决策项

- [ ] `DenyRule` 匹配对象：整条命令规范化匹配（Phase 1 采用）还是拆解命令 token（Phase 2）
- [ ] 内置默认规则清单范围
- [ ] L2 启发式检测的阈值（哪些算"明显"敏感路径）
- [ ] L4 沙箱执行器是否立项、用 Docker 还是受限用户

## 9. 参考

- 工具执行链路：`Agent.ExecuteSingleToolAsync`（`Agent.cs:715`）→ `CheckToolPermissionAsync` → 遍历 `IToolHook` → `ToolRegistry.ExecuteAsync`
- Hook 接口：`src/InsightaAI.Agent/Hooks/IToolHook.cs`
- 配置链路：`src/InsightaAI.Agent.Cli/Services/AgentFactory.cs`
