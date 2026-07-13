using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Text;
using System.Text.Json;

namespace InsightaAI.LLM.OpenAI;

/// <summary>
/// OpenAI Responses API 适配器
/// 使用 /v1/responses 端点，支持流式和非流式模式
/// </summary>
public class OpenAIResponseAdapter : IProviderAdapter
{
    public string Name => "openai-response";
    public bool SupportsReasoning => true;
    public ReasoningMode SupportedReasoningModes => ReasoningMode.ReasoningEffort;

    // 流式解析状态：跟踪进行中的工具调用
    // key = call_id
    private readonly Dictionary<string, (int OutputIndex, string Name, StringBuilder Args)> _pendingToolCalls = new();

    public HttpRequestMessage CreateRequest(LlmRequest request, ProviderConfig config, bool stream)
    {
        var baseUrl = config.BaseUrl ?? "https://api.openai.com/v1";
        var endpoint = $"{baseUrl.TrimEnd('/')}/responses";

        var body = BuildRequestBody(request, stream);
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

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
        switch (eventType)
        {
            case "response.created":
                return HandleResponseCreated(data);

            case "response.in_progress":
                return null;

            case "response.output_item.added":
                return HandleOutputItemAdded(data);

            case "response.content_part.added":
                return HandleContentPartAdded(data);

            case "response.output_text.delta":
                return HandleOutputTextDelta(data);

            case "response.output_text.done":
                return HandleOutputTextDone(data);

            case "response.reasoning_text.delta":
                return HandleReasoningTextDelta(data);

            case "response.reasoning_text.done":
                return HandleReasoningTextDone(data);

            case "response.function_call_arguments.delta":
                return HandleFunctionCallArgumentsDelta(data);

            case "response.function_call_arguments.done":
                return HandleFunctionCallArgumentsDone(data);

            case "response.output_item.done":
                return HandleOutputItemDone(data);

            case "response.completed":
                return HandleResponseCompleted(data);

            default:
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

        if (response.TryGetProperty("status", out var statusElement))
        {
            finishReason = statusElement.GetString() switch
            {
                "completed" => DoneReason.Complete,
                "incomplete" => DoneReason.MaxTokens,
                _ => DoneReason.Complete
            };
        }

        if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

                switch (type)
                {
                    case "message":
                        ParseMessageOutput(item, content);
                        break;

                    case "function_call":
                        ParseFunctionCallOutput(item, content);
                        break;

                    case "reasoning":
                        ParseReasoningOutput(item, content);
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

    // ── 请求构建 ──

    private ResponsesRequest BuildRequestBody(LlmRequest request, bool stream)
    {
        var body = new ResponsesRequest
        {
            Model = request.Model,
            Input = ConvertMessages(request.Messages),
            Stream = stream,
            MaxOutputTokens = request.MaxTokens,
            Temperature = request.Temperature,
            Stop = request.StopSequences
        };

        // 工具
        if (request.Tools is { Length: > 0 })
        {
            body.Tools = request.Tools.Select(t => new ResponseTool
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Schema
            }).ToArray();

            body.ToolChoice = request.ToolChoice switch
            {
                ToolChoiceMode.None => "none",
                ToolChoiceMode.Auto => "auto"
            };
        }

        // 推理
        if (request.Reasoning?.Enabled == true)
        {
            body.Reasoning = new ResponseReasoning
            {
                Effort = request.Reasoning.Effort?.ToString().ToLowerInvariant() ?? "medium"
            };
        }

        return body;
    }

    /// <summary>
    /// 将统一 Message[] 转换为 Responses API input item 数组
    /// </summary>
    private static object[] ConvertMessages(Message[] messages)
    {
        var items = new List<object>();

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case MessageRole.System:
                    items.Add(new ResponseMessageItem
                    {
                        Type = "message",
                        Role = "developer",
                        Content = [new ResponseInputText { Text = msg.GetTextContent() }]
                    });
                    break;

                case MessageRole.User:
                    items.Add(new ResponseMessageItem
                    {
                        Type = "message",
                        Role = "user",
                        Content = [new ResponseInputText { Text = msg.GetTextContent() }]
                    });
                    break;

                case MessageRole.Assistant:
                    // 文本内容
                    var text = msg.GetTextContent();
                    if (!string.IsNullOrEmpty(text))
                    {
                        items.Add(new ResponseMessageItem
                        {
                            Type = "message",
                            Role = "assistant",
                            Content = [new ResponseOutputText { Text = text }]
                        });
                    }

                    // 工具调用（每个单独一个 function_call item）
                    foreach (var tc in msg.GetToolCalls())
                    {
                        items.Add(new ResponseFunctionCallItem
                        {
                            Id = tc.Id,
                            CallId = tc.Id,
                            Name = tc.Name,
                            Arguments = tc.Arguments.GetRawText()
                        });
                    }
                    break;

                case MessageRole.ToolResult:
                    items.Add(new ResponseFunctionCallOutputItem
                    {
                        CallId = msg.ToolCallId ?? "",
                        Output = msg.GetTextContent()
                    });
                    break;
            }
        }

        return items.ToArray();
    }

