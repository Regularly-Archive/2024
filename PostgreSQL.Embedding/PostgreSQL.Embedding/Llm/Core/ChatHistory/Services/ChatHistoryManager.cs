using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core.ChatHistory.Models;
using System.Text.Json;

namespace PostgreSQL.Embedding.Llm.Core.ChatHistory.Services
{
    /// <summary>
    /// 聊天历史管理器
    /// </summary>
    public class ChatHistoryManager
    {
        private readonly ChatHistoryConfig _config;
        private readonly Kernel _kernel;
        private readonly IChatHistoriesService _chatHistoriesService;
        private readonly IRepository<AppConversationState> _stateRepository;

        private ConversationState _state = new();

        public ChatHistoryManager(
            ChatHistoryConfig config,
            Kernel kernel,
            IChatHistoriesService chatHistoriesService,
            IRepository<AppConversationState> stateRepository)
        {
            _config = config;
            _kernel = kernel;
            _chatHistoriesService = chatHistoriesService;
            _stateRepository = stateRepository;
        }

        /// <summary>
        /// 获取上下文（自动处理加载/压缩/保存）
        /// </summary>
        public async Task<List<(string Role, string Content)>> GetOrCreateContextAsync(long appId, string conversationId)
        {
            await LoadStateAsync(conversationId);

            // 2. 查询所有消息
            var allMessages = await _chatHistoriesService.GetConversationMessagesAsync(appId, conversationId);
            if (allMessages.Count == 0)
                return new List<(string, string)>();

            // 3. 检查是否需要压缩
            var activeStartIndex = _state.GetActiveStartIndex();
            var activeMessages = allMessages.Skip(activeStartIndex).ToList();
            var activeRounds = activeMessages.Count / 2;

            // 4. 达到压缩条件，触发压缩
            if (activeRounds >= _config.ActiveRounds)
            {
                await CompressAsync(allMessages);
                await SaveStateAsync(conversationId);
            }

            // 5. 构建上下文
            return BuildContext(allMessages);
        }

        /// <summary>
        /// 加载状态
        /// </summary>
        private async Task LoadStateAsync(string conversationId)
        {
            var entity = await _stateRepository.FindAsync(x => x.ConversationId == conversationId);
            _state = ConversationState.FromEntity(entity);
        }

        /// <summary>
        /// 保存状态
        /// </summary>
        private async Task SaveStateAsync(string conversationId)
        {
            var entity = _state.ToEntity(conversationId);

            var existing = await _stateRepository.FindAsync(x => x.ConversationId == conversationId);
            if (existing != null)
            {
                existing.BlockTopic = _state.CompressedBlock?.Topic;
                existing.BlockSummary = _state.CompressedBlock?.Summary;
                existing.BlockKeyPoints = _state.CompressedBlock?.KeyPoints;
                existing.BlockHint = _state.CompressedBlock?.Hint;
                existing.BlockStartMsgId = _state.CompressedBlock?.StartMsgId;
                existing.BlockEndMsgId = _state.CompressedBlock?.EndMsgId;
                existing.BlockCompressedAt = _state.CompressedBlock?.CompressedAt;
                existing.CompressedMessageCount = _state.CompressedMessageCount;
                existing.UpdatedAt = DateTime.Now;
                await _stateRepository.UpdateAsync(existing);
            }
            else
            {
                await _stateRepository.AddAsync(entity);
            }
        }

        /// <summary>
        /// 构建上下文
        /// </summary>
        private List<(string Role, string Content)> BuildContext(List<ChatMessage> allMessages)
        {
            var result = new List<(string Role, string Content)>();

            // 1. 压缩块转 XML
            if (_state.CompressedBlock != null)
            {
                result.Add(("system", _state.CompressedBlock.ToXml()));
            }

            // 2. 活跃消息
            var activeStartIndex = _state.GetActiveStartIndex();
            var activeMessages = allMessages.Skip(activeStartIndex).ToList();

            foreach (var msg in activeMessages)
            {
                var roleName = msg.IsUserMessage ? "user" : "assistant";
                result.Add((roleName, msg.Content));
            }

            return result;
        }

