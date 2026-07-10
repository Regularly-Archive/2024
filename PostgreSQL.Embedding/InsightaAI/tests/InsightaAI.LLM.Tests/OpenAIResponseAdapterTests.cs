using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using System.Text.Json;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// OpenAI Responses API 适配器单元测试
/// </summary>
public class OpenAIResponseAdapterTests
{
    private readonly OpenAIResponseAdapter _adapter = new();
    private readonly ProviderConfig _config = new() { ApiKey = "test-key" };

    // ── 基本属性 ──

    [Fact]
    public void Adapter_Name_Should_Be_OpenAI_Response()
    {
        Assert.Equal("openai-response", _adapter.Name);
    }

    [Fact]
    public void Adapter_Should_Support_Reasoning()
    {
        Assert.True(_adapter.SupportsReasoning);
        Assert.True(_adapter.SupportedReasoningModes.HasFlag(ReasoningMode.ReasoningEffort));
    }

    // ── CreateRequest 测试 ──

    [Fact]
    public void CreateRequest_Should_Use_Responses_Endpoint()
    {
        var request = CreateSimpleRequest();

        var httpRequest = _adapter.CreateRequest(request, _config, stream: false);

        Assert.Equal(HttpMethod.Post, httpRequest.Method);
        Assert.Contains("/responses", httpRequest.RequestUri!.ToString());
        Assert.DoesNotContain("/chat/completions", httpRequest.RequestUri!.ToString());
    }