    // ── 流式事件处理 ──

    private StreamEvent HandleResponseCreated(JsonElement data)
    {
        string model = "";
        if (data.TryGetProperty("response", out var response) &&
            response.TryGetProperty("model", out var modelElement))
        {
            model = modelElement.GetString() ?? "";
        }

        // 清空上一次流的工具调用状态
        _pendingToolCalls.Clear();

        return new StreamStartEvent { Model = model, Provider = Name };
    }

    private StreamEvent? HandleOutputItemAdded(JsonElement data)
    {
        if (!data.TryGetProperty("item", out var item))
            return null;

        var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

        if (type == "function_call")
        {
            var callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var outputIndex = data.TryGetProperty("output_index", out var oi) ? oi.GetInt32() : 0;

            _pendingToolCalls[callId] = (outputIndex, name, new StringBuilder());

            return new ToolCallStartEvent
            {
                ContentIndex = outputIndex,
                ToolName = name,
                ToolCallId = callId
            };
        }

        // reasoning 和 message 类型不需要特殊处理
        return null;
    }

    private StreamEvent? HandleContentPartAdded(JsonElement data)
    {
        if (data.TryGetProperty("part", out var part))
        {
            var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;
            var contentIndex = data.TryGetProperty("content_index", out var ci) ? ci.GetInt32() : 0;

            if (partType == "output_text")
            {
                return new TextStartEvent { ContentIndex = contentIndex };
            }

            if (partType == "reasoning_text")
            {
                return new ThinkingStartEvent { ContentIndex = contentIndex };
            }
        }

        return null;
    }

    private StreamEvent? HandleOutputTextDelta(JsonElement data)
    {
        if (data.TryGetProperty("delta", out var delta) &&
            delta.ValueKind == JsonValueKind.String)
        {
            var text = delta.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                var contentIndex = data.TryGetProperty("content_index", out var ci) ? ci.GetInt32() : 0;
                return new TextDeltaEvent { Delta = text, ContentIndex = contentIndex };
            }
        }

