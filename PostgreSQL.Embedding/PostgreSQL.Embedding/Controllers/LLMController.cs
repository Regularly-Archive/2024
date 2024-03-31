using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Net;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LLMController : ControllerBase
    {
        private readonly ILlmServiceFactory _llmServiceFactory;
        private readonly ILogger<LLMController> _logger;
        public LLMController(ILlmServiceFactory llmServiceFactory, ILogger<LLMController> logger)
        {
            _llmServiceFactory = llmServiceFactory;
            _logger = logger;
        }

        /// <summary>
        /// 与 OpenAI 兼容的聊天接口
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("{provider}/chat/completions")]
        public async Task Chat(OpenAIModel model, string provider)
        {
            if (!Enum.TryParse<LlmServiceProvider>(provider, out LlmServiceProvider llmServiceProvider))
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await HttpContext.Response.WriteAsync("The parameter \"provider\" must be one of \"OpenAI\", \"LLama\" ,\"HuggingFace\"");
                return;
            }

            var llmService = _llmServiceFactory.Create(llmServiceProvider);
            if (model.stream)
            {
                await llmService.ChatStream(model, HttpContext);
            }
            else
            {
                await llmService.Chat(model, HttpContext);
            }
        }

        /// <summary>
        /// 与 OpenAI 兼容的嵌入接口
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("{provider}/embeddings")]
        public async Task embedding(OpenAIEmbeddingModel model, string provider)
        {
            if (!Enum.TryParse<LlmServiceProvider>(provider, out LlmServiceProvider llmServiceProvider))
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await HttpContext.Response.WriteAsync("The parameter \"provider\" must be one of \"OpenAI\", \"LLama\" ,\"HuggingFace\"");
                return;
            }

            var llmService = _llmServiceFactory.Create(llmServiceProvider);
            await llmService.Embedding(model, HttpContext);
        }
    }
}
