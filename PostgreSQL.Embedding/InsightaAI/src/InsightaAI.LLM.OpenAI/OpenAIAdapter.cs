using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Text;
using System.Text.Json;

namespace InsightaAI.LLM.OpenAI;

/// <summary>
/// OpenAI 兼容适配器
/// 支持 OpenAI, DeepSeek, 通义千问, Ollama 等 OpenAI 兼容接口
/// </summary>
public class OpenAIAdapter : IProviderAdapter
{
    public string Name => "openai";
    public bool SupportsReasoning => true;
    public ReasoningMode SupportedReasoningModes => ReasoningMode.ReasoningEffort | ReasoningMode.ReasoningContent;

    // 需要使用 reasoning_effort 的模型前缀
    private static readonly string[] ReasoningEffortModels =
    [
        "o1", "o1-mini", "o1-preview",
        "o3", "o3-mini",
    ];

    // DeepSeek 模型前缀 (使用 reasoning_content)
    private static readonly string[] DeepSeekModels =
    [
        "deepseek-r1", "deepseek-reasoner"
    ];

    public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream)
    {
        var baseUrl = config.BaseUrl ?? "https://api.openai.com/v1";
        var endpoint = $"{baseUrl.TrimEnd('/')}/chat/completions";

        var body = BuildRequestBody(request, stream);
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // 设置认证头
        httpRequest.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        // 设置自定义头
        if (config.Headers != null)
        {
            foreach (var header in config.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return httpRequest;
    }

    public StreamEvent? ParseStreamEvent(string eventType, JsonElement data)
    {
        // 先提取 usage（可能和 choices 同时存在）
        TokenUsage? usage = null;
        if (data.TryGetProperty("usage", out var usageElement) &&
            usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = ParseUsage(usageElement);
        }

        // OpenAI 格式: data.choices[0].delta
        if (!data.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            // 没有 choices，但可能有 usage（最后一个 chunk）
            if (usage != null)
            {
                return new DoneEvent { Reason = DoneReason.Complete, Usage = usage };
            }
            return null;
        }

        var choice = choices[0];

        // 检查 finish_reason
        if (choice.TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.ValueKind == JsonValueKind.String)
        {
            var reason = finishReason.GetString();
            if (!string.IsNullOrEmpty(reason))
            {
                return new DoneEvent
                {
                    Reason = reason switch
                    {
                        "stop" => DoneReason.Complete,
                        "tool_calls" => DoneReason.ToolCalls,
                        "length" => DoneReason.MaxTokens,
                        _ => DoneReason.Complete
                    },
                    Usage = usage
                };
            }
        }

        // 解析 delta
        if (!choice.TryGetProperty("delta", out var delta))
        {
            if (usage != null)
            {
                return new DoneEvent { Reason = DoneReason.Complete, Usage = usage };
            }
            return null;
        }

        // 检查是否有工具调用
        if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
        {
            return ParseToolCallStreamDelta(toolCalls[0]);
        }

        // 检查是否有 reasoning_content (DeepSeek)
        if (delta.TryGetProperty("reasoning_content", out var reasoningContent) &&
            reasoningContent.ValueKind == JsonValueKind.String)
        {
            var text = reasoningContent.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                return new ThinkingDeltaEvent { Delta = text };
            }
        }

        // 普通文本内容
        if (delta.TryGetProperty("content", out var textContent) &&
            textContent.ValueKind == JsonValueKind.String)
        {
            var text = textContent.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                return new TextDeltaEvent { Delta = text };
            }
        }

        return null;
    }

    public LlmResponse ParseResponse(JsonElement response)
    {
        var content = new List<ContentBlock>();
        var model = string.Empty;
        var finishReason = DoneReason.Complete;
        TokenUsage? usage = null;

        if (response.TryGetProperty("model", out var modelElement))
        {
            model = modelElement.GetString() ?? string.Empty;
        }

        if (response.TryGetProperty("usage", out var usageElement))
        {
            usage = ParseUsage(usageElement);
        }

        if (response.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            var message = choice.GetProperty("message");

            // 解析文本内容
            if (message.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                var text = contentElement.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    content.Add(new TextBlock { Text = text });
                }
            }

            // 解析推理内容 (DeepSeek)
            if (message.TryGetProperty("reasoning_content", out var reasoningElement) &&
                reasoningElement.ValueKind == JsonValueKind.String)
            {
                var reasoning = reasoningElement.GetString();
                if (!string.IsNullOrEmpty(reasoning))
                {
                    content.Add(new ThinkingBlock { Thinking = reasoning });
                }
            }

            // 解析工具调用
            if (message.TryGetProperty("tool_calls", out var toolCalls))
            {
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    content.Add(ParseToolCall(toolCall));
                }
            }

            // 解析完成原因
            if (choice.TryGetProperty("finish_reason", out var finishElement))
            {
                finishReason = finishElement.GetString() switch
                {
                    "stop" => DoneReason.Complete,
                    "tool_calls" => DoneReason.ToolCalls,
                    "length" => DoneReason.MaxTokens,
                    _ => DoneReason.Complete
                };
            }
        }

        return new LlmResponse
        {
            Model = model,
            Content = content.ToArray(),
            FinishReason = finishReason,
            Usage = usage,
            RawResponse = response
        };
    }

    private object BuildRequestBody(LlmRequest request, bool stream)
    {
        var body = new OpenAIRequest
        {
            Model = request.Model,
            Stream = stream,
            Messages = request.Messages.Select(ConvertMessage).ToArray(),
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            Stop = request.StopSequences,
            StreamOptions = stream ? new { include_usage = true } : null
        };

        // 处理工具
        if (request.Tools is { Length: > 0 })
        {
            body.Tools = request.Tools.Select(t => new OpenAITool
            {
                Type = "function",
                Function = new OpenAIFunction
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.Schema
                }
            }).ToArray();

            // 工具调用策略
            body.ToolChoice = request.ToolChoice switch
            {
                ToolChoiceMode.None => "none",
                ToolChoiceMode.Auto => "auto"
            };
        }

        // 处理推理配置
        if (request.Reasoning?.Enabled == true)
        {
            var modelLower = request.Model.ToLowerInvariant();

            // 检查是否是需要 reasoning_effort 的模型
            if (ReasoningEffortModels.Any(m => modelLower.StartsWith(m)))
            {
                body.ReasoningEffort = request.Reasoning.Effort?.ToString().ToLowerInvariant() ?? "medium";
            }
            // DeepSeek 模型使用 temperature=0 来启用推理
            else if (DeepSeekModels.Any(m => modelLower.Contains(m)))
            {
                body.Temperature = 0;
            }
        }

        // OpenAI 特定选项
        if (request.ProviderOptions?.OpenAI != null)
        {
            body.User = request.ProviderOptions.OpenAI.User;
            body.ServiceTier = request.ProviderOptions.OpenAI.ServiceTier;
            body.ParallelToolCalls = request.ProviderOptions.OpenAI.ParallelToolCalls;
        }

        return body;
    }

    private static OpenAIMessage ConvertMessage(Message msg)
    {
        var message = new OpenAIMessage
        {
            Role = msg.Role switch
            {
                MessageRole.System => "system",
                MessageRole.User => "user",
                MessageRole.Assistant => "assistant",
                MessageRole.ToolResult => "tool",
                _ => "user"
            }
        };

        if (msg.Role == MessageRole.ToolResult)
        {
            message.ToolCallId = msg.ToolCallId;
            message.Content = msg.GetTextContent();
        }
        else if (msg.HasToolCalls)
        {
            // 助手消息带工具调用
            message.Content = msg.GetTextContent();
            message.ToolCalls = msg.GetToolCalls().Select(tc => new OpenAIToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OpenAIFunctionCall
                {
                    Name = tc.Name,
                    Arguments = tc.Arguments.GetRawText()
                }
            }).ToArray();
        }
        else
        {
            message.Content = msg.GetTextContent();
        }

        return message;
    }

    /// <summary>
    /// 解析 OpenAI SSE 流式工具调用 delta chunk。
    /// OpenAI 格式中，第一个 chunk 包含 id + name，后续 chunk 只包含 arguments 片段。
    /// </summary>
    private static StreamEvent ParseToolCallStreamDelta(JsonElement toolCall)
    {
        var index = 0;
        if (toolCall.TryGetProperty("index", out var indexElement))
        {
            index = indexElement.GetInt32();
        }

        var toolCallId = toolCall.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;

        if (toolCall.TryGetProperty("function", out var function))
        {
            var name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var arguments = function.TryGetProperty("arguments", out var argsElement) ? argsElement.GetString() : null;

            // 第一个 chunk: 有 name → ToolCallStartEvent
            if (!string.IsNullOrEmpty(name))
            {
                return new ToolCallStartEvent
                {
                    ContentIndex = index,
                    ToolName = name,
                    ToolCallId = toolCallId
                };
            }

            // 后续 chunk: 有 arguments → ToolCallDeltaEvent
            if (!string.IsNullOrEmpty(arguments))
            {
                return new ToolCallDeltaEvent
                {
                    ContentIndex = index,
                    ArgumentsDelta = arguments
                };
            }
        }

        return new ToolCallDeltaEvent { ContentIndex = index, ArgumentsDelta = "" };
    }

    private static ToolCallBlock ParseToolCall(JsonElement toolCall)
    {
        var id = toolCall.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
        var function = toolCall.GetProperty("function");
        var name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
        var argsRaw = function.TryGetProperty("arguments", out var argsElement) ? argsElement.GetString() ?? "{}" : "{}";

        JsonElement args;
        try
        {
            args = JsonSerializer.Deserialize<JsonElement>(argsRaw);
        }
        catch
        {
            args = JsonSerializer.SerializeToElement(new { });
        }

        return new ToolCallBlock
        {
            Id = id,
            Name = name,
            Arguments = args
        };
    }

    private static TokenUsage ParseUsage(JsonElement usage)
    {
        var inputTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
        var outputTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
        var cacheHit = 0;

        // OpenAI 缓存 token
        if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            if (details.TryGetProperty("cached_tokens", out var cached) && cached.ValueKind == JsonValueKind.Number)
            {
                cacheHit = cached.GetInt32();
            }
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheHitTokens = cacheHit
        };
    }
}
