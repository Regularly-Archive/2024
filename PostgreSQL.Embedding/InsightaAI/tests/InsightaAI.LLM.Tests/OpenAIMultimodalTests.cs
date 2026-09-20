using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// OpenAI Chat Completions 多模态（图片）请求序列化测试。
/// 保障线格式：user 消息含图片时 content 为 parts 数组（text / image_url）；
/// 纯文本消息 content 保持字符串，不破坏对严格端点的兼容。
/// </summary>
public class OpenAIMultimodalTests
{
    private static async Task<JsonElement> SerializeRequestAsync(LlmRequest request)
    {
        var adapter = new OpenAI.OpenAIAdapter();
        var config = new InsightaAI.LLM.Abstractions.ProviderConfig { ApiKey = "test-key" };

        using var httpRequest = adapter.CreateRequest(request, config, stream: false);
        var json = await httpRequest.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static ImageBlock CreateImageBlock(string mediaType = "image/png", string data = "aGVsbG8=") =>
        new() { Source = new ImageSource { MediaType = mediaType, Data = data } };

    [Fact]
    public async Task UserMessage_WithImage_SerializesContentAsPartsArray()
    {
        var root = await SerializeRequestAsync(new LlmRequest
        {
            Model = "test-model",
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextBlock { Text = "What is in this picture?" },
                        CreateImageBlock()
                    ]
                }
            ]
        });

        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(2, content.GetArrayLength());

        var textPart = content[0];
        Assert.Equal("text", textPart.GetProperty("type").GetString());
        Assert.Equal("What is in this picture?", textPart.GetProperty("text").GetString());

        var imagePart = content[1];
        Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,aGVsbG8=",
            imagePart.GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task UserMessage_TextOnly_KeepsPlainStringContent()
    {
        var root = await SerializeRequestAsync(new LlmRequest
        {
            Model = "test-model",
            Messages = [Message.FromUser("Just plain text.")]
        });

        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal("Just plain text.", content.GetString());
    }

    [Fact]
    public async Task UserMessage_ImageOnly_ProducesSingleImagePart()
    {
        var root = await SerializeRequestAsync(new LlmRequest
        {
            Model = "test-model",
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content = [CreateImageBlock(mediaType: "image/jpeg", data: "aGVsbG8=")]
                }
            ]
        });

        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Assert.Equal("data:image/jpeg;base64,aGVsbG8=",
            content[0].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task ToolResultMessage_AlwaysSerializesStringContent()
    {
        var root = await SerializeRequestAsync(new LlmRequest
        {
            Model = "test-model",
            Messages =
            [
                Message.FromToolResult("call_1", "read_image",
                    [new TextBlock { Text = "a red apple on a wooden table" }])
            ]
        });

        var message = root.GetProperty("messages")[0];
        Assert.Equal("tool", message.GetProperty("role").GetString());
        Assert.Equal("call_1", message.GetProperty("tool_call_id").GetString());
        Assert.Equal(JsonValueKind.String, message.GetProperty("content").ValueKind);
        Assert.Equal("a red apple on a wooden table", message.GetProperty("content").GetString());
    }
}
