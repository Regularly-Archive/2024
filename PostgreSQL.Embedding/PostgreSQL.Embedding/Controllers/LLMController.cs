using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices.LLama;
using PostgreSQL.Embedding.LLmServices.Extensions;
using System.Net;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
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
                var chatCompletion = llmService.ChatStreamAsync(model);
                await HttpContext.WriteStreamingChatCompletion(chatCompletion);
            }
            else
            {
                var chatCompletion = await llmService.ChatAsync(model);
                await HttpContext.WriteChatCompletion(chatCompletion);
            }
        }

        /// <summary>
        /// 与 OpenAI 兼容的嵌入接口
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("{provider}/embeddings")]
        public async Task Embedding(OpenAIEmbeddingModel model, string provider)
        {
            if (!Enum.TryParse<LlmServiceProvider>(provider, out LlmServiceProvider llmServiceProvider))
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await HttpContext.Response.WriteAsync("The parameter \"provider\" must be one of \"OpenAI\", \"LLama\" ,\"HuggingFace\"");
                return;
            }

            var llmService = _llmServiceFactory.Create(llmServiceProvider);
            var embedding = await llmService.Embedding(model);
            await HttpContext.WriteEmbedding(embedding);
        }

        /// <summary>
        /// 与 OpenAI 兼容的生成接口
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("{provider}/completions")]
        public async Task Completion(OpenAICompletionModel model, string provider)
        {
            if (!Enum.TryParse<LlmServiceProvider>(provider, out LlmServiceProvider llmServiceProvider))
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await HttpContext.Response.WriteAsync("The parameter \"provider\" must be one of \"OpenAI\", \"LLama\" ,\"HuggingFace\"");
                return;
            }

            var llmService = _llmServiceFactory.Create(llmServiceProvider);
            var completion = await llmService.CompletionAsync(model);
            await HttpContext.WriteCompletion(completion);
        }
    }
}