        /// <summary>
        /// 压缩
        /// </summary>
        private async Task CompressAsync(List<ChatMessage> allMessages)
        {
            var activeStartIndex = _state.GetActiveStartIndex();
            var activeMessages = allMessages.Skip(activeStartIndex).ToList();

            var activeRounds = activeMessages.Count / 2;
            var toCompressRounds = activeRounds - _config.BufferRounds;

            if (toCompressRounds <= 0)
                return;

            var toCompressCount = toCompressRounds * 2;

            // 收集待压缩内容
            var toCompressMessages = new List<(long Id, bool IsUserMessage, string Content)>();

            // 压缩块覆盖的原始消息
            if (_state.CompressedBlock != null && _state.CompressedMessageCount > 0)
            {
                var blockMsgs = allMessages
                    .Take(_state.CompressedMessageCount)
                    .Select(m => (m.Id, m.IsUserMessage, m.Content))
                    .ToList();
                toCompressMessages.AddRange(blockMsgs);
            }

            // 需要压缩的活跃消息
            var activeToCompress = activeMessages
                .Take(toCompressCount)
                .Select(m => (m.Id, m.IsUserMessage, m.Content))
                .ToList();
            toCompressMessages.AddRange(activeToCompress);

            // 调用 AI 压缩
            var newBlock = await CompressWithAIAsync(toCompressMessages);

            // 更新状态
            _state.CompressedBlock = newBlock;
            _state.CompressedMessageCount = toCompressMessages.Count;
        }

        /// <summary>
        /// 调用 AI 压缩
        /// </summary>
        private async Task<CompressedBlock> CompressWithAIAsync(
            List<(long Id, bool IsUserMessage, string Content)> messages)
        {
            var startId = 1;
            var endId = messages.Count;

            var prompt = $@"
            压缩以下对话（按轮次），输出 JSON {{
                ""topic"": ""一句话主题"",
                ""summary"": ""2-4句摘要"",
                ""key_points"": [""关键点1"", ""关键点2"", ""关键点3""],
                ""hint"": ""如需完整内容，可通过 RefID [1-{messages.Count}] 召回""
            }}：

            {string.Join("\n\n", messages.Select(m => $"{(m.IsUserMessage ? "用户" : "AI")}: {m.Content}"))}";

            try
            {
                var svc = _kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                chatHistory.AddUserMessage(prompt);

                var settings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        { "temperature", 0.3 },
                        { "max_tokens", 1000 }
                    }
                };

                var result = await svc.GetChatMessageContentsAsync(chatHistory, settings);

                var json = result[0].Content.Replace("```json","").Replace("```","");
                var data = JsonSerializer.Deserialize<JsonElement>(json);

                var keyPoints = new List<string>();
                if (data.TryGetProperty("key_points", out var kpElement))
                {
                    foreach (var kp in kpElement.EnumerateArray())
                    {
                        keyPoints.Add(kp.GetString() ?? "");
                    }
                }

                return new CompressedBlock
                {
                    StartMsgId = (int)startId,
                    EndMsgId = (int)endId,
                    Topic = data.GetProperty("topic").GetString() ?? "",
                    Summary = data.GetProperty("summary").GetString() ?? "",
                    KeyPoints = keyPoints,
                    Hint = $"如需完整内容，可通过 RefID [1-{messages.Count}] 召回",
                    CompressedAt = DateTime.UtcNow,
                    
                };
            }
            catch (Exception ex)
            {
                return new CompressedBlock
                {
                    StartMsgId = (int)startId,
                    EndMsgId = (int)endId,
                    Topic = "历史对话",
                    Summary = $"包含 {messages.Count / 2} 轮对话的摘要",
                    Hint = $"如需完整内容，可通过 RefID [1-{messages.Count}] 召回",
                    CompressedAt = DateTime.UtcNow
                };
            }
        }
    }
}
