# SecretRedaction 运行时失效调查（2026-08-17）

> 背景：验证 `SecretRedactionPipeline` 脱敏链路。元培更新工具后重测，JSON/XML/KeyValue 三类 redactor 在运行时仍不生效（仅 ConnectionString + Generic 生效）。以下为完整测试记录与疑点。

> **最终结论（2026-08-17）**：并非 DI 覆盖或 `RedactionContext.Format` 门控。`read_file` 会加入文件元数据、行号和 Tab，使完整 JSON/XML 解析需要片段回退；更关键的是 Windows `FileReadTool.AppendLine()` 产生 `CRLF`，`KeyValueSecretRedactor` 的多行 `$` 锚点停在 `\n` 前、无法跨越残留的 `\r`，导致裸键值规则完全不匹配。实现现已统一换行符为 `LF` 后匹配，并恢复原始换行风格；运行时复验中 `probe.txt`、`app.yaml`、`.env`、JSON、XML 与连接串均通过。

## 1. 目标

确认 `read_file` 机密文件后工具结果是否被脱敏为 `[REDACTED]`，定位"源码/DLL/单测全对、运行时只生效 2/5 redactor"的断层。

## 2. 环境与版本

| 项 | 值 |
|---|---|
| 运行时 | .NET 9.0，全局工具 `insighta chat` |
| 版本 | 1.0.0-alpha.2 |
| 测试机 | Windows，`C:\Users\Administrator` |
| 样例目录 | `D:\test\` |

### 关键时间线

| 对象 | mtime |
|---|---|
| `src/InsightaAI.Agent/Security/BuiltInSecretRedactors.cs` | 16:03:48 |
| `bin/.../InsightaAI.Agent.dll`（源码构建） | 16:04:08 |
| `.store/.../InsightaAI.Agent.dll`（全局工具实际加载） | 16:04:08 |
| `.store/.../InsightaAI.Agent.Cli.dll` | 16:19:38 |
| `~\.dotnet\tools\insighta.exe`（shim） | 16:29:12 |
| 当前 insighta 进程（PID 84192）启动 | 16:29:44 |

### 哈希核对

- `.store/InsightaAI.Agent.dll` 与 `src/.../bin/.../InsightaAI.Agent.dll`：**哈希一致（IDENTICAL）**，为同一构建。
- 用独立 AssemblyLoadContext 加载 `.store` 的 Agent.dll，枚举到 **495 个类型**，`InsightaAI.Agent.Security` 命名空间完整包含：`SecretRedactionPipeline`、`SecretRedactionRules`、5 个 redactor（Json/Xml/KeyValue/ConnectionString/Generic）、`ToolRedactionContextFactory`、`SecurityPolicyHook` 等。

> 结论：全局工具安装的 DLL 就是最新构建，代码与程序集层面没有问题。

## 3. 决定性实验（直接调用 .store pipeline）

写独立 .NET 程序（`D:\test\asmcheck`），加载 `.store` 的 `InsightaAI.Agent.dll`，`SecretRedactionPipeline.CreateDefault()` 对 5 个样例文件全文执行脱敏。

### 结果

| 文件 | RedactionCount | 明细 |
|---|---|---|
| `config.json` | **7** | `apiKey`/`password`/`dbPassword`/`clientSecret`/`deepToken` 全部 → `[REDACTED]`；connectionString 内 Password → `[REDACTED]`；`publicInfo` 保留 |
| `settings.xml` | **4** | `ApiPassword`/`AuthToken`/`<password>`/connectionString Password 命中 |
| `.env` | **2** | `API_KEY=[REDACTED]`；`DATABASE_URL`（postgres URI）→ Generic UriPassword 命中；**`SECRET_KEY` 未命中** |
| `app.yaml` | **3** | `password`/`secret`/`token` 命中；**`- key:` 未命中** |
| `conn.txt` | **4** | SQL Server / PostgreSQL / MongoDB 连接串全部命中 |

> 结论：`.store` 的 pipeline 直接调用**功能完全正常**，5 类 redactor 均按预期工作。

## 4. 运行时行为（会话内 read_file）

同批文件通过 Agent `read_file` 读取，工具结果返回给 LLM 的内容：

- `config.json`：`apiKey`/`password`/`dbPassword`/`clientSecret`/`deepToken` **明文**；仅 connectionString 内 `Password=[REDACTED]`（ConnectionString redactor 生效）
- `settings.xml` / `.env` / `app.yaml`：JSON/XML/KeyValue 对应字段均**明文**（KeyValue 不生效）
- 对照组：`bash` 输出同样的 JSON 字符串，同样不脱敏

## 5. 核心矛盾

**同一个 `.store` DLL**：
- 直接 `CreateDefault()` 调用 → 全部 redactor 生效（7/4/2/3/4 处）
- Agent 运行时 → 仅 ConnectionString + Generic 生效

源码 `SecretRedactionPipeline.Redact()` 无条件遍历全部 5 个 redactor，无按 format 短路，无异常吞掉。`ToolResultProcessor.RedactTextContent()` 无条件调用 `_secretRedactor.Redact()`。因此问题几乎必然出在**运行时 pipeline 实例的来源**，而非脱敏算法本身。

## 6. 疑点与待排查项（Codex 处理）

### P0 — DI 注入覆盖（最可能）

`src/InsightaAI.Agent/Tools/ToolCallExecutor.cs:38`：

```csharp
redactor = serviceProvider.GetService<ISecretRedactor>() ?? SecretRedactionPipeline.CreateDefault()
```

若 DI 容器已注册 `ISecretRedactor`（旧实现，只有 2 个 redactor），则会**绕过 `CreateDefault()`**，完美解释"直接调用正常、运行时 2/5"。

**动作**：
1. `grep -rn "ISecretRedactor" src/` 全项目搜注册点（`AgentFactory` / `AgentBuilder` / DI 扩展方法）。
2. 确认注册的是哪个实现；若为旧版/私有实现，删除该注册或改为注册 `SecretRedactionPipeline.CreateDefault()`。
3. 补一个"运行时实例化路径"断言测试，防止回归。

### P1 — ToolCallExecutor 实例化路径

确认运行时 `ToolCallExecutor` 到底走哪个构造函数：
- 若存在**旧构造函数分支**（不经过 line 38 的 DI 逻辑），也可能绕开默认 pipeline。

**动作**：读 `ToolCallExecutor.cs` 全文件，列出所有构造函数及调用方，确认 line 38 是实际执行路径。

### P1 — 确认运行进程加载的 DLL 路径

PID 84192（当前会话进程）加载的 `InsightaAI.Agent.dll` 完整路径需最终确认（上轮进程枚举输出被截断）。理论上应为 `.store` 那个 16:04:08 的 DLL，但需实锤。

**动作**：`Get-Process -Id 84192 | Select Modules` 过滤 `InsightaAI.Agent.dll`，记录完整路径与 mtime。

## 7. 附：发现的次要盲区（非本次 bug，可另行评估）

1. **`*_KEY` 后缀未覆盖**：`SECRET_KEY` → normalized `secretkey`，`IsSensitiveKey` 无 `EndsWith("key")` 规则 → 不命中。`.env` 的 `SECRET_KEY` 漏脱敏即因此。
2. **YAML 列表项的裸 `key:`**：`app.yaml` 的 `- key: yaml-list-key-555` 未命中（`key` 不在敏感键清单）。若视为敏感键，需在规则中加 `key`。
3. 以上属**规则设计取舍**（防误伤 vs 覆盖度），非运行时 bug，暂不处理。

## 8. 复现材料

- 样例文件：`D:\test\`（config.json / settings.xml / .env / app.yaml / conn.txt / id_rsa）
- 直接调用验证程序：`D:\test\asmcheck\`（加载 `.store` DLL 跑 pipeline）
- 单测：`tests/InsightaAI.Agent.Tests/Security/SecretRedactionPipelineTests.cs`（12/12 通过）

## 9. 关键代码位置速查

| 文件 | 位置 | 说明 |
|---|---|---|
| `ToolCallExecutor.cs` | :38 | redactor 来源：DI 优先，否则 CreateDefault() |
| `ToolCallExecutor.cs` | :39-40 | ToolResultProcessor 构造 |
| `ToolResultProcessor.cs` | :36 | ProcessAsync 入口调 RedactTextContent |
| `ToolResultProcessor.cs` | :78 | RedactTextContent 无条件调 `_secretRedactor.Redact()` |
| `SecretRedactionPipeline.cs` | :13-19 | CreateDefault 注册 5 redactor 顺序 |
| `SecretRedactionPipeline.cs` | :30-36 | Redact 遍历全部 redactor |
| `ToolRedactionContextFactory.cs` | :32-48 | 按扩展名 DetectFormat |
| `BuiltInSecretRedactors.cs` | :11-20 | IsSensitiveKey 判定 |
