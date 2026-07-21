using InsightaAI.Agent.Context.Summary;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;

namespace InsightaAI.Agent.Tests.Context;

public class SummaryServiceTests
{
    [Fact]
    public async Task SummarizeAsync_Should_ReturnFullSummary()
    {
        var client = new MockLlmClient(response: "<summary>Full summary</summary>");
        var service = CreateService(client);

        var result = await service.SummarizeAsync([Message.FromUser("conversation")]);

        Assert.True(result.Success);
        Assert.Equal(SummaryMode.Full, result.Mode);
        Assert.Equal("Full summary", result.Summary);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task UpdateAsync_Should_IncludePreviousSummary()
    {
        var client = new MockLlmClient(response: "<summary>Updated summary</summary>");
        var service = CreateService(client);

        var result = await service.UpdateAsync("Previous state", [Message.FromUser("new fact")]);

        Assert.True(result.Success);
        Assert.Equal(SummaryMode.Incremental, result.Mode);
        var prompt = client.Requests.Single().Messages.Last().GetTextContent();
        Assert.Contains("Previous state", prompt);
        Assert.Contains("new fact", client.Requests.Single().Messages[^2].GetTextContent());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SummaryPrompt_Should_ContainCompleteOutputTemplate(bool incremental)
    {
        var client = new MockLlmClient(response: "<summary>Summary</summary>");
        var service = CreateService(client);

        if (incremental)
            await service.UpdateAsync("Previous state", [Message.FromUser("new fact")]);
        else
            await service.SummarizeAsync([Message.FromUser("conversation")]);

        var prompt = client.Requests.Single().Messages.Last().GetTextContent();
        Assert.Contains("## Goal", prompt);
        Assert.Contains("## Constraints & Preferences", prompt);
        Assert.Contains("### Done", prompt);
        Assert.Contains("### In Progress", prompt);
        Assert.Contains("### Blocked", prompt);
        Assert.Contains("## Key Decisions", prompt);
        Assert.Contains("## Next Steps", prompt);
        Assert.Contains("## Critical Context", prompt);
        Assert.Contains("## Relevant Files", prompt);
        Assert.Contains("REMINDER: Do NOT call any tools. Respond with plain text only.", prompt);
    }

    [Fact]
    public async Task MaxTokens_Should_RetryWithAggressiveCompression()
    {
        var client = new MockLlmClient(
            response: "<summary>Incomplete",
            secondResponse: "<summary>Recovered summary</summary>",
            firstFinishReason: DoneReason.MaxTokens);
        var service = CreateService(client);

        var result = await service.SummarizeAsync([Message.FromUser("conversation")]);

        Assert.True(result.Success);
        Assert.Equal("Recovered summary", result.Summary);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("compress more aggressively", client.Requests[1].Messages.Last().GetTextContent());
    }

    [Fact]
    public async Task RepeatedMaxTokens_Should_NotReturnPartialSummary()
    {
        var client = new MockLlmClient(
            response: "<summary>Incomplete",
            firstFinishReason: DoneReason.MaxTokens,
            secondFinishReason: DoneReason.MaxTokens);
        var service = CreateService(client);

        var result = await service.UpdateAsync("Previous state", [Message.FromUser("new fact")]);

        Assert.False(result.Success);
        Assert.Null(result.Summary);
        Assert.Equal(DoneReason.MaxTokens, result.FinishReason);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task MissingClosingTag_Should_BeRejected()
    {
        var client = new MockLlmClient(response: "<summary>Incomplete");
        var service = CreateService(client, maxAttempts: 1);

        var result = await service.SummarizeAsync([Message.FromUser("conversation")]);

        Assert.False(result.Success);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_ReturnNormalizedShortTitle()
    {
        var client = new MockLlmClient(response: "# \"设计会话标题。\"\nExtra explanation");
        var service = CreateService(client);

        var title = await service.GenerateTitleAsync("我想给第一次创建的会话生成标题");

        Assert.Equal("设计会话标题", title);
        var request = client.Requests.Single();
        Assert.Equal(256, request.MaxTokens);
        Assert.Empty(request.Tools!);
        Assert.Contains("我想给第一次创建的会话生成标题", request.Messages.Single().GetTextContent());
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_EnforceCharacterLimit()
    {
        var client = new MockLlmClient(response: "1234567890");
        var service = CreateService(client, titleMaxCharacters: 6);

        var title = await service.GenerateTitleAsync("test");

        Assert.Equal("123456", title);
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_RetryWhenMaxTokensReached()
    {
        var client = new MockLlmClient(
            response: "",
            secondResponse: "Recovered title",
            firstFinishReason: DoneReason.MaxTokens);
        var service = CreateService(client);

        var title = await service.GenerateTitleAsync("test");

        Assert.Equal("Recovered title", title);
        Assert.Equal(2, client.CallCount);
        Assert.Equal(256, client.Requests[0].MaxTokens);
        Assert.Equal(512, client.Requests[1].MaxTokens);
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_KeepUsableTextWhenMaxTokensReached()
    {
        var client = new MockLlmClient(
            response: "Usable title",
            firstFinishReason: DoneReason.MaxTokens);
        var service = CreateService(client);

        var title = await service.GenerateTitleAsync("test");

        Assert.Equal("Usable title", title);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_ReturnNullForEmptyInput()
    {
        var client = new MockLlmClient();
        var service = CreateService(client);

        var title = await service.GenerateTitleAsync("  ");

        Assert.Null(title);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task GenerateTitleAsync_Should_FallbackToInitialMessageWhenLlmFails()
    {
        var client = new MockLlmClient(
            response: "",
            firstFinishReason: DoneReason.MaxTokens,
            secondFinishReason: DoneReason.MaxTokens);
        var service = CreateService(client);

        var title = await service.GenerateTitleAsync(
            "## 现在可以了，但我建议做个降级处理，如果 LLM 生成失败可以截取用户输入\nMore details");

        Assert.Equal("现在可以了，但我建议做个降级处理，如果 LLM 生成失败可…", title);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateTitleAsync_FallbackShould_NotSplitEmoji()
    {
        var client = new MockLlmClient(response: "");
        var service = CreateService(client, titleFallbackMaxCharacters: 4);

        var title = await service.GenerateTitleAsync("😀😀😀😀😀 test");

        Assert.Equal("😀😀😀…", title);
    }

    private static SummaryService CreateService(
        MockLlmClient client,
        int maxAttempts = 2,
        int titleMaxCharacters = 40,
        int titleFallbackMaxCharacters = 30) =>
        new(new SummaryOptions
        {
            Model = "mock/test-model",
            ClientFactory = _ => client,
            MaxTokens = 2048,
            TargetTokens = 1200,
            MaxAttempts = maxAttempts,
            TitleMaxCharacters = titleMaxCharacters,
            TitleFallbackMaxCharacters = titleFallbackMaxCharacters
        });
}
