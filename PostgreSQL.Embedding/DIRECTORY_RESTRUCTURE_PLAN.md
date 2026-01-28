# 目录结构重构计划

> 生成日期: 2026-01-23
> 最后更新: 2026-01-28
> 状态: ✅ 已完成

---

## 目标结构

```
PostgreSQL.Embedding/
├── Application/                    # ✅ 完成 (API Controllers)
│   ├── Controllers/
│   │   └── (15个Controller)
│   └── DTOs/
│
├── Domain/                         # ✅ 完成
│   ├── Entities/                   # ✅ 完成 (17个实体)
│   ├── Interfaces/                 # ✅ 完成
│   └── Models/                     # ✅ 完成
│
├── Infrastructure/                 # ✅ 完成
│   ├── DataAccess/                 # ✅ 完成
│   ├── FileStorage/                # ✅ 完成
│   ├── Messaging/                  # ✅ 完成
│   └── BackgroundJobs/             # ✅ 完成
│       ├── IImportingTaskHandler.cs
│       ├── BaseImportingTaskHandler.cs
│       ├── FileImportingTaskHandler.cs
│       ├── TextImportingTaskHandler.cs
│       ├── UrlImportingTaskHandler.cs
│       └── OpenAIProxyHandler.cs
│
├── Llm/                            # ✅ 完成
│   ├── Abstractions/               # ✅ 完成
│   ├── Core/                       # ✅ 完成
│   ├── Connectors/                 # ✅ 完成 (5个连接器)
│   ├── Routers/                    # ✅ 完成
│   ├── Planners/                   # ✅ 完成
│   └── Services/                   # ✅ 完成
│       ├── Retrieval/              # ✅ 完成
│       └── Rerank/                 # ✅ 完成
│
├── Plugins/                        # ✅ 完成
│   ├── Abstractions/               # ✅ 完成
│   ├── BuiltIn/                    # ✅ 完成
│   └── MCP/                        # ✅ 完成
│
├── Common/                         # ✅ 完成
│   ├── Configuration/              # ✅ 完成
│   ├── Extensions/                 # ✅ 完成
│   ├── Utilities/                  # ✅ 完成
│   ├── Enums/                      # ✅ 完成
│   └── Json/                       # ✅ 完成
│
├── Hubs/                           # ✅ 完成
│   └── NotificationHub.cs
│
└── Program.cs
```

---

## 已完成任务 ✅

### 2026-01-28 插件注册机制重构 + LLM 服务优化

