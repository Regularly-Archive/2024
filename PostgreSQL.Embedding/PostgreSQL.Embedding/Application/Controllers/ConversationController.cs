using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Llm.Abstractions;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConversationController : ControllerBase
    {
        private readonly IConversationService _conversationService;
        private readonly IChatHistoriesService _chatHistoryService;

        public ConversationController(
            IConversationService conversationService,
            IChatHistoriesService chatHistoryService)
        {
            _conversationService = conversationService;
            _chatHistoryService = chatHistoryService;
        }

        [HttpPost("{appId}")]
        public async Task ChatAsync(ConversationRequestModel model, long appId, CancellationToken cancellationToken)
        {
            await _conversationService.InvokeAsync(model, appId, HttpContext, cancellationToken);
        }

        /// <summary>
        /// V2 Chat endpoint with Anthropic-compatible SSE streaming format.
        /// Returns IAsyncEnumerable{ISseEvent} for better client interoperability.
        /// </summary>
        [HttpPost("v2/{appId}")]
        public IActionResult ChatV2Async(ConversationRequestModel model, long appId, CancellationToken cancellationToken)
        {
            var input = model.Messages.LastOrDefault()?.content;
            var events = _conversationService.InvokeStreamingV2Async(
                model,
                appId,
                input,
                model.ConversationId,
                cancellationToken);

            return new SseResult(events);
        }

        [HttpGet("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> GetConversationMessagesAsync(long appId, string conversationId)
        {
            var messsages = await _chatHistoryService.GetConversationMessagesAsync(appId, conversationId);
            return ApiResult.Success(messsages);
        }

        [HttpGet("{appId}/histories")]
        public async Task<JsonResult> GetConversationsAsync(long appId)
        {
            var conversations = await _chatHistoryService.GetAppConversationsAsync(appId);
            return ApiResult.Success(conversations);
        }

        [HttpDelete("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> DeleteConversationAsync(long appId, string conversationId)
        {
            await _chatHistoryService.DeleteConversationAsync(appId, conversationId);
            return ApiResult.Success<object>(null);
        }

        [HttpPut("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> UpdateConversationAsync(long appId, string conversationId, [FromBody] AppConversation conversation)
        {
            await _chatHistoryService.UpdateConversationAsync(appId, conversationId, conversation.Title);
            return ApiResult.Success<object>(null);
        }

        [HttpDelete("{appId}/histories/{conversationId}/{messageId}")]
        public async Task<JsonResult> DeleteConversationMessageAsync(long appId, string conversationId, long messageId)
        {
            await _chatHistoryService.DeleteConversationMessageAsync(messageId);
            return ApiResult.Success<object>(null);
        }

        [HttpGet("{appId}/recommend")]
        public JsonResult GetRecommendedTopicsAsync()
        {
            // Todo: 实现真正的推荐算法
            var topics = new List<string>()
            {
                "今天的天气怎么样",
                "金庸小说《连城诀》主要剧情是什么？",
                "智能体和大模型是什么关系？",
                "苏轼和辛弃疾的作品风格各自有什么不同？",
                "如何评价金庸小说《白马啸西风》？",
                "历史上的今天都发生过哪些重要事件"
            };

            return ApiResult.Success<object>(topics);
        }
    }
}
