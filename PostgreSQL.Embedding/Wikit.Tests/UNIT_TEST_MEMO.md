# 单元测试备忘录

> 生成日期: 2026-01-23
> 目标: 梳理项目代码，列出缺少单元测试的模块

---

## 一、现有测试 (8个测试文件)

| 文件路径 | 测试内容 | 优先级 |
|----------|----------|--------|
| `Agents/TaskPlanner_Tests.cs` | 任务规划器 | ✅ 已完成 |
| `Agents/SystemStep_Tests.cs` | 系统步骤 | ✅ 已完成 |
| `Plugins/When_Call_BingSearchPlugin.cs` | Bing搜索插件 | ✅ 已完成 |
| `Plugins/When_Call_BraveSearchPlugin.cs` | Brave搜索插件 | ✅ 已完成 |
| `Plugins/When_Call_WeiXinSearchPlugin.cs` | 微信搜索插件 | ✅ 已完成 |
| `Reranker/When_Call_Reranker.cs` | 重排序器 | ✅ 已完成 |
| `When_Call_Regex.cs` | 正则工具 | ✅ 已完成 |
| `LlmServices/AnthropicChatCompletionService_Tests.cs` | Anthropic Chat Completion | ✅ 已完成 |

---

## 二、缺少测试的模块

### 🔴 高优先级 - 核心业务逻辑

| 模块 | 文件/类 | 建议测试内容 | 复杂度 |
|------|---------|--------------|--------|
| **Controllers** | `KnowledgeBaseController.cs` | 知识库 CRUD、搜索、问答 | ⭐⭐⭐ |
| | `LlmAppController.cs` | LLM 应用管理、插件关联 | ⭐⭐⭐ |
| | `ConversationController.cs` | 对话管理、流式响应 | ⭐⭐⭐ |
| | `DocumentController.cs` | 文档处理、导入导出 | ⭐⭐ |
| | `LLMController.cs` | LLM 模型管理 | ⭐⭐ |
| **Services** | `ConversationService.cs` | 对话服务、流式处理 | ⭐⭐⭐ |
| | `GenericConversationService.cs` | 通用对话服务 | ⭐⭐⭐ |
| | `MinioFileStorageService.cs` | MinIO 文件存储 | ⭐⭐ |
| | `NotificationService.cs` | 通知服务 | ⭐⭐ |

### 🟡 中优先级 - LLM 服务

| 模块 | 文件/类 | 建议测试内容 | 复杂度 |
|------|---------|--------------|--------|
| **LlmServices** | `KernalService.cs` | Kernel 构建、插件注册 | ⭐⭐⭐ |
| | `LlmServiceFactory.cs` | 服务工厂、模型选择 | ⭐⭐ |
| | `LlmCompletionRouter.cs` | 路由选择、认证头处理 | ⭐⭐ |
| | `HuggingFaceService.cs` | HuggingFace 嵌入 | ⭐⭐ |
| | `LLamaService.cs` | 本地 LLama 模型 | ⭐⭐⭐ |
| | `OllamaService.cs` | Ollama 服务 | ⭐⭐ |
| | `PromptTemplateService.cs` | 模板服务 | ⭐ |
| **Routers** | `LlmEmbeddingRouter.cs` | 嵌入路由 | ⭐⭐ |
| | `LlmCompletionRouter.cs` | 完成路由 | ⭐⭐ |

### 🟢 低优先级 - 工具和辅助类

