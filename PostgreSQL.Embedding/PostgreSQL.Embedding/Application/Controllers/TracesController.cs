using Azure;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using System.Text.Json;
using static PostgreSQL.Embedding.Plugins.BuiltIn.ArtifactsPlugin;

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
        /// 提交用户交互响应
        /// </summary>
        [HttpPost("{messageId}/toolcalls/{toolcallId}/respond")]
        public async Task<JsonResult> SubmitUserResponseAsync(long messageId, long toolcallId, [FromBody] UserInteractionRequest request)
        {
            var toolCall = await _toolCallRepository.GetAsync(toolcallId);
            if (toolCall == null)
            {
                return ApiResult.Failure("Tool call not found");
            }

            // 检查是否已经响应过
            if (toolCall.Status != 0)
            {
                return ApiResult.Failure("Already responded");
            }

            // 更新 tool call 状态和输出
            toolCall.Status = 1; // success
            toolCall.Output = JsonSerializer.Serialize(request.SelectedOptions, JsonSerializerOptions.Default);
            toolCall.DurationMs = (decimal)DateTime.Now.Subtract(toolCall.CreatedAt.Value).TotalMilliseconds;

            await _toolCallRepository.UpdateAsync(toolCall);

            return ApiResult.Success(true);
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
            var artifactData = artifacts.Select(x => new ArtifactData()
            {
                Id = x.ArtifactId,
                FileName = x.FileName,
                AccessUrl = x.Url,
                Type = ((ArtifactType)x.ArtifactType).ToString().ToLowerInvariant(),
                CanPreview = x.CanPreview,
                CanDownload = x.CanDownload,
                FileSize = x.FileSize,
                CreatedAt = x.CreatedAt.Value
            }).ToList();
            return ApiResult.Success(artifactData);
        }
    }

    public class UserInteractionRequest
    {
        public List<string>? SelectedOptions { get; set; }
    }
}
