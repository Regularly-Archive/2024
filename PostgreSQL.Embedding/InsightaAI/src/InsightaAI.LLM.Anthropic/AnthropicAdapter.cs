using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Anthropic;

/// <summary>
/// Anthropic 适配器 - 支持 Claude 系列模型
/// 支持 Extended Thinking 功能
/// </summary>
public class AnthropicAdapter : IProviderAdapter
{
    public string Name => "anthropic";
    public bool SupportsReasoning => true;
    public ReasoningMode SupportedReasoningModes => ReasoningMode.ExtendedThinking;

    // 支持 extended thinking 的模型
    private static readonly string[] ThinkingCapableModels =
    [
        "claude-sonnet-4", "claude-opus-4",
        "claude-3-5-sonnet", "claude-3-opus"
    ];

    public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream)
    {
        var baseUrl = config.BaseUrl ?? "https://api.anthropic.com";
        var endpoint = $"{baseUrl.TrimEnd('/')}/v1/messages";

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

        // Anthropic 使用自定义认证头
        httpRequest.Headers.Add("x-api-key", config.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

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
        try
        {
            return eventType switch
            {
                "message_start" => ParseMessageStart(data),
                "content_block_start" => ParseContentBlockStart(data),
                "content_block_delta" => ParseContentBlockDelta(data),
                "content_block_stop" => null, // 忽略
                "message_delta" => ParseMessageDelta(data),
                "message_stop" => new DoneEvent { Reason = DoneReason.Complete },
                "ping" => null,
                "error" => ParseError(data),
                _ => null
            };
        }
        catch
        {
            return null;
        }
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

        if (response.TryGetProperty("stop_reason", out var stopReason))
        {
            finishReason = stopReason.GetString() switch
            {
                "end_turn" => DoneReason.Complete,
                "tool_use" => DoneReason.ToolCalls,
                "max_tokens" => DoneReason.MaxTokens,
                _ => DoneReason.Complete
            };
        }

        if (response.TryGetProperty("content", out var contentArray))
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();

                switch (blockType)
                {
                    case "text":
                        var text = block.GetProperty("text").GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            content.Add(new TextBlock { Text = text });
                        }
                        break;

                    case "thinking":
                        var thinking = block.GetProperty("thinking").GetString();
                        if (!string.IsNullOrEmpty(thinking))
                        {
                            content.Add(new ThinkingBlock { Thinking = thinking });
                        }
                        break;

                    case "tool_use":
                        content.Add(ParseToolUseBlock(block));
                        break;
                }
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

    private AnthropicRequest BuildRequestBody(LlmRequest request, bool stream)
    {
        // 处理消息
        var systemPrompt = string.Empty;
        var messages = new List<AnthropicMessage>();

        foreach (var msg in request.Messages)
        {
            switch (msg.Role)
            {
                case MessageRole.System:
                    systemPrompt = msg.GetTextContent();
                    break;

                case MessageRole.User:
                    messages.Add(new AnthropicMessage
                    {
                        Role = "user",
                        Content = ConvertContentBlocks(msg.Content)
                    });
                    break;

                case MessageRole.Assistant:
                    messages.Add(new AnthropicMessage
                    {
                        Role = "assistant",
                        Content = ConvertContentBlocks(msg.Content)
                    });
                    break;

                case MessageRole.ToolResult:
                    messages.Add(new AnthropicMessage
                    {
                        Role = "user",
                        Content = new object[] { new AnthropicToolResultContent
                        {
                            ToolUseId = msg.ToolCallId ?? "",
                            Content = msg.GetTextContent(),
                            IsError = false
                        }}
                    });
                    break;
            }
        }

        var body = new AnthropicRequest
        {
            Model = request.Model,
            Stream = stream,
            MaxTokens = request.MaxTokens ?? 4096, // Anthropic 需要必填的 max_tokens
            System = systemPrompt,
            Messages = messages.ToArray()
        };

        // 处理工具
        if (request.Tools is { Length: > 0 })
        {
            body.Tools = request.Tools.Select(t => new AnthropicTool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.Schema
            }).ToArray();
        }

        // 处理推理配置 (Extended Thinking)
        if (request.Reasoning?.Enabled == true)
        {
            var modelLower = request.Model.ToLowerInvariant();
            var supportsThinking = ThinkingCapableModels.Any(m => modelLower.Contains(m.ToLowerInvariant()));

            if (supportsThinking)
            {
                body.Thinking = new AnthropicThinkingConfig
                {
                    Type = "enabled",
                    BudgetTokens = request.Reasoning.BudgetTokens ?? 10000
                };

                // Extended thinking 需要 temperature=1
                body.Temperature = 1;
            }
        }
        else if (request.Temperature.HasValue)
        {
            body.Temperature = request.Temperature;
        }

        // Anthropic 特定选项
        if (request.ProviderOptions?.Anthropic != null)
        {
            if (request.ProviderOptions.Anthropic.TopK.HasValue)
            {
                body.TopK = request.ProviderOptions.Anthropic.TopK;
            }
            if (request.ProviderOptions.Anthropic.TopP.HasValue)
            {
                body.TopP = request.ProviderOptions.Anthropic.TopP;
            }
        }

        // 停止序列
        if (request.StopSequences is { Length: > 0 })
        {
            body.StopSequences = request.StopSequences;
        }

        return body;
    }

    private static object[] ConvertContentBlocks(ContentBlock[] blocks)
    {
        var result = new List<object>();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    result.Add(new AnthropicTextContent { Text = text.Text });
                    break;

                case ImageBlock image:
                    result.Add(new AnthropicImageContent
                    {
                        Source = new AnthropicImageSource
                        {
                            MediaType = image.Source.MediaType,
                            Data = image.Source.Data
                        }
                    });
                    break;

                case ToolCallBlock toolCall:
                    result.Add(new AnthropicToolUseContent
                    {
                        Id = toolCall.Id,
                        Name = toolCall.Name,
                        Input = toolCall.Arguments
                    });
                    break;

                case ThinkingBlock thinking:
                    result.Add(new AnthropicThinkingContent
                    {
                        Thinking = thinking.Thinking
                    });
                    break;
            }
        }

        return result.ToArray();
    }

    private static StreamEvent? ParseMessageStart(JsonElement data)
    {
        // message_start 包含 message 对象，可以提取 model 信息
        return new StreamStartEvent
        {
            Model = data.GetProperty("message").TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
            Provider = "anthropic"
        };
    }

    private static StreamEvent ParseContentBlockStart(JsonElement data)
    {
        var index = data.GetProperty("index").GetInt32();
        var contentBlock = data.GetProperty("content_block");
        var type = contentBlock.GetProperty("type").GetString();

        return type switch
        {
            "text" => new TextStartEvent { ContentIndex = index },
            "thinking" => new ThinkingStartEvent { ContentIndex = index },
            "tool_use" => new ToolCallStartEvent
            {
                ContentIndex = index,
                ToolName = contentBlock.GetProperty("name").GetString() ?? "",
                ToolCallId = contentBlock.TryGetProperty("id", out var id) ? id.GetString() : null
            },
            _ => new TextStartEvent { ContentIndex = index }
        };
    }

    private static StreamEvent ParseContentBlockDelta(JsonElement data)
    {
        var index = data.GetProperty("index").GetInt32();
        var delta = data.GetProperty("delta");
        var type = delta.GetProperty("type").GetString();

        return type switch
        {
            "text_delta" => new TextDeltaEvent
            {
                ContentIndex = index,
                Delta = delta.GetProperty("text").GetString() ?? ""
            },
            "thinking_delta" => new ThinkingDeltaEvent
            {
                ContentIndex = index,
                Delta = delta.GetProperty("thinking").GetString() ?? ""
            },
            "input_json_delta" => new ToolCallDeltaEvent
            {
                ContentIndex = index,
                ArgumentsDelta = delta.GetProperty("partial_json").GetString() ?? ""
            },
            _ => new TextDeltaEvent { ContentIndex = index, Delta = "" }
        };
    }

    private static StreamEvent ParseMessageDelta(JsonElement data)
    {
        var delta = data.GetProperty("delta");

        if (delta.TryGetProperty("stop_reason", out var stopReason))
        {
            return new DoneEvent
            {
                Reason = stopReason.GetString() switch
                {
                    "end_turn" => DoneReason.Complete,
                    "tool_use" => DoneReason.ToolCalls,
                    "max_tokens" => DoneReason.MaxTokens,
                    _ => DoneReason.Complete
                }
            };
        }

        // 解析 usage
        if (data.TryGetProperty("usage", out var usage))
        {
            return new UsageEvent { Usage = ParseUsage(usage) };
        }

        return new DoneEvent { Reason = DoneReason.Complete };
    }

    private static ErrorEvent ParseError(JsonElement data)
    {
        var message = data.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
        return new ErrorEvent
        {
            Error = new Exception(message),
            Recoverable = false
        };
    }

    private static ToolCallBlock ParseToolUseBlock(JsonElement block)
    {
        var id = block.GetProperty("id").GetString() ?? "";
        var name = block.GetProperty("name").GetString() ?? "";
        var input = block.TryGetProperty("input", out var i) ? i : JsonSerializer.SerializeToElement(new { });

        return new ToolCallBlock
        {
            Id = id,
            Name = name,
            Arguments = input
        };
    }

    private static TokenUsage ParseUsage(JsonElement usage)
    {
        var inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
        var outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
        var cacheRead = 0;
        var cacheWrite = 0;

        if (usage.TryGetProperty("cache_read_input_tokens", out var cr))
        {
            cacheRead = cr.GetInt32();
        }
        if (usage.TryGetProperty("cache_creation_input_tokens", out var cw))
        {
            cacheWrite = cw.GetInt32();
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheRead,
            CacheWriteTokens = cacheWrite
        };
    }
}
