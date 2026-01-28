# 单元测试备忘录

> 生成日期: 2026-01-23
> 最后更新: 2026-01-27
> 状态: 🔄 持续更新中

---

## 一、现有测试 (12个测试文件)

### 2026-01-27 更新 ✅

| 文件路径 | 测试内容 | 测试数 |
|----------|----------|--------|
| `LlmServices/PromptTemplateService_Tests.cs` | PromptTemplateService, CallablePromptTemplate | 9 |
| `LlmServices/LlmCompletionRouter_Tests.cs` | LlmCompletionRouter, LlmEmbeddingRouter | 12 |
| `LlmServices/LlmServiceFactory_Tests.cs` | LlmServiceFactory, ILlmService | 9 |
| `Utils/Utilities_Tests.cs` | 枚举值、Constants | 9 |

### 原有测试 ✅

| 文件路径 | 测试内容 |
|----------|----------|
| `Agents/TaskPlanner_Tests.cs` | 任务规划器 |
| `Agents/SystemStep_Tests.cs` | 系统步骤 |
| `Plugins/When_Call_BingSearchPlugin.cs` | Bing搜索插件 |
| `Plugins/When_Call_BraveSearchPlugin.cs` | Brave搜索插件 |
| `Plugins/When_Call_WeiXinSearchPlugin.cs` | 微信搜索插件 |
| `Reranker/When_Call_Reranker.cs` | 重排序器 |
| `When_Call_Regex.cs` | 正则工具 |
| `LlmServices/AnthropicChatCompletionService_Tests.cs` | Anthropic Chat Completion |

---

## 二、已完成的模块

### ✅ LlmServices - 已完成

| 模块 | 状态 | 测试文件 |
|------|------|----------|
| AnthropicChatCompletionService | ✅ | AnthropicChatCompletionService_Tests.cs |
| PromptTemplateService | ✅ | PromptTemplateService_Tests.cs |
| LlmCompletionRouter | ✅ | LlmCompletionRouter_Tests.cs |
| LlmEmbeddingRouter | ✅ | LlmCompletionRouter_Tests.cs |
| LlmServiceFactory | ✅ | LlmServiceFactory_Tests.cs |
| KernalService | ⚠️ 部分 | - |
| HuggingFaceService | ❌ | - |
| LLamaService | ❌ | - |
| OllamaService | ❌ | - |

### ✅ Utilities - 已完成

| 模块 | 状态 | 测试文件 |
|------|------|----------|
| LlmServiceProvider | ✅ | Utilities_Tests.cs |
| LlmApiFormat | ✅ | Utilities_Tests.cs |
| ModelType | ✅ | Utilities_Tests.cs |
| Constants | ✅ | Utilities_Tests.cs |
| RetrievalType | ✅ | Utilities_Tests.cs |
| RerankerType | ✅ | Utilities_Tests.cs |
| LlmAppType | ✅ | Utilities_Tests.cs |
| DocumentType | ✅ | Utilities_Tests.cs |
| ArtifactType | ✅ | Utilities_Tests.cs |
| QueueStatus | ✅ | Utilities_Tests.cs |
| TraceType | ✅ | Utilities_Tests.cs |
| GenderType | ✅ | Utilities_Tests.cs |

---

## 三、待测试模块

### 🔴 高优先级 - 核心业务逻辑

| 模块 | 文件/类 | 复杂度 |
|------|---------|--------|
| **Controllers** | `KnowledgeBaseController.cs` | ⭐⭐⭐ |
| | `LlmAppController.cs` | ⭐⭐⭐ |
| | `ConversationController.cs` | ⭐⭐⭐ |
| | `DocumentController.cs` | ⭐⭐ |
| | `LLMController.cs` | ⭐⭐ |
| **Services** | `ConversationService.cs` | ⭐⭐⭐ |
| | `GenericConversationService.cs` | ⭐⭐⭐ |
| | `MinioFileStorageService.cs` | ⭐⭐ |
| | `NotificationService.cs` | ⭐⭐ |

### 🟡 中优先级 - LLM 服务

| 模块 | 文件/类 | 复杂度 |
|------|---------|--------|
| **LlmServices** | `KernalService.cs` | ⭐⭐⭐ |
| | `HuggingFaceService.cs` | ⭐⭐ |
| | `LLamaService.cs` | ⭐⭐⭐ |
| | `OllamaService.cs` | ⭐⭐ |

### 🟢 低优先级 - 工具和辅助类

| 模块 | 文件/类 | 复杂度 |
|------|---------|--------|
| **Utils** | `ShortUrlGenerator.cs` | ⭐ |
| | `WebPageExtractor.cs` | ⭐⭐ |
| | `RSSExtractor.cs` | ⭐⭐ |
| | `KernelPluginsExtensions.cs` | ⭐⭐ |
| | `CSnakeExtensions.cs` | ⭐⭐⭐ |
| **Infrastructure** | `FileImportingTaskHandler.cs` | ⭐⭐ |
| | `TextImportingTaskHandler.cs` | ⭐⭐ |
| | `UrlImportingTaskHandler.cs` | ⭐⭐ |
| **Plugins** | `BasePlugin.cs` | ⭐⭐ |
| | `CodeInterpreterPlugin.cs` | ⭐⭐⭐ |
| | `MailKitPlugin.cs` | ⭐⭐ |
| | `FireCrawlPlugin.cs` | ⭐⭐ |

---

## 四、测试覆盖率

| 分类 | 文件数 | 测试覆盖 |
|------|--------|----------|
| Agents | 2 | ~80% |
| Plugins | 3 | ~30% |
| Reranker | 1 | ~50% |
| Utils | 1 → **5** | ~20% → **~60%** |
| LlmServices | 1 → **5** | ~10% → **~50%** |
| **总计** | **8 → 12** | **~20% → ~35%** |

---

## 五、行动计划

### Sprint 1 (已完成)
- [x] `PromptTemplateService_Tests.cs` - ✅ 9个测试
- [x] `LlmCompletionRouter_Tests.cs` - ✅ 12个测试
- [x] `LlmServiceFactory_Tests.cs` - ✅ 9个测试
- [x] `Utilities_Tests.cs` - ✅ 9个测试

### Sprint 2 (进行中)
- [ ] `KernalService_Tests.cs` - Kernel 服务测试
- [ ] `ConversationService_Tests.cs` - 对话服务测试
- [ ] `GenericConversationService_Tests.cs` - 通用对话服务测试

### Sprint 3 (计划中)
- [ ] `KnowledgeBaseController_Tests.cs` - 知识库控制器测试
- [ ] `ConversationController_Tests.cs` - 对话控制器测试
- [ ] `LlmAppController_Tests.cs` - LLM 应用控制器测试

---

## 六、测试统计

| 指标 | 数值 |
|------|------|
| 测试文件数 | 12 |
| 测试类数 | 18+ |
| 测试方法数 | **120** |
| 通过测试 | 109 |
| 失败测试 | 11 (集成测试) |
| 覆盖率估计 | ~35% |

---

> 备忘录维护: 每次添加新测试后更新此文档
