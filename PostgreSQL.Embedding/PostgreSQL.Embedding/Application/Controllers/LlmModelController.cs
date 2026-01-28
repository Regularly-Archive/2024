using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Domain.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Abstractions;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LlmModelController : CrudBaseController<LlmModel, LlmModelQueryableFilter>
    {
        private readonly IRepository<LlmModel> _llmModelRepository;
        private readonly IKernelService _kernelService;
        public LlmModelController(
            CrudBaseService<LlmModel> crudBaseService,
            IRepository<LlmModel> llmModelRepository,
            IKernelService kernelService) : base(crudBaseService)
        {
            _llmModelRepository = llmModelRepository;
            _kernelService = kernelService;
        }

        [HttpGet("{id}/test")]
        public async Task<JsonResult> TestConnection(long id)
        {
            var llmModel = await _crudBaseService.GetByIdAsync(id);
            if (llmModel == null) return ApiResult.Failure("模型不存在");


            var kernel = await _kernelService.GetKernel(llmModel, initializeTools: false);

            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory
            {
                new ChatMessageContent(AuthorRole.User, "Hi")
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(chatHistory);

            return ApiResult.Success<object>(null, message: "模型服务状态正常");
        }

        [HttpPatch("{id}/SetAsDefault")]

        public async Task<JsonResult> SetAsDefault(long id)
        {
            var llmModel = await _crudBaseService.GetByIdAsync(id);
            if (llmModel == null) return ApiResult.Failure("请检查当前模型信息是否正确");

            var defaultLlmModel = await _crudBaseService.Repository.FindAsync(x => x.IsDefaultModel == true && x.ModelType == llmModel.ModelType);
            if (defaultLlmModel != null)
            {
                defaultLlmModel.IsDefaultModel = false;
                await _crudBaseService.Repository.UpdateAsync(defaultLlmModel);
            }

            llmModel.IsDefaultModel = true;
            await _crudBaseService.Repository.UpdateAsync(llmModel);

            return ApiResult.Success(llmModel);
        }
    }
}
