using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Net;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly IConversationService _chatService;
        public ChatController(IConversationService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost]
        [Route("{appId}")]
        public async Task Chat(OpenAIModel model, string appId)
        {
            await _chatService.Chat(model, appId, HttpContext);
        }
    }
}