        return null;
    }

    private StreamEvent? HandleOutputTextDone(JsonElement data)
    {
        var contentIndex = data.TryGetProperty("content_index", out var ci) ? ci.GetInt32() : 0;
        return new TextEndEvent { ContentIndex = contentIndex };
    }

    private StreamEvent? HandleReasoningTextDelta(JsonElement data)
    {
        if (data.TryGetProperty("delta", out var delta) &&
            delta.ValueKind == JsonValueKind.String)
        {
            var text = delta.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                var contentIndex = data.TryGetProperty("content_index", out var ci) ? ci.GetInt32() : 0;
                return new ThinkingDeltaEvent { Delta = text, ContentIndex = contentIndex };
            }
        }

        return null;
    }

    private StreamEvent? HandleReasoningTextDone(JsonElement data)
    {
        var contentIndex = data.TryGetProperty("content_index", out var ci) ? ci.GetInt32() : 0;
        return new ThinkingEndEvent { ContentIndex = contentIndex };
    }

    private StreamEvent? HandleFunctionCallArgumentsDelta(JsonElement data)
    {
        if (!data.TryGetProperty("delta", out var delta) ||
            delta.ValueKind != JsonValueKind.String)
            return null;

        var callId = data.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
        var arguments = delta.GetString() ?? "";

        if (_pendingToolCalls.TryGetValue(callId, out var pending))
        {
            pending.Args.Append(arguments);
            _pendingToolCalls[callId] = pending;

            return new ToolCallDeltaEvent
            {
                ContentIndex = pending.OutputIndex,
                ArgumentsDelta = arguments
            };
        }

        return null;
    }

    private StreamEvent? HandleFunctionCallArgumentsDone(JsonElement data)
    {
        // 参数累积完成，等待 output_item.done 发射 ToolCallEndEvent
        return null;
    }

    private StreamEvent? HandleOutputItemDone(JsonElement data)
    {
        if (!data.TryGetProperty("item", out var item))
            return null;

        var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

        if (type == "function_call")
        {
            var callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            // 优先使用 call_id 作为工具调用 ID，与 HandleOutputItemAdded 保持一致
            // item.id 是 output item 的 ID（如 "fc_xxx"），不是工具调用的 ID
            var id = !string.IsNullOrEmpty(callId) ? callId : (item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "");

            // 优先使用通过 delta 累积的参数，fallback 到事件中的 arguments
            string argumentsStr;
            if (_pendingToolCalls.TryGetValue(callId, out var pending) && pending.Args.Length > 0)
            {
                argumentsStr = pending.Args.ToString();
            }
            else
            {
                argumentsStr = item.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}";
            }

            // 从 pending 中移除
            _pendingToolCalls.Remove(callId);

            JsonElement arguments;
            try
            {
                arguments = JsonSerializer.Deserialize<JsonElement>(argumentsStr);
            }
            catch
            {
                arguments = JsonSerializer.SerializeToElement(new { });
            }

            return new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = id,
                    Name = name,
                    Arguments = arguments
                }
            };
        }

        if (type == "message")
        {
            // 检查是否有 refusal 内容需要处理
            if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in content.EnumerateArray())
                {
                    var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;
                    if (partType == "refusal")
                    {
                        // refusal 可以后续扩展处理
                    }
                }
            }
        }

        // reasoning 类型不需要特殊处理
        return null;
    }

    private StreamEvent? HandleResponseCompleted(JsonElement data)
    {
        TokenUsage? usage = null;
        var finishReason = DoneReason.Complete;

        if (data.TryGetProperty("response", out var response))
        {
            if (response.TryGetProperty("usage", out var usageElement))
            {
                usage = ParseUsage(usageElement);
            }

            if (response.TryGetProperty("status", out var statusElement))
            {
                finishReason = statusElement.GetString() switch
                {
                    "completed" => DoneReason.Complete,
                    "incomplete" => DoneReason.MaxTokens,
                    _ => DoneReason.Complete
                };
            }
        }

        // 清空 pending 状态
        _pendingToolCalls.Clear();

        return new DoneEvent
        {
            Reason = finishReason,
            Usage = usage
        };
    }

    // ── 非流式响应解析辅助 ──

    private static void ParseMessageOutput(JsonElement item, List<ContentBlock> content)
    {
        if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return;

        foreach (var part in parts.EnumerateArray())
        {
            var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;

            if (partType == "output_text" &&
                part.TryGetProperty("text", out var textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                var text = textElement.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    content.Add(new TextBlock { Text = text });
                }
            }
        }
    }

    private static void ParseFunctionCallOutput(JsonElement item, List<ContentBlock> content)
    {
        var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var argumentsStr = item.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}";

        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(argumentsStr);
        }
        catch
        {
            arguments = JsonSerializer.SerializeToElement(new { });
        }

        content.Add(new ToolCallBlock
        {
            Id = id,
            Name = name,
            Arguments = arguments
        });
    }

    private static void ParseReasoningOutput(JsonElement item, List<ContentBlock> content)
    {
        if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return;

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;
            if (partType == "reasoning_text" &&
                part.TryGetProperty("text", out var textElement) &&
                textElement.ValueKind == JsonValueKind.String)
            {
                sb.Append(textElement.GetString());
            }
        }

        if (sb.Length > 0)
        {
            content.Add(new ThinkingBlock { Thinking = sb.ToString() });
        }
    }

    // ── 工具方法 ──

    private static TokenUsage ParseUsage(JsonElement usage)
    {
        var inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
        var outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;

        var cacheHitTokens = 0;
        if (usage.TryGetProperty("input_tokens_details", out var itd) &&
            itd.TryGetProperty("cached_tokens", out var ct))
        {
            cacheHitTokens = ct.GetInt32();
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheHitTokens = cacheHitTokens
        };
    }
}
