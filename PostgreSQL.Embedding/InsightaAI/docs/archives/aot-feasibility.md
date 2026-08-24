# InsightaAI CLI Native AOT 可行性研究

> 整理时间：2026-08-06
> 分支：`experiment/cli-aot`
> 目的：评估将 `insighta` CLI 通过 Native AOT 发布以提升启动速度、减小体积、去除运行时依赖的可行性
> 状态：**研究结论已存档，未动手实施**

---

## 1. Background & Motivation

当前 `insighta` 以 .NET Global Tool 形式分发（`dotnet tool install`），运行时依赖 .NET 9 Runtime。CLI 是 Agent 的核心交互入口，每次 `insighta chat` 启动都要经历 JIT 编译，启动延迟明显。

Native AOT（`PublishAot=true`）可带来：
- **启动更快**：ILC 预编译为原生码，无 JIT，对 CLI 冷启动感知明显
- **单文件、体积小**：无需安装 .NET Runtime，减少磁盘占用
- **内存占用更低**：无 JIT、无 GC 大量元数据

## 2. 现状分析

### 2.1 项目配置（`src/InsightaAI.Agent.Cli/InsightaAI.Agent.Cli.csproj`）

```xml
<OutputType>Exe</OutputType>
<TargetFramework>net9.0</TargetFramework>
<PackAsTool>true</PackAsTool>
<ToolCommandName>insighta</ToolCommandName>
```

### 2.2 依赖清单与 AOT 兼容性预估

| 依赖 | 版本 | AOT 兼容性 | 风险 |
|------|------|-----------|------|
| Microsoft.Extensions.Hosting | 9.0.0 | ⚠️ 部分 | Host/DI 有裁剪告警，可运行但需处理 |
| System.CommandLine | 2.0.0-beta4 | ⚠️ 部分 | beta 版 AOT 支持不完整 |
| Spectre.Console | 0.55.2 | ✅ 良好 | 社区广泛用于 AOT 场景 |
| DiffPlex | 1.9.0 | ✅ 良好 | 纯托管库 |
| OpenTelemetry ×3 | 1.16.0 | ⚠️ 部分 | OTLP exporter AOT 支持不完整 |
| Serilog + Sinks | 4.3.0 | ✅ 良好 | 纯托管，AOT 友好 |
| Microsoft.Data.Sqlite | 10.0.9 | ⚠️ 注意 | 依赖 native `e_sqlite3`，打包需处理 |

### 2.3 反射序列化使用规模（AOT 最大障碍）

全仓 7 处 `new JsonSerializerOptions`（默认反射模式），分布在：

| 位置 | 用途 |
|------|------|
| `OpenAIAdapter.cs:37` | LLM 请求/响应序列化 |
| `OpenAIResponseAdapter.cs:28` | Responses API 序列化 |
| `AnthropicAdapter.cs:32` | LLM 请求/响应序列化 |
| `GeminiAdapter.cs:26` | LLM 请求/响应序列化 |
| `WebSearchTool.cs:123` | 搜索响应反序列化 |
| `SessionMemoryHook.cs:274` | 会话摘要序列化 |
| `TaskPlanner.cs:68` | 任务规划序列化 |

## 3. 关键障碍与解决方案

### 障碍 1：Global Tool 与 AOT 互斥（结构性）

.NET 官方明确 **Global Tool（`PackAsTool`）不支持 Native AOT**。发布 AOT 时必须放弃 `dotnet tool install` 分发。

**方案：**
- 改为直接发布单文件 exe（`dotnet publish -r <rid> /p:PublishAot=true`）
- 分发方式改为：下载 exe / 自更新脚本 / 安装包
- 需权衡：放弃 Global Tool 的便捷更新机制（`dotnet tool update`）

**工作量**：中。涉及分发链路重新设计，非代码改动。

### 障碍 2：System.Text.Json 反射序列化（工作量最大）

AOT 裁剪下反射 JSON 序列化会失败或告警，7 处 `JsonSerializerOptions` 全部需改造。

**方案：**
- 全部换 `[JsonSerializable]` source generator + `JsonSerializerContext`
- 各 LLM 适配器的请求/响应 DTO 需逐一标注并注册到 Context
- 涉及 OpenAI/Anthropic/Gemini 三个适配器的完整 DTO 层

**工作量**：**大**。这是整个改造的主体，预计占 70% 工作量。且 DTO 后续新增模型时需同步维护 Context 注册，增加维护成本。

### 障碍 3：System.CommandLine beta4

beta 版对 AOT/裁剪支持不完整，可能出现绑定器相关告警或运行期问题。

**方案：**
- 实测验证；如有问题升级稳定版或评估替代方案（如 System.CommandLine.NativeAOT / Spectre.Console.Cli）

**工作量**：小-中，待实测确认。

### 障碍 4：OpenTelemetry / Host DI

- OTel 1.16 OTLP exporter 在 AOT 下支持不完整（无 `[DynamicallyAccessedMembers]` 注解的路径）
- Host 的反射式服务注册在裁剪下有风险

