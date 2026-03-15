using DocumentFormat.OpenXml.Office.SpreadSheetML.Y2023.MsForms;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using static Org.BouncyCastle.Crypto.Engines.SM2Engine;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "用户交互插件。用于请求用户批准操作或让用户从选项中选择。", Version = "1.2")]
    public class InteractionPlugin : BasePlugin
    {
        private readonly IRepository<ChatMessageToolCall> _toolCallRepository;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        private const int PollIntervalMs = 1000;

        /// <summary>
        /// 默认超时时间（秒）
        /// </summary>
        private const int DefaultTimeoutSeconds = 300;

        public InteractionPlugin(IServiceProvider serviceProvider, IRepository<ChatMessageToolCall> toolCallRepository)
            : base(serviceProvider)
        {
            _toolCallRepository = toolCallRepository;
        }

        /// <summary>
        /// 向用户请求批准或让用户选择选项
        /// </summary>
        [KernelFunction]
        [Description("向用户请求批准或让用户从选项列表中选择。mode=approve 时请求操作批准；mode=choice 时让用户选择选项。")]
        public async Task<UserInteractionResponse> AskUserAsync(
            [Description("交互模式：approve（请求批准）或 choice（选择）")] string mode,
            [Description("要询问的问题或操作描述")] string question,
            [Description("选项列表。approve 模式下留空使用默认选项[同意/拒绝]；choice 模式下必填")] List<string>? options = null,
            [Description("是否允许多选，仅 mode=choice 时有效，默认 false")] bool multiSelect = false,
            Kernel? kernel = null
        )
        {
            var context = kernel?.GetAgentExecutionContext();

            if (mode.ToLower() == "approve")
            {
                return await HandleUserApprove(question, options, context);
            }
            else if (mode.ToLower() == "choice")
            {
                // 选择模式
                if (options == null || options.Count == 0)
                {
                    throw new ArgumentException("choice 模式需要提供选项列表");
                }

                return await HandleUserChoice(question, options, multiSelect, context);
            }
            else
            {
                throw new ArgumentException("mode 必须是 'approve' 或 'choice'");
            }
        }

        private async Task<UserInteractionResponse> HandleUserApprove(string question, List<string>? options, AgentExecutionContext context)
        {
            var approvalOptions = options?.Count > 0 ? options : new List<string> { "同意", "拒绝" };

            var approveRequest = new UserInteractionRequest
            {
                Mode = "approve",
                Question = question,
                Options = approvalOptions,
                MultiSelect = false,
                IsPending = true,
                PendingMessage = "等待用户批准..."
            };

            var traceId = Guid.NewGuid().ToString("N");

            var toolName = "InteractionPlugin.AskUser";
            var input = ToDictionry(approveRequest);
            
            var toolUseId = await SaveToolUseAsync(toolName, input, traceId, context);
            await PublishToolUseEvent(context, toolUseId, toolName, input);

            var startTime = DateTime.UtcNow;

            while (true)
            {
                // 检查超时
                if ((DateTime.UtcNow - startTime).TotalSeconds > DefaultTimeoutSeconds)
                {
                    return new UserInteractionResponse
                    {
                        Mode = "approve",
                        Question = question,
                        Options = approvalOptions,
                        MultiSelect = false,
                        IsPending = false,
                        SelectedOptions = new List<string> { "超时" }
                    };
                }

                var toolCall = await _toolCallRepository.GetAsync(toolUseId);
                if (toolCall.Status == 0)
                {
                    // 等待一段时间后继续轮询
                    await Task.Delay(PollIntervalMs);
                }
                else
                {
                    // 得到结果
                    var userSelectedOptions = string.IsNullOrEmpty(toolCall.Output)
                        ? new List<string>()
                        : JsonConvert.DeserializeObject<List<string>>(toolCall.Output) ?? new List<string>();

                    await PublishToolResultEvent(context, toolCall.Id, userSelectedOptions);

                    return new UserInteractionResponse
                    {
                        Mode = "approve",
                        Question = question,
                        Options = approvalOptions,
                        MultiSelect = false,
                        IsPending = false,
                        SelectedOptions = userSelectedOptions
                    };
                }
            }
        }

        private async Task<UserInteractionResponse> HandleUserChoice(string question, List<string> options, bool multiSelect, AgentExecutionContext context)
        {
            var choiceRequest = new UserInteractionRequest
            {
                Mode = "choice",
                Question = question,
                Options = options,
                MultiSelect = multiSelect,
                IsPending = true,
                PendingMessage = "等待用户做出选择..."
            };

            var traceId = Guid.NewGuid().ToString("N");
            var input = ToDictionry(choiceRequest);

            var toolName = "InteractionPlugin.AskUser";
            var toolUseId = await SaveToolUseAsync(toolName, input, traceId, context);
            await PublishToolUseEvent(context, toolUseId, toolName, input);

            var startTime = DateTime.UtcNow;

            while (true)
            {
                // 检查超时
                if ((DateTime.UtcNow - startTime).TotalSeconds > DefaultTimeoutSeconds)
                {
                    return new UserInteractionResponse
                    {
                        Mode = "choice",
                        Question = question,
                        Options = options,
                        MultiSelect = multiSelect,
                        IsPending = false,
                        SelectedOptions = new List<string> { "超时" }
                    };
                }

                var toolCall = await _toolCallRepository.GetAsync(toolUseId);
                if (toolCall.Status == 0)
                {
                    await Task.Delay(PollIntervalMs);
                }
                else
                {
                    var userSelectedOptions = string.IsNullOrEmpty(toolCall.Output)
                        ? new List<string>()
                        : JsonConvert.DeserializeObject<List<string>>(toolCall.Output) ?? new List<string>();

                    await PublishToolResultEvent(context, toolCall.Id, userSelectedOptions);

                    return new UserInteractionResponse
                    {
                        Mode = "choice",
                        Question = question,
                        Options = options,
                        MultiSelect = multiSelect,
                        IsPending = false,
                        SelectedOptions = userSelectedOptions
                    };
                }
            }
        }

        private async Task PublishToolUseEvent(AgentExecutionContext context, long toolUseId, string toolName, Dictionary<string, object> input)
        {
            if (context.HasEventBus)
            {
                await context.PublishEventAsync(new ToolUseEvent
                {
                    Id = toolUseId.ToString(),
                    Name = toolName,
                    Input = input,
                });
            }
        }

        private async Task PublishToolResultEvent(AgentExecutionContext context, long toolUseId, List<string> userSelectedOptions)
        {
            if (context.HasEventBus)
            {
                await context.PublishEventAsync(new ToolResultEvent()
                {
                    ToolUseId = toolUseId.ToString(),
                    Content = JsonConvert.SerializeObject(userSelectedOptions),
                    IsError = false,
                });
            }
        }

        private async Task<long> SaveToolUseAsync(string name, Dictionary<string, object>? input, string traceId, AgentExecutionContext context)
        {
            var toolCall = await _toolCallRepository.AddAsync(new ChatMessageToolCall
            {
                RunId = context.GetRunId(),
                MessageId = context.GetMessageId(),
                Name = name,
                Input = input,
                Output = string.Empty,
                Status = 0,
                DurationMs = 0,
                TraceId = traceId
            });

            return toolCall.Id;
        }

        private Dictionary<string,object> ToDictionry(UserInteractionRequest request)
        {
            return new Dictionary<string, object>()
            {
                { "mode", request.Mode },
                { "question", request.Question  },
                { "options", request.Options },
                { "multiSelect", request.MultiSelect },
                { "isPending", request.IsPending },
                { "pendingMessage", request.PendingMessage }
            };
        }

    }

    public class UserInteractionRequest
    {
        /// <summary>
        /// 交互模式：approve 或 choice
        /// </summary>
        [JsonProperty("mode")]
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// 询问的问题或操作描述
        /// </summary> 
        [JsonProperty("question")]
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// 选项列表
        /// </summary>
        [JsonProperty("options")]
        public List<string> Options { get; set; } = new();

        /// <summary>
        /// 是否允许多选
        /// </summary>
        [JsonProperty("multiSelect")]
        public bool MultiSelect { get; set; }

        /// <summary>
        /// 是否等待用户响应
        /// </summary>
        [JsonProperty("isPending")]
        public bool IsPending { get; set; }

        /// <summary>
        /// 等待时的提示信息
        /// </summary>
        [JsonProperty("pendingMessage")]
        public string? PendingMessage { get; set; }
    }

    public class UserInteractionResponse : UserInteractionRequest
    {
        /// <summary>
        /// 用户选择的答案（响应后填充）
        /// </summary>
        [JsonProperty("selectedOptions")]
        public List<string>? SelectedOptions { get; set; }
    }
}