    [Fact]
    public void CreateRequest_Should_Set_Auth_Header()
    {
        var request = CreateSimpleRequest();

        var httpRequest = _adapter.CreateRequest(request, _config, stream: false);

        Assert.True(httpRequest.Headers.Contains("Authorization"));
        Assert.Contains("Bearer test-key", httpRequest.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public void CreateRequest_Should_Set_Custom_BaseUrl()
    {
        var customConfig = new ProviderConfig
        {
            ApiKey = "key",
            BaseUrl = "https://custom.api.com/v1"
        };
        var request = CreateSimpleRequest();

        var httpRequest = _adapter.CreateRequest(request, customConfig, stream: false);

        Assert.Contains("https://custom.api.com/v1/responses", httpRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateRequest_Should_Build_Correct_Body_For_Simple_Message()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [Message.FromUser("Hello")],
            MaxTokens = 100,
            Temperature = 0.7
        };

        var body = await GetRequestBodyAsync(request, stream: false);

        Assert.Equal("gpt-4o", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.Equal(100, body.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(0.7, body.GetProperty("temperature").GetDouble());

        // input 应该是数组
        var input = body.GetProperty("input");
        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.Equal(1, input.GetArrayLength());

        var item = input[0];
        Assert.Equal("message", item.GetProperty("type").GetString());
        Assert.Equal("user", item.GetProperty("role").GetString());
    }

    [Fact]
    public async Task CreateRequest_Should_Convert_System_Message_As_Developer()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [Message.FromSystem("You are helpful."), Message.FromUser("Hi")]
        };

        var body = await GetRequestBodyAsync(request, stream: false);
        var input = body.GetProperty("input");

        Assert.Equal(2, input.GetArrayLength());

        var systemItem = input[0];
        Assert.Equal("message", systemItem.GetProperty("type").GetString());
        Assert.Equal("developer", systemItem.GetProperty("role").GetString());

        var content = systemItem.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("input_text", content[0].GetProperty("type").GetString());
        Assert.Equal("You are helpful.", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task CreateRequest_Should_Convert_Assistant_With_ToolCalls()
    {
        var toolCallArgs = JsonSerializer.Deserialize<JsonElement>(@"{""location"":""Beijing""}");
        var messages = new Message[]
        {
            Message.FromUser("Weather?"),
            new Message
            {
                Role = MessageRole.Assistant,
                Content =
                [
                    new TextBlock { Text = "Let me check." },
                    new ToolCallBlock { Id = "call_123", Name = "get_weather", Arguments = toolCallArgs }
                ]
            },
            Message.FromToolResult("call_123", "get_weather",
                [new TextBlock { Text = "Sunny, 25°C" }])
        };
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = messages
        };

        var body = await GetRequestBodyAsync(request, stream: false);
        var input = body.GetProperty("input");

        // user + assistant message + function_call + function_call_output = 4 items
        Assert.Equal(4, input.GetArrayLength());

        // assistant message (text only)
        var assistantMsg = input[1];
        Assert.Equal("message", assistantMsg.GetProperty("type").GetString());
        Assert.Equal("assistant", assistantMsg.GetProperty("role").GetString());
        Assert.Equal("output_text", assistantMsg.GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("Let me check.", assistantMsg.GetProperty("content")[0].GetProperty("text").GetString());

        // function_call item
        var funcCall = input[2];
        Assert.Equal("function_call", funcCall.GetProperty("type").GetString());
        Assert.Equal("call_123", funcCall.GetProperty("call_id").GetString());
        Assert.Equal("get_weather", funcCall.GetProperty("name").GetString());
        var funcArgs = JsonSerializer.Deserialize<JsonElement>(funcCall.GetProperty("arguments").GetString()!);
        Assert.Equal("Beijing", funcArgs.GetProperty("location").GetString());

        // function_call_output item
        var funcOutput = input[3];
        Assert.Equal("function_call_output", funcOutput.GetProperty("type").GetString());
        Assert.Equal("call_123", funcOutput.GetProperty("call_id").GetString());
        Assert.Equal("Sunny, 25°C", funcOutput.GetProperty("output").GetString());
    }

    [Fact]
    public async Task CreateRequest_Should_Convert_Tools()
    {
        var request = new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [Message.FromUser("Hi")],
            Tools =
            [
                new ToolDefinition
                {
                    Name = "get_weather",
                    Description = "Get weather",
                    Schema = JsonSerializer.Deserialize<JsonElement>(@"{
                        ""type"": ""object"",
                        ""properties"": { ""location"": { ""type"": ""string"" } },
                        ""required"": [""location""]
                    }")
                }
            ],
            ToolChoice = ToolChoiceMode.Auto
        };

        var body = await GetRequestBodyAsync(request, stream: false);
        var tools = body.GetProperty("tools");

        Assert.Equal(1, tools.GetArrayLength());

        var tool = tools[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("get_weather", tool.GetProperty("name").GetString());
        Assert.Equal("Get weather", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
        Assert.Equal("auto", body.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task CreateRequest_Should_Set_Reasoning_Config()
    {
        var request = new LlmRequest
        {
            Model = "o3-mini",
            Messages = [Message.FromUser("Think step by step.")],
            Reasoning = new ReasoningConfig { Enabled = true, Effort = ReasoningEffort.High }
        };

        var body = await GetRequestBodyAsync(request, stream: false);

        Assert.True(body.TryGetProperty("reasoning", out var reasoning));
        Assert.Equal("high", reasoning.GetProperty("effort").GetString());
    }

    [Fact]
    public async Task CreateRequest_Should_Set_Stream_Flag()
    {
        var request = CreateSimpleRequest();

        var body = await GetRequestBodyAsync(request, stream: true);

        Assert.True(body.GetProperty("stream").GetBoolean());
    }

    // ── ParseStreamEvent 测试 ──

    [Fact]
    public void ParseStreamEvent_ResponseCreated_Should_Return_StreamStartEvent()
    {
        var (eventType, data) = LoadStreamEvent("response_created.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var startEvent = Assert.IsType<StreamStartEvent>(result);
        Assert.Equal("gpt-4o", startEvent.Model);
        Assert.Equal("openai-response", startEvent.Provider);
    }

    [Fact]
    public void ParseStreamEvent_ResponseInProgress_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("response_in_progress.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamEvent_OutputTextDelta_Should_Return_TextDeltaEvent()
    {
        var (eventType, data) = LoadStreamEvent("response_output_text_delta.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var textDelta = Assert.IsType<TextDeltaEvent>(result);
        Assert.Equal("Hello", textDelta.Delta);
        Assert.Equal(0, textDelta.ContentIndex);
    }

    [Fact]
    public void ParseStreamEvent_OutputTextDone_Should_Return_TextEndEvent()
    {
        var (eventType, data) = LoadStreamEvent("response_output_text_done.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var textEnd = Assert.IsType<TextEndEvent>(result);
        Assert.Equal(0, textEnd.ContentIndex);
    }

    [Fact]
    public void ParseStreamEvent_OutputItemAdded_FunctionCall_Should_Return_ToolCallStartEvent()
    {
        var (eventType, data) = LoadStreamEvent("output_item_added_function_call.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var toolStart = Assert.IsType<ToolCallStartEvent>(result);
        Assert.Equal("get_weather", toolStart.ToolName);
        Assert.Equal("call_abc", toolStart.ToolCallId);
        Assert.Equal(0, toolStart.ContentIndex);
    }

    [Fact]
    public void ParseStreamEvent_OutputItemAdded_Message_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("output_item_added_message.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamEvent_FunctionCallArgumentsDelta_Should_Return_ToolCallDeltaEvent()
    {
        // 先触发一个 output_item.added 来注册 pending tool call
        var (addType, addData) = LoadStreamEvent("output_item_added_function_call.json");
        _adapter.ParseStreamEvent(addType, addData);

        // 然后发送 arguments delta
        var (eventType, data) = LoadStreamEvent("function_call_arguments_delta.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var toolDelta = Assert.IsType<ToolCallDeltaEvent>(result);
        Assert.Equal("{\"location\":\"Beijing\"", toolDelta.ArgumentsDelta);
        Assert.Equal(0, toolDelta.ContentIndex);
    }

    [Fact]
    public void ParseStreamEvent_FunctionCallArgumentsDone_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("function_call_arguments_done.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamEvent_OutputItemDone_FunctionCall_Should_Return_ToolCallEndEvent()
    {
        // 先注册 pending tool call
        var (addType, addData) = LoadStreamEvent("output_item_added_function_call.json");
        _adapter.ParseStreamEvent(addType, addData);

        // 发送 arguments delta
        var (deltaType, deltaData) = LoadStreamEvent("function_call_arguments_delta.json");
        _adapter.ParseStreamEvent(deltaType, deltaData);

        // 完成 item
        var (eventType, data) = LoadStreamEvent("output_item_done_function_call.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var toolEnd = Assert.IsType<ToolCallEndEvent>(result);
        Assert.Equal("item_fc_1", toolEnd.ToolCall.Id);
        Assert.Equal("get_weather", toolEnd.ToolCall.Name);
        Assert.Equal("Beijing", toolEnd.ToolCall.Arguments.GetProperty("location").GetString());
    }

    [Fact]
    public void ParseStreamEvent_OutputItemDone_Message_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("output_item_done_message.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamEvent_ResponseCompleted_Should_Return_DoneEvent_With_Usage()
    {
        var (eventType, data) = LoadStreamEvent("response_completed.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var doneEvent = Assert.IsType<DoneEvent>(result);
        Assert.Equal(DoneReason.Complete, doneEvent.Reason);
        Assert.NotNull(doneEvent.Usage);
        Assert.Equal(150, doneEvent.Usage.InputTokens);
        Assert.Equal(50, doneEvent.Usage.OutputTokens);
    }

    [Fact]
    public void ParseStreamEvent_ResponseCompleted_Incomplete_Should_Return_MaxTokens()
    {
        var (eventType, data) = LoadStreamEvent("response_completed_incomplete.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var doneEvent = Assert.IsType<DoneEvent>(result);
        Assert.Equal(DoneReason.MaxTokens, doneEvent.Reason);
    }

    [Fact]
    public void ParseStreamEvent_Unknown_Event_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("unknown_event.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    // ── Reasoning 类型处理 ──

    [Fact]
    public void ParseStreamEvent_OutputItemAdded_Reasoning_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("output_item_added_reasoning.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamEvent_ContentPartAdded_ReasoningText_Should_Return_ThinkingStartEvent()
    {
        var (eventType, data) = LoadStreamEvent("content_part_added_reasoning_text.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        var thinkingStart = Assert.IsType<ThinkingStartEvent>(result);
        Assert.Equal(0, thinkingStart.ContentIndex);
    }

    [Fact]
    public void ParseStreamEvent_OutputItemDone_Reasoning_Should_Return_Null()
    {
        var (eventType, data) = LoadStreamEvent("output_item_done_reasoning.json");

        var result = _adapter.ParseStreamEvent(eventType, data);

        Assert.Null(result);
    }

    // ── 完整流式工具调用流程 ──

    [Fact]
    public void ParseStreamEvent_Full_ToolCall_Flow_Should_Work()
    {
        // 1. response.created
        var (t1, d1) = LoadStreamEvent("full_tool_flow/01_response_created.json");
        var start = _adapter.ParseStreamEvent(t1, d1);
        Assert.IsType<StreamStartEvent>(start);

        // 2. output_item.added (function_call)
        var (t2, d2) = LoadStreamEvent("full_tool_flow/02_output_item_added.json");
        var toolStart = _adapter.ParseStreamEvent(t2, d2);
        Assert.IsType<ToolCallStartEvent>(toolStart);

        // 3. function_call_arguments.delta (multiple chunks)
        var (t3, d3) = LoadStreamEvent("full_tool_flow/03_args_delta_1.json");
        var td1 = _adapter.ParseStreamEvent(t3, d3);
        Assert.IsType<ToolCallDeltaEvent>(td1);

        var (t4, d4) = LoadStreamEvent("full_tool_flow/04_args_delta_2.json");
        var td2 = _adapter.ParseStreamEvent(t4, d4);
        Assert.IsType<ToolCallDeltaEvent>(td2);

        // 4. function_call_arguments.done
        var (t5, d5) = LoadStreamEvent("full_tool_flow/05_args_done.json");
        var null1 = _adapter.ParseStreamEvent(t5, d5);
        Assert.Null(null1);

        // 5. output_item.done (function_call) — should emit ToolCallEndEvent with complete args
        var (t6, d6) = LoadStreamEvent("full_tool_flow/06_output_item_done.json");
        var toolEnd = _adapter.ParseStreamEvent(t6, d6);
        var endEvent = Assert.IsType<ToolCallEndEvent>(toolEnd);
        Assert.Equal("get_weather", endEvent.ToolCall.Name);
        Assert.Equal("Shanghai", endEvent.ToolCall.Arguments.GetProperty("location").GetString());

        // 6. response.completed
        var (t7, d7) = LoadStreamEvent("full_tool_flow/07_response_completed.json");
        var done = _adapter.ParseStreamEvent(t7, d7);
        var doneEvent = Assert.IsType<DoneEvent>(done);
        Assert.Equal(DoneReason.Complete, doneEvent.Reason);
        Assert.Equal(50, doneEvent.Usage!.InputTokens);
        Assert.Equal(20, doneEvent.Usage.OutputTokens);
    }

    // ── ParseResponse 测试（非流式） ──

    [Fact]
    public void ParseResponse_Should_Parse_Text_Message()
    {
        var response = LoadResponse("simple_text.json");

        var result = _adapter.ParseResponse(response);

        Assert.Equal("gpt-4o", result.Model);
        Assert.Equal(DoneReason.Complete, result.FinishReason);
        Assert.Single(result.Content);

        var textBlock = Assert.IsType<TextBlock>(result.Content[0]);
        Assert.Equal("Hello, World!", textBlock.Text);

        Assert.NotNull(result.Usage);
        Assert.Equal(10, result.Usage.InputTokens);
        Assert.Equal(5, result.Usage.OutputTokens);
    }

    [Fact]
    public void ParseResponse_Should_Parse_Function_Call()
    {
        var response = LoadResponse("function_call.json");

        var result = _adapter.ParseResponse(response);

        Assert.Single(result.Content);
        var toolCall = Assert.IsType<ToolCallBlock>(result.Content[0]);
        Assert.Equal("fc_1", toolCall.Id);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("Tokyo", toolCall.Arguments.GetProperty("location").GetString());
    }

    [Fact]
    public void ParseResponse_Should_Parse_Mixed_Text_And_ToolCall()
    {
        var response = LoadResponse("mixed_text_and_tool_call.json");

        var result = _adapter.ParseResponse(response);

        Assert.Equal(2, result.Content.Length);

        var textBlock = Assert.IsType<TextBlock>(result.Content[0]);
        Assert.Equal("Let me check the weather.", textBlock.Text);

        var toolCall = Assert.IsType<ToolCallBlock>(result.Content[1]);
        Assert.Equal("get_weather", toolCall.Name);
        Assert.Equal("Paris", toolCall.Arguments.GetProperty("location").GetString());
    }

    [Fact]
    public void ParseResponse_Should_Handle_Incomplete_Status()
    {
        var response = LoadResponse("incomplete_status.json");

        var result = _adapter.ParseResponse(response);

        Assert.Equal(DoneReason.MaxTokens, result.FinishReason);
    }

    [Fact]
    public void ParseResponse_Should_Handle_Empty_Output()
    {
        var response = LoadResponse("empty_output.json");

        var result = _adapter.ParseResponse(response);

        Assert.Empty(result.Content);
        Assert.Equal(DoneReason.Complete, result.FinishReason);
    }

    [Fact]
    public void ParseResponse_Should_Parse_Reasoning_And_Message()
    {
        var response = LoadResponse("with_reasoning.json");

        var result = _adapter.ParseResponse(response);

        Assert.Equal("mimo-v2.5-pro", result.Model);
        Assert.Equal(2, result.Content.Length);

        // reasoning item → ThinkingBlock
        var thinkingBlock = Assert.IsType<ThinkingBlock>(result.Content[0]);
        Assert.Contains("introduce myself", thinkingBlock.Thinking);

        // message item → TextBlock
        var textBlock = Assert.IsType<TextBlock>(result.Content[1]);
        Assert.Contains("MiMo", textBlock.Text);

        Assert.NotNull(result.Usage);
        Assert.Equal(55, result.Usage.InputTokens);
        Assert.Equal(177, result.Usage.OutputTokens);
        Assert.Equal(30, result.Usage.CacheHitTokens);
    }

    // ── 多工具调用流式场景 ──

    [Fact]
    public void ParseStreamEvent_Multiple_ToolCalls_Should_Track_Independently()
    {
        // 注册两个 tool call
        var (t1, d1) = LoadStreamEvent("multi_tool_calls/01_add_weather.json");
        _adapter.ParseStreamEvent(t1, d1);

        var (t2, d2) = LoadStreamEvent("multi_tool_calls/02_add_time.json");
        _adapter.ParseStreamEvent(t2, d2);

        // 第一个 tool call 的 arguments delta
        var (t3, d3) = LoadStreamEvent("multi_tool_calls/03_delta_weather.json");
        var td1 = _adapter.ParseStreamEvent(t3, d3);
        var toolDelta1 = Assert.IsType<ToolCallDeltaEvent>(td1);
        Assert.Equal(0, toolDelta1.ContentIndex);

        // 第二个 tool call 的 arguments delta
        var (t4, d4) = LoadStreamEvent("multi_tool_calls/04_delta_time.json");
        var td2 = _adapter.ParseStreamEvent(t4, d4);
        var toolDelta2 = Assert.IsType<ToolCallDeltaEvent>(td2);
        Assert.Equal(1, toolDelta2.ContentIndex);

        // 完成第一个
        var (t5, d5) = LoadStreamEvent("multi_tool_calls/05_done_weather.json");
        var end1 = _adapter.ParseStreamEvent(t5, d5);
        var endEvent1 = Assert.IsType<ToolCallEndEvent>(end1);
        Assert.Equal("get_weather", endEvent1.ToolCall.Name);

        // 完成第二个
        var (t6, d6) = LoadStreamEvent("multi_tool_calls/06_done_time.json");
        var end2 = _adapter.ParseStreamEvent(t6, d6);
        var endEvent2 = Assert.IsType<ToolCallEndEvent>(end2);
        Assert.Equal("get_time", endEvent2.ToolCall.Name);
    }

    // ── LlmStream 端到端测试 ──

    [Fact]
    public async Task LlmStream_Should_Not_Duplicate_ToolCall_For_Response_API()
    {
        // 模拟 Response API 的完整事件流（含 DefaultLlmClient 的额外 StreamStartEvent）
        var events = new StreamEvent[]
        {
            // DefaultLlmClient 添加的 StreamStartEvent
            new StreamStartEvent { Model = "mimo-v2.5-pro", Provider = "openai-response" },
            // adapter 处理 response.created
            new StreamStartEvent { Model = "mimo-v2.5-pro", Provider = "openai-response" },
            // adapter 处理 response.output_item.added (function_call)
            new ToolCallStartEvent { ContentIndex = 0, ToolName = "whereami", ToolCallId = "call_abc" },
            // adapter 处理 response.function_call_arguments.delta
            new ToolCallDeltaEvent { ContentIndex = 0, ArgumentsDelta = "{}" },
            // adapter 处理 response.output_item.done (function_call)
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_abc",
                    Name = "whereami",
                    Arguments = JsonSerializer.SerializeToElement(new { })
                }
            },
            // adapter 处理 response.completed
            new DoneEvent { Reason = DoneReason.Complete, Usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 } }
        };

        var stream = new LlmStreamImpl(ToAsyncEnumerable(events));

        // 消费流
        var collectedEvents = new List<StreamEvent>();
        await foreach (var evt in stream)
        {
            collectedEvents.Add(evt);
        }

        // 获取最终响应
        var response = await stream.GetResponseAsync();

        // 验证：应该只有 1 个 ToolCallBlock
        var toolCalls = response.GetToolCalls();
        Assert.Single(toolCalls);
        Assert.Equal("whereami", toolCalls[0].Name);
        Assert.Equal("call_abc", toolCalls[0].Id);

        // 验证：Content 中应该只有 1 个 ToolCallBlock
        var toolCallBlocks = response.Content.OfType<ToolCallBlock>().ToArray();
        Assert.Single(toolCallBlocks);
    }

    [Fact]
    public async Task LlmStream_Should_Not_Duplicate_ToolCall_Via_Cache_Replay()
    {
        // 测试缓存回放时不会重复工具调用
        var events = new StreamEvent[]
        {
            new StreamStartEvent { Model = "gpt-4o", Provider = "openai-response" },
            new ToolCallStartEvent { ContentIndex = 0, ToolName = "get_weather", ToolCallId = "call_1" },
            new ToolCallDeltaEvent { ContentIndex = 0, ArgumentsDelta = "{\"location\":\"Beijing\"}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_1",
                    Name = "get_weather",
                    Arguments = JsonSerializer.Deserialize<JsonElement>("{\"location\":\"Beijing\"}")
                }
            },
            new DoneEvent { Reason = DoneReason.Complete }
        };

        var stream = new LlmStreamImpl(ToAsyncEnumerable(events));

        // 第一次消费
        await foreach (var _ in stream) { }

        // 第二次消费（从缓存）
        await foreach (var _ in stream) { }

        // GetResponseAsync 使用缓存
        var response = await stream.GetResponseAsync();

        var toolCalls = response.GetToolCalls();
        Assert.Single(toolCalls);
        Assert.Equal("get_weather", toolCalls[0].Name);
    }

    [Fact]
    public async Task LlmStream_Should_Handle_Multiple_ToolCalls_Without_Duplication()
    {
        // 模拟两个不同的工具调用（不应该被去重，因为它们是不同的调用）
        var events = new StreamEvent[]
        {
            new StreamStartEvent { Model = "gpt-4o", Provider = "openai-response" },
            // 第一个工具调用
            new ToolCallStartEvent { ContentIndex = 0, ToolName = "get_weather", ToolCallId = "call_a" },
            new ToolCallDeltaEvent { ContentIndex = 0, ArgumentsDelta = "{\"location\":\"Beijing\"}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_a",
                    Name = "get_weather",
                    Arguments = JsonSerializer.Deserialize<JsonElement>("{\"location\":\"Beijing\"}")
                }
            },
            // 第二个工具调用
            new ToolCallStartEvent { ContentIndex = 1, ToolName = "get_time", ToolCallId = "call_b" },
            new ToolCallDeltaEvent { ContentIndex = 1, ArgumentsDelta = "{\"tz\":\"UTC\"}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_b",
                    Name = "get_time",
                    Arguments = JsonSerializer.Deserialize<JsonElement>("{\"tz\":\"UTC\"}")
                }
            },
            new DoneEvent { Reason = DoneReason.Complete }
        };

        var stream = new LlmStreamImpl(ToAsyncEnumerable(events));

        await foreach (var _ in stream) { }
        var response = await stream.GetResponseAsync();

        var toolCalls = response.GetToolCalls();
        Assert.Equal(2, toolCalls.Length);
        Assert.Equal("get_weather", toolCalls[0].Name);
        Assert.Equal("get_time", toolCalls[1].Name);
    }

    [Fact]
    public async Task LlmStream_Should_Deduplicate_ToolCalls_With_Same_Id()
    {
        // 模拟 LLM 生成了两个相同 ID 的工具调用（bug 场景）
        var events = new StreamEvent[]
        {
            new StreamStartEvent { Model = "mimo-v2.5-pro", Provider = "openai-response" },
            // 第一个 whereami 工具调用
            new ToolCallStartEvent { ContentIndex = 0, ToolName = "whereami", ToolCallId = "call_dup" },
            new ToolCallDeltaEvent { ContentIndex = 0, ArgumentsDelta = "{}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_dup",
                    Name = "whereami",
                    Arguments = JsonSerializer.SerializeToElement(new { })
                }
            },
            // 第二个相同 ID 的工具调用（LLM 重复生成）
            new ToolCallStartEvent { ContentIndex = 1, ToolName = "whereami", ToolCallId = "call_dup" },
            new ToolCallDeltaEvent { ContentIndex = 1, ArgumentsDelta = "{}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_dup",
                    Name = "whereami",
                    Arguments = JsonSerializer.SerializeToElement(new { })
                }
            },
            new DoneEvent { Reason = DoneReason.Complete }
        };

        var stream = new LlmStreamImpl(ToAsyncEnumerable(events));

        await foreach (var _ in stream) { }
        var response = await stream.GetResponseAsync();

        // 应该只有一个工具调用（被去重了）
        var toolCalls = response.GetToolCalls();
        Assert.Single(toolCalls);
        Assert.Equal("whereami", toolCalls[0].Name);
        Assert.Equal("call_dup", toolCalls[0].Id);
    }

    [Fact]
    public async Task LlmStream_Should_Deduplicate_Via_FinalizePending()
    {
        // 模拟场景：ToolCallEndEvent 处理了一个，FinalizePendingToolCalls 处理了另一个同 ID 的
        // 这发生在 LLM 生成了两个 output item，但只有第一个有 ToolCallEndEvent
        var events = new StreamEvent[]
        {
            new StreamStartEvent { Model = "mimo-v2.5-pro", Provider = "openai-response" },
            // 第一个工具调用（有 EndEvent）
            new ToolCallStartEvent { ContentIndex = 0, ToolName = "whereami", ToolCallId = "call_x" },
            new ToolCallDeltaEvent { ContentIndex = 0, ArgumentsDelta = "{}" },
            new ToolCallEndEvent
            {
                ToolCall = new ToolCallBlock
                {
                    Id = "call_x",
                    Name = "whereami",
                    Arguments = JsonSerializer.SerializeToElement(new { })
                }
            },
            // 第二个工具调用（相同 ID，没有 EndEvent，由 FinalizePending 处理）
            new ToolCallStartEvent { ContentIndex = 1, ToolName = "whereami", ToolCallId = "call_x" },
            new ToolCallDeltaEvent { ContentIndex = 1, ArgumentsDelta = "{}" },
            new DoneEvent { Reason = DoneReason.Complete }
        };

        var stream = new LlmStreamImpl(ToAsyncEnumerable(events));

        await foreach (var _ in stream) { }
        var response = await stream.GetResponseAsync();

        // 应该只有一个工具调用
        var toolCalls = response.GetToolCalls();
        Assert.Single(toolCalls);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
    }

    // ── 辅助方法 ──

    private static LlmRequest CreateSimpleRequest()
    {
        return new LlmRequest
        {
            Model = "gpt-4o",
            Messages = [Message.FromUser("Hello")]
        };
    }

    private async Task<JsonElement> GetRequestBodyAsync(LlmRequest request, bool stream)
    {
        var httpRequest = _adapter.CreateRequest(request, _config, stream);
        var json = await httpRequest.Content!.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string GetTestDataPath(string relativePath)
    {
        return Path.Combine(AppContext.BaseDirectory, "TestData", relativePath);
    }

    private static JsonElement LoadJson(string relativePath)
    {
        var json = File.ReadAllText(GetTestDataPath(relativePath));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static (string EventType, JsonElement Data) LoadStreamEvent(string relativePath)
    {
        var data = LoadJson($"Responses/StreamEvents/{relativePath}");
        var eventType = data.GetProperty("type").GetString()!;
        return (eventType, data);
    }

    private static JsonElement LoadResponse(string fileName)
    {
        return LoadJson($"Responses/{fileName}");
    }
}
