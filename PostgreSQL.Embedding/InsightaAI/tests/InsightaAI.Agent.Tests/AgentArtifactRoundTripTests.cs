using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Models;
using InsightaAI.Agent.Storage;
using InsightaAI.Agent.Tools;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// 集成测试：真实 Agent → 大文件读取 → 会话持久化/恢复 → artifact 回查。
/// 锁定三条链路：ToolEnd 事件携带 artifact 引用、artifact 文件内容完整落盘、
/// 会话恢复后 ToolResultState 仍指向可读回的 artifact。
/// </summary>
public sealed class AgentArtifactRoundTripTests
{
    [Fact]
    public async Task LargeFileRead_Should_CarryArtifact_From_Event_To_Restored_Session()
    {
        var artifactDir = Path.Combine(Path.GetTempPath(), $"insighta-artifacts-{Guid.NewGuid():N}");
        var storageDir = Path.Combine(Path.GetTempPath(), $"insighta-storage-{Guid.NewGuid():N}");
        var bigFile = Path.Combine(artifactDir, "big-input.log");
        try
        {
            Directory.CreateDirectory(artifactDir);
            Directory.CreateDirectory(storageDir);

            // 约 2000 行 × ~50 字符 ≈ 100KB，稳定超过 30KB 落盘阈值
            var content = string.Join("\n",
                Enumerable.Range(1, 2000).Select(i => $"line {i:D4}: {new string('x', 36)} metrics=ok"));
            await File.WriteAllTextAsync(bigFile, content);

            var toolRegistry = new ToolRegistry();
            toolRegistry.Register(new FileReadTool(new LocalFileSystem(), new FileReadState()));

            var services = new ServiceCollection();
            services.AddSingleton<ILlmClient>(new MockLlmClient(
                firstResponseToolCalls:
                [
                    new ToolCallBlock
                    {
                        Id = "call-read-1",
                        Name = "read_file",
                        Arguments = JsonSerializer.Deserialize<JsonElement>(
                            JsonSerializer.Serialize(new { file_path = bigFile, limit = 5000 }))
                    }
                ],
                secondResponse: "File read complete."));
            services.AddSingleton(toolRegistry);
            services.AddSingleton<IMessageStorage>(new JsonlMessageStorage(storageDir));
            services.AddSingleton<IToolResultArtifactStore>(
                new ToolResultArtifactStore(new LocalFileSystem(), artifactDir));
            using var serviceProvider = services.BuildServiceProvider();

            var config = new AgentConfig
            {
                Id = "artifact-agent",
                Name = "Artifact Agent",
                CustomInstructions = "You read files.",
                Model = "test-model",
                MaxToolRounds = 5
            };
            var agent = new Agent(config, serviceProvider);
            var storage = serviceProvider.GetRequiredService<IMessageStorage>();
            var session = await storage.CreateSessionAsync("test-model", "mock", workDir: storageDir);

            // Act：真实 Agent 执行大文件读取
            var events = new List<AgentEvent>();
            await foreach (var evt in agent.RunStreamAsync(
                "Read the big log file.",
                new AgentContext { SessionId = session.Id }))
            {
                events.Add(evt);
            }

            // Assert 1：ToolEnd 事件携带 artifact 引用，终端预览保持截断
            var toolEnd = Assert.Single(events.OfType<AgentToolEndEvent>());
            Assert.Equal("read_file", toolEnd.ToolName);
            Assert.False(toolEnd.IsError);
            Assert.NotNull(toolEnd.Artifact);
            Assert.NotNull(toolEnd.ResultPreview);
            Assert.True(toolEnd.ResultPreview.Length <= 103, "终端预览应保持 100 字符截断");
            Assert.True(toolEnd.Artifact.ByteSize > 30 * 1024, "大文件结果应触发落盘");
            Assert.True(File.Exists(toolEnd.Artifact.Path), "artifact 文件应已落盘");

            // Assert 2：artifact 内容完整（首尾行都在，未被预览截断）
            var persisted = await File.ReadAllTextAsync(toolEnd.Artifact.Path);
            Assert.Contains("line 0001:", persisted);
            Assert.Contains("line 2000:", persisted);

            // Assert 3：会话恢复——持久化记录的 ToolResultState 指向同一 artifact
            var records = await storage.GetMessagesAsync(session.Id);
            var toolRecord = Assert.Single(records, r => r.Role == "tool");
            var state = toolRecord.ToolResultState;
            Assert.NotNull(state?.Artifact);
            Assert.Equal(ToolResultRetentionLevel.Preview, state!.RetentionLevel);
            Assert.Equal(toolEnd.Artifact.Path, state.Artifact!.Path);

            // Assert 4：artifact 回查——恢复后的引用可读回完整内容
            var restored = toolRecord.ToLlmMessage();
            Assert.Equal(toolEnd.Artifact.Id, restored.ToolResultState?.Artifact?.Id);
            var roundTripped = await File.ReadAllTextAsync(restored.ToolResultState!.Artifact!.Path);
            Assert.Contains("line 0001:", roundTripped);
            Assert.Contains("line 2000:", roundTripped);
        }
        finally
        {
            if (Directory.Exists(artifactDir))
                Directory.Delete(artifactDir, recursive: true);
            if (Directory.Exists(storageDir))
                Directory.Delete(storageDir, recursive: true);
        }
    }
}
