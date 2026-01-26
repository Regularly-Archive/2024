# 目录结构重构计划

> 生成日期: 2026-01-23
> 最后更新: 2026-01-26
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

## 兼容性保证

- ✅ 命名空间保持不变（向后兼容）
- ✅ 只移动文件，不修改代码
- ✅ 分支开发，逐步迁移
