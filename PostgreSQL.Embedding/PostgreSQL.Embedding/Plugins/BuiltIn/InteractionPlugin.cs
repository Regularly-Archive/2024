using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "用户交互插件。用于请求用户批准操作或让用户从选项中选择。", Version = "1.2")]
    public class InteractionPlugin : BasePlugin
    {
        public InteractionPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        /// <summary>
        /// 向用户请求批准或让用户选择选项
        /// </summary>
        [KernelFunction]
        [Description("向用户请求批准或让用户从选项列表中选择。mode=approve 时请求操作批准；mode=choice 时让用户选择选项。")]
        public async Task<UserInteractionResult> AskUser(
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
                // 批准模式：返回预设选项
                var approvalOptions = options?.Count > 0
                    ? options
                    : new List<string> { "同意", "拒绝" };

                return new UserInteractionResult
                {
                    Mode = "approve",
                    Question = question,
                    Options = approvalOptions,
                    MultiSelect = false,
                    IsPending = true,
                    PendingMessage = "等待用户批准..."
                };
            }
            else if (mode.ToLower() == "choice")
            {
                // 选择模式
                if (options == null || options.Count == 0)
                {
                    throw new ArgumentException("choice 模式需要提供选项列表");
                }

                return new UserInteractionResult
                {
                    Mode = "choice",
                    Question = question,
                    Options = options,
                    MultiSelect = multiSelect,
                    IsPending = true,
                    PendingMessage = "等待用户选择..."
                };
            }
            else
            {
                throw new ArgumentException("mode 必须是 'approve' 或 'choice'");
            }
        }
    }

    public class UserInteractionResult
    {
        /// <summary>
        /// 交互模式：approve 或 choice
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// 询问的问题或操作描述
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// 选项列表
        /// </summary>
        public List<string> Options { get; set; } = new();

        /// <summary>
        /// 是否允许多选
        /// </summary>
        public bool MultiSelect { get; set; }

        /// <summary>
        /// 是否等待用户响应
        /// </summary>
        public bool IsPending { get; set; }

        /// <summary>
        /// 等待时的提示信息
        /// </summary>
        public string? PendingMessage { get; set; }

        /// <summary>
        /// 用户选择的答案（响应后填充）
        /// </summary>
        public List<string>? SelectedOptions { get; set; }
    }
}