| 模块 | 文件/类 | 建议测试内容 | 复杂度 |
|------|---------|--------------|--------|
| **Utils** | `ShortUrlGenerator.cs` | 短链接生成 | ⭐ |
| | `WebPageExtractor.cs` | 网页提取 | ⭐⭐ |
| | `RSSExtractor.cs` | RSS 解析 | ⭐⭐ |
| | `KernelPluginsExtensions.cs` | 插件扩展 | ⭐⭐ |
| | `CSnakeExtensions.cs` | Python 集成 | ⭐⭐⭐ |
| **Handlers** | `FileImportingTaskHandler.cs` | 文件导入处理 | ⭐⭐ |
| | `TextImportingTaskHandler.cs` | 文本导入处理 | ⭐⭐ |
| | `OpenAIProxyHandler.cs` | OpenAI 代理 | ⭐⭐ |
| **Plugins** | `BasePlugin.cs` | 插件基类 | ⭐⭐ |
| | `CodeInterpreterPlugin.cs` | 代码解释器 | ⭐⭐⭐ |
| | `MailKitPlugin.cs` | 邮件插件 | ⭐⭐ |
| | `FireCrawlPlugin.cs` | 网页抓取 | ⭐⭐ |
| | `NMCWeatherPlugin.cs` | 天气插件 | ⭐ |
| | `CloudMusicPlugin.cs` | 音乐插件 | ⭐⭐ |

---

## 三、按优先级排序的测试任务

### Sprint 1: 核心对话和 LLM 服务
- [ ] `ConversationService_Tests.cs` - 对话服务测试
- [ ] `GenericConversationService_Tests.cs` - 通用对话服务测试
- [ ] `KernalService_Tests.cs` - Kernel 服务测试
- [ ] `LlmCompletionRouter_Tests.cs` - 路由测试

### Sprint 2: 控制器层
- [ ] `KnowledgeBaseController_Tests.cs` - 知识库控制器测试
- [ ] `ConversationController_Tests.cs` - 对话控制器测试
- [ ] `LlmAppController_Tests.cs` - LLM 应用控制器测试

### Sprint 3: 其他 LLM 服务
- [ ] `HuggingFaceService_Tests.cs` - HuggingFace 测试
- [ ] `LLamaService_Tests.cs` - LLama 服务测试
- [ ] `OllamaService_Tests.cs` - Ollama 服务测试

### Sprint 4: 工具类和其他
- [ ] `WebPageExtractor_Tests.cs` - 网页提取测试
- [ ] `CodeInterpreterPlugin_Tests.cs` - 代码解释器测试
- [ ] `ShortUrlGenerator_Tests.cs` - 短链接测试

---

## 四、测试建议

### 测试框架
- **xUnit** - 已有
- **Moq** - 已有
- **Shouldly** - 已有
- **Microsoft.AspNetCore.Mvc.Testing** - 建议添加 (控制器集成测试)

### 测试策略
1. **Mock 外部依赖** - HTTP 调用、数据库、文件系统
2. **集成测试** - 使用 Testcontainers 进行数据库测试
3. **Controller 测试** - 使用 `WebApplicationFactory`

### 示例测试代码结构

```csharp
public class ConversationService_Tests
{
    private readonly Mock<IRepository<Conversation>> _conversationRepo;
    private readonly Mock<IChatCompletionService> _chatCompletionService;
    private readonly ConversationService _service;

    public ConversationService_Tests()
    {
        _conversationRepo = new Mock<IRepository<Conversation>>();
        _chatCompletionService = new Mock<IChatCompletionService>();
        _service = new ConversationService(_conversationRepo.Object, _chatCompletionService.Object);
    }

    [Fact]
    public async Task GetConversation_Should_Return_Conversation_When_Exists()
    {
        // Arrange
        var conversationId = 1L;
        var expected = new Conversation { Id = conversationId, Title = "Test" };
        _conversationRepo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<Conversation, bool>>>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.GetConversationAsync(conversationId);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(conversationId);
    }
}
```

---

## 五、当前测试覆盖率估计

| 分类 | 文件数 | 测试覆盖 |
|------|--------|----------|
| Agents | 2 | ~80% |
| Plugins | 3 | ~30% |
| Reranker | 1 | ~50% |
| Utils | 1 | ~20% |
| LlmServices | 1 | ~10% |
| **总计** | **8** | **~20%** |

---

## 六、行动计划

1. **本周**: 完成 `ConversationService` 和 `KernalService` 的测试
2. **下周**: 完成控制器层测试
3. **后续**: 逐步补充其他模块测试

---

> 备忘录维护: 每次添加新测试后更新此文档
