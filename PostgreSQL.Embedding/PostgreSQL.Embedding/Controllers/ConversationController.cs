using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using PostgreSQL.Embedding.Common.Models.WebApi;
using static LLama.Common.ChatHistory;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConversationController : ControllerBase
    {
        private readonly IConversationService _conversationService;
        private readonly IChatHistoriesService _chatHistoryService;
        public ConversationController(IConversationService conversationService, IChatHistoriesService chatHistoryService)
        {
            _conversationService = conversationService;
            _chatHistoryService = chatHistoryService;
        }

        [HttpPost("{appId}")]
        public async Task Chat(OpenAIModel model, long appId, CancellationToken cancellationToken = default)
        {
            await _conversationService.Invoke(model, appId, HttpContext);
        }

        [HttpGet("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> GetConversationMessages(long appId, string conversationId)
        {
            var messsages = await _chatHistoryService.GetConversationMessages(appId, conversationId);
            return ApiResult.Success(messsages);
        }

        [HttpGet("{appId}/histories")]
        public async Task<JsonResult> GetConversations(long appId)
        {
            var conversations = await _chatHistoryService.GetAppConversations(appId);
            return ApiResult.Success(conversations);
        }

        [HttpDelete("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> DeleteConversation(long appId, string conversationId)
        {
            await _chatHistoryService.DeleteConversation(appId, conversationId);
            return ApiResult.Success<object>(null);
        }

        [HttpPut("{appId}/histories/{conversationId}")]
        public async Task<JsonResult> UpdateConversation(long appId, string conversationId, [FromBody] AppConversation conversation)
        {
            await _chatHistoryService.UpdateConversation(appId, conversationId, conversation.Summary);
            return ApiResult.Success<object>(null);
        }
    }
}
