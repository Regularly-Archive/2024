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
    public class ConversationController : ControllerBase
    {
        private readonly IConversationService _conversationService;
        public ConversationController(IConversationService conversationService)
        {
            _conversationService = conversationService;
        }

        [HttpPost]
        [Route("{appId}")]
        public async Task Chat(OpenAIModel model, string appId)
        {
            await _conversationService.Invoke(model, appId, HttpContext);
        }
    }
}
