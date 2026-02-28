using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Infrastructure.DataAccess;

namespace PostgreSQL.Embedding.Application.Controllers
{
    [Route("api/traces")]
    [ApiController]
    public class TracesController : ControllerBase
    {
        private readonly IRepository<ChatMessageToolCall> _toolCallRepository;
        private readonly IRepository<ChatMessagePlan> _planRepository;
        private readonly IRepository<ChatMessageArtifact> _artifactRepository;

        public TracesController(
            IRepository<ChatMessageToolCall> toolCallRepository,
            IRepository<ChatMessagePlan> planRepository,
            IRepository<ChatMessageArtifact> artifactRepository)
        {
            _toolCallRepository = toolCallRepository;
            _planRepository = planRepository;
            _artifactRepository = artifactRepository;
        }

        /// <summary>
        /// 获取工具调用详情
        /// </summary>
        [HttpGet("{messageId}/toolcalls/{toolcallId}")]
        public async Task<JsonResult> GetToolCallAsync(long messageId, long toolcallId)
        {
            var toolCall = await _toolCallRepository.GetAsync(toolcallId);
            return ApiResult.Success(toolCall);
        }

        /// <summary>
        /// 获取消息的计划列表
        /// </summary>
        [HttpGet("{messageId}/plans")]
        public async Task<JsonResult> GetPlansAsync(long messageId)
        {
            var plans = await _planRepository.FindListAsync(x => x.MessageId == messageId);
            return ApiResult.Success(plans);
        }

        /// <summary>
        /// 获取消息的产物列表
        /// </summary>
        [HttpGet("{messageId}/artifacts")]
        public async Task<JsonResult> GetArtifactsAsync(long messageId)
        {
            var artifacts = await _artifactRepository.FindListAsync(x => x.MessageId == messageId);
            return ApiResult.Success(artifacts);
        }
    }
}