**方案：**
- 裁剪时对相关类型做 `[DynamicallyAccessedMembers]` 标注或 TrimmerRootAssembly
- 实测裁剪告警后逐个处理

**工作量**：中。

### 障碍 5：Microsoft.Data.Sqlite native 库

`e_sqlite3` native 依赖在 AOT 单文件打包中需正确嵌入。

**方案：**
- .NET 9 对 Sqlite AOT 打包有官方支持路径，实测验证

**工作量**：小-中。

## 4. 成本收益评估

### 收益
- 启动速度提升（CLI 冷启动最明显收益）
- 无需安装 .NET Runtime，降低部署门槛
- 单文件分发

### 成本
- **STJ 全量改造**：7 处 + 三个适配器 DTO 层，最大投入
- **分发方式变更**：放弃 Global Tool，重设计更新链路
- **维护负担增加**：DTO 需持续维护 `JsonSerializationContext` 注册
- **生态兼容风险**：System.CommandLine beta、OTel 的不确定项

### 关键判断

**Insighta 是网络 IO 密集型 agent CLI**，启动延迟并非核心瓶颈：
- 单次启动毫秒级收益，相对 LLM 调用秒级延迟可忽略
- 真正的体感瓶颈在 LLM 网络等待，AOT 无法改善

## 5. 结论与建议

1. **技术路线可行**，但成本收益比不佳——为启动速度投入大规模序列化改造不划算
2. **不建议当前实施**；如未来用户规模扩大、冷启动成为痛点，可再评估
3. **更轻量的替代方向**：
   - ReadyToRun（`PublishReadyToRun`）：保留 JIT 回退，无需改代码（已实测，见 §8）
   - 单文件 + 自包含（`PublishSingleFile` + `SelfContained`）：去运行时依赖，无需 AOT
   - 两者均可直接发布，零代码改动，是"更快更轻量"的低成本路径
4. 若坚持 AOT，优先做一次 `dotnet publish /p:PublishAot=true` 实测拿到真实错误清单再决策

## 6. 决策记录

| 日期 | 决策 | 依据 |
|------|------|------|
| 2026-08-06 | 先存档研究结论，不实施 AOT | 成本收益比不佳，STJ 改造投入大、收益边际小 |
| 2026-08-06 | 实测 ReadyToRun，启动提升仅 ~2-3%，不建议作为默认发布配置 | 启动瓶颈在 Host/DI/Serilog/OTel 初始化而非 JIT，R2R 无法显著改善（见 §8） |

## 7. ReadyToRun 实测（2026-08-06）

### 7.1 环境

- 分支 `experiment/cli-aot`，net9.0 / win-x64 / Release，framework-dependent
- 在 `InsightaAI.Agent.Cli.csproj` 打开 `<PublishReadyToRun>true</PublishReadyToRun>`（RID 由发布命令指定）
- 对比发布：`dotnet publish -r win-x64 /p:PublishReadyToRun=false`（normal） vs 默认开启（r2r）

### 7.2 R2R 生效确认

核心程序集体积显著增大（原生码嵌入 DLL）：

| 程序集 | normal | r2r | 增幅 |
|--------|--------|-----|------|
| InsightaAI.Agent.dll | 578 KB | 1,450 KB | +872 KB |
| Spectre.Console.dll | 829 KB | 1,626 KB | +797 KB |
| System.CommandLine.dll | 205 KB | 479 KB | +274 KB |
| InsightaAI.Agent.Cli.dll | 139 KB | 344 KB | +205 KB |

（.NET Core 3.0+ 的 R2R 将原生码直接嵌入原 DLL，不再生成独立 `.ni.dll`。）

### 7.3 启动时间实测

`Measure-Command` 各跑 6 次，去首尾取均值：

| 路径 | normal | r2r | 提升 |
|------|--------|-----|------|
| `--help` | 4,414 ms | 4,300 ms | **~2.6%** |
| `--version`（不识别，报错退出） | 4,394 ms | 4,331 ms | **~1.4%** |

### 7.4 结论

- R2R 技术生效，但启动时间提升仅 **~2-3%**，远低于"30-40%"的常见预期。
- 两个路径均耗时 ~4.4s，说明启动开销几乎全部来自 **Program.cs 的初始化链路**（CliConfig 加载、Host/DI 构建、Serilog、OpenTelemetry 装配），与 JIT 编译无关——R2R（乃至 AOT）都无法改善这部分。
- 真正优化启动需从初始化链路下手：懒加载、精简 Host、Telemetry 延迟初始化等，而非编译方式。
- **建议**：撤销 `PublishReadyToRun` 默认开关（发布变慢、体积增大，收益 ~3% 不值），如未来有真机冷启动场景再按需开启。

## 8. 参考

- .NET 官方文档：Native AOT 部署（Global Tool 限制说明）
- .NET 9 AOT 兼容性矩阵