| 模块 | 变更 | 说明 |
|------|------|------|
| **Plugins/** | 重构 | `AddPlugins()` 改为扫描程序集注册所有插件（BuiltIn + Custom） |
| **Common/Utilities/** | 新增 | `KernelPluginsExtensions.PersistAllPluginsAsync()` 从 DI 容器获取已注册类型 |
| **Domain/Entities/** | 修改 | `LlmPlugin` 增加 `IsBuiltin` 字段 |
| **Domain/Entities/** | 修改 | `LlmAppPlugin` 增加 `Enabled` 字段 |
| **Llm/Connectors/** | 新增 | `AddChatCompletionFromModel()` 扩展方法，根据 `LlmModel.ApiFormat` 自动选择 OpenAI/Anthropic |
| **Llm/Core/** | 简化 | `KernelService.GetKernel()` 从 47 行缩减到 28 行 |
| **Application/Controllers/** | 新增 | `LlmModelController` 添加 `{id}/test` 测试连通性接口 |
| **Program.cs** | 修改 | 调用 `PersistAllPluginsAsync()` 持久化插件元数据 |
| **Wikit.Tests/** | 重构 | 单元测试规范重构，统一使用 `When_Call_XXX` 命名模式 |

---

### 2026-01-26 最终更新

| 模块 | 状态 | 说明 |
|------|------|------|
| Application/Controllers | ✅ | 15个Controller已创建/移动 |
| Application/DTOs | ✅ | 目录已创建 |
| Domain/Entities | ✅ | 17个实体已移动 |
| Domain/Interfaces | ✅ | 接口定义已创建 |
| Domain/Models | ✅ | 模型文件已移动 |
| Infrastructure/DataAccess | ✅ | Repository.cs, CrudBaseService.cs |
| Infrastructure/FileStorage | ✅ | 文件存储服务已移动 |
| Infrastructure/Messaging | ✅ | 通知服务已移动 |
| Infrastructure/BackgroundJobs | ✅ | 所有Handler已移动 |
| Llm/Core | ✅ | 核心服务已移动 |
| Llm/Services/Retrieval | ✅ | 检索服务已创建 |
| Llm/Services/Rerank | ✅ | 重排序服务已创建 |
| Llm/Routers | ✅ | 路由已移动 |
| Llm/Planners | ✅ | 规划器已移动 |
| Llm/Abstractions | ✅ | 接口已移动 |
| Llm/Connectors | ✅ | OpenAI, Anthropic, LLama, Ollama, HuggingFace |
| Common/Extensions | ✅ | 扩展方法已移动 |
| Common/Utilities | ✅ | 工具类已移动 |
| Prompts嵌入资源 | ✅ | 提示词已改为嵌入资源 |
| 清理旧目录 | ✅ | Handlers, Services, Utils, LlmServices.* 已删除 |

---

## 清理完成清单

### 已删除的空目录
```
✅ LlmServices.Abstration/
✅ LLmServices.Extensions/
✅ Services/
✅ Utils/
✅ Handlers/
```

### 已删除的重复/残留文件
```
✅ Llm/Services/KnowledgeBaseBackgroundService.cs
✅ Llm/Services/KnowledgeBaseTaskQueueService.cs
```

### 已移动的文件
```
✅ Handlers/OpenAIProxyHandler.cs → Infrastructure/BackgroundJobs/
✅ Handlers/BaseImportingTaskHandler.cs → Infrastructure/BackgroundJobs/
✅ Handlers/UrlImportingTaskHandler.cs → Infrastructure/BackgroundJobs/
```

---

## 命名规范

### 目录命名
- 使用 **PascalCase** (如 `LlmServices` → `Llm`)
- 避免缩写 (如 `Utils` → `Utilities`)
- 一致性

### 文件命名
- 类名 = 文件名
- 接口加 `I` 前缀

### 命名空间规范
```csharp
namespace PostgreSQL.Embedding.Llm.Core;
namespace PostgreSQL.Embedding.Llm.Connectors.OpenAI;
namespace PostgreSQL.Embedding.Infrastructure.FileStorage;
```

---

## 架构变更记录

### 插件注册与持久化机制（2026-01-28）

**启动阶段流程：**
```
1. AddPlugins()
   └─ 扫描程序集，注册所有插件（BuiltIn + Custom）到 DI 容器

2. PersistAllPluginsAsync()
   └─ 从 DI 容器获取已注册的插件类型，持久化到 llm_plugins 表
```

**会话开始导入流程：**
```
ImportLlmPluginsAsync(serviceProvider, appId)
│
├── BuiltIn 插件 → 全部从 DI 容器导入
└── Custom 插件 → 根据 LlmAppPlugin.Enabled 导入
```

**数据库变更：**
```sql
-- llm_plugins 表
ALTER TABLE llm_plugins ADD COLUMN is_builtin boolean DEFAULT false;

-- llm_app_plugins 表
ALTER TABLE llm_app_plugins ADD COLUMN enabled boolean DEFAULT true;
```

---

### LLM 服务简化（2026-01-28）

**之前：**
```csharp
var apiFormat = (LlmApiFormat)llmModel.ApiFormat;
if (apiFormat == LlmApiFormat.Anthropic)
{
    kernelBuilder.AddAnthropicChatCompletion(...);
}
else
{
    kernelBuilder.AddOpenAIChatCompletion(...);
}
```

**之后：**
```csharp
// 根据 LlmModel 自动选择 OpenAI 或 Anthropic
kernelBuilder.AddChatCompletionFromModel(llmModel, httpClient);
```

---

### 新增 API 接口（2026-01-28）

**测试模型连通性**
```
GET /api/LlmModel/{id}/test
```

**请求示例：**
```bash
curl -X GET http://localhost:5000/api/LlmModel/1/test
```

**响应示例：**
```json
// 成功
{
  "code": 200,
  "data": {
    "success": true,
    "message": "模型连通正常",
    "response": "Hello! How can I help you today?"
  },
  "message": "success"
}

// 失败
{
  "code": 500,
  "data": null,
  "message": "模型测试失败: Invalid API key"
}
```

---

## 兼容性保证

- ✅ 命名空间保持不变（向后兼容）
- ✅ 只移动文件，不修改代码
- ✅ 分支开发，逐步迁移
