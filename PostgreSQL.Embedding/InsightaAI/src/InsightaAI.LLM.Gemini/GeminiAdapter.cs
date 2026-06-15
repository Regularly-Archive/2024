using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Text;
using System.Text.Json;

namespace InsightaAI.LLM.Gemini;

/// <summary>
/// Google Gemini 适配器
/// 支持 Gemini Pro, Gemini Flash 等模型
/// </summary>
public class GeminiAdapter : IProviderAdapter
{
    public string Name => "gemini";
    public bool SupportsReasoning => false;
    public ReasoningMode SupportedReasoningModes => ReasoningMode.None;

    public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream)
    {
        var baseUrl = config.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta";
        var endpoint = stream
            ? $"{baseUrl.TrimEnd('/')}/models/{request.Model}:streamGenerateContent?alt=sse&key={config.ApiKey}"
            : $"{baseUrl.TrimEnd('/')}/models/{request.Model}:generateContent?key={config.ApiKey}";

        var body = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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
            // Gemini SSE 格式: data.candidates[0].content.parts[0].text
            if (!data.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var candidate = candidates[0];

            // 检查是否完成
            if (candidate.TryGetProperty("finishReason", out var finishReason))
            {
                var reason = finishReason.GetString();
                TokenUsage? usage = null;

                // 提取 usage
                if (data.TryGetProperty("usageMetadata", out var usageMetadata))
                {
                    usage = ParseUsage(usageMetadata);
                }

                return new DoneEvent
                {
                    Reason = reason switch
                    {
                        "STOP" => DoneReason.Complete,
                        "MAX_TOKENS" => DoneReason.MaxTokens,
                        "SAFETY" => DoneReason.Stop,
                        _ => DoneReason.Complete
                    },
                    Usage = usage
                };
            }

            // 解析内容
            if (candidate.TryGetProperty("content", out var content))
            {
                if (content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    var part = parts[0];

                    // 检查是否有文本
                    if (part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        var textValue = text.GetString();
                        if (!string.IsNullOrEmpty(textValue))
                        {
                            return new TextDeltaEvent { Delta = textValue };
                        }
                    }

                    // 检查是否有函数调用
                    if (part.TryGetProperty("functionCall", out var functionCall))
                    {
                        return ParseFunctionCall(functionCall);
                    }
                }
            }

            return null;
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

        // 提取 usage
        if (response.TryGetProperty("usageMetadata", out var usageMetadata))
        {
            usage = ParseUsage(usageMetadata);
        }

        if (response.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("finishReason", out var fr))
            {
                finishReason = fr.GetString() switch
                {
                    "STOP" => DoneReason.Complete,
                    "MAX_TOKENS" => DoneReason.MaxTokens,
                    "SAFETY" => DoneReason.Stop,
                    _ => DoneReason.Complete
                };
            }

            if (candidate.TryGetProperty("content", out var contentObj))
            {
                if (contentObj.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        // 文本内容
                        if (part.TryGetProperty("text", out var text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            var textValue = text.GetString();
                            if (!string.IsNullOrEmpty(textValue))
                            {
                                content.Add(new TextBlock { Text = textValue });
                            }
                        }

                        // 函数调用
                        if (part.TryGetProperty("functionCall", out var functionCall))
                        {
                            content.Add(ParseFunctionCallBlock(functionCall));
                        }
                    }
                }
            }
        }

        return new LlmResponse
        {
            Model = model,
            Content = content.ToArray(),
            FinishReason = finishReason,
            Usage = usage
        };
    }

    private static object BuildRequestBody(LlmRequest request)
    {
        var contents = new List<object>();

        // 提取系统指令
        object? systemInstruction = null;
        var systemMessage = request.Messages.FirstOrDefault(m => m.Role == MessageRole.System);
        if (systemMessage != null)
        {
            var systemText = systemMessage.GetTextContent();
            if (!string.IsNullOrEmpty(systemText))
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemText } }
                };
            }
        }

        // 转换消息
        foreach (var message in request.Messages.Where(m => m.Role != MessageRole.System))
        {
            var role = message.Role switch
            {
                MessageRole.User => "user",
                MessageRole.Assistant => "model",
                MessageRole.ToolResult => "user",
                _ => "user"
            };

            var parts = new List<object>();

            foreach (var block in message.Content)
            {
                switch (block)
                {
                    case TextBlock text:
                        parts.Add(new { text = text.Text });
                        break;

                    case ImageBlock image:
                        parts.Add(new
                        {
                            inlineData = new
                            {
                                mimeType = image.Source.MediaType,
                                data = image.Source.Data
                            }
                        });
                        break;

                    case ToolCallBlock toolCall:
                        parts.Add(new
                        {
                            functionCall = new
                            {
                                name = toolCall.Name,
                                args = toolCall.Arguments
                            }
                        });
                        break;

                    case ToolResultBlock toolResult:
                        parts.Add(new
                        {
                            functionResponse = new
                            {
                                name = toolResult.ToolName,
                                response = new { content = toolResult.Content }
                            }
                        });
                        break;
                }
            }

            if (parts.Count > 0)
            {
                contents.Add(new { role, parts });
            }
        }

        // 构建工具定义
        object? tools = null;
        if (request.Tools != null && request.Tools.Length > 0)
        {
            tools = new[]
            {
                new
                {
                    functionDeclarations = request.Tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description ?? "",
                        parameters = t.Schema
                    })
                }
            };
        }

        var body = new Dictionary<string, object>
        {
            ["contents"] = contents
        };

        if (systemInstruction != null)
        {
            body["systemInstruction"] = systemInstruction;
        }

        if (tools != null)
        {
            body["tools"] = tools;

            // 工具调用策略
            var mode = request.ToolChoice switch
            {
                ToolChoiceMode.None => "NONE",
                ToolChoiceMode.Auto => "AUTO"
            };
            body["tool_config"] = new
            {
                function_calling_config = new { mode }
            };
        }

        // 生成配置
        var generationConfig = new Dictionary<string, object>();
        if (request.MaxTokens.HasValue)
        {
            generationConfig["maxOutputTokens"] = request.MaxTokens.Value;
        }
        if (request.Temperature.HasValue)
        {
            generationConfig["temperature"] = request.Temperature.Value;
        }
        if (generationConfig.Count > 0)
        {
            body["generationConfig"] = generationConfig;
        }

        return body;
    }

    private static StreamEvent ParseFunctionCall(JsonElement functionCall)
    {
        var name = functionCall.GetProperty("name").GetString() ?? "";
        var args = functionCall.TryGetProperty("args", out var a) ? a : JsonSerializer.SerializeToElement(new { });

        return new ToolCallStartEvent
        {
            ToolName = name,
            ToolCallId = $"call_{Guid.NewGuid():N}"
        };
    }

    private static ToolCallBlock ParseFunctionCallBlock(JsonElement functionCall)
    {
        var name = functionCall.GetProperty("name").GetString() ?? "";
        var args = functionCall.TryGetProperty("args", out var a) ? a : JsonSerializer.SerializeToElement(new { });

        return new ToolCallBlock
        {
            Id = $"call_{Guid.NewGuid():N}",
            Name = name,
            Arguments = args
        };
    }

    private static TokenUsage ParseUsage(JsonElement usage)
    {
        var promptTokens = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : 0;
        var completionTokens = usage.TryGetProperty("candidatesTokenCount", out var ct) ? ct.GetInt32() : 0;
        var cacheHit = 0;

        if (usage.TryGetProperty("cachedContentTokenCount", out var cached))
        {
            cacheHit = cached.GetInt32();
        }

        return new TokenUsage
        {
            InputTokens = promptTokens,
            OutputTokens = completionTokens,
            CacheHitTokens = cacheHit
        };
    }
}
