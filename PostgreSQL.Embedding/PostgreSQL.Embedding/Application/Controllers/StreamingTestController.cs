using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common.Streaming;

namespace PostgreSQL.Embedding.Application.Controllers
{
    /// <summary>
    /// 测试 SSE 流式事件的控制器
    /// </summary>
    [Route("api/test/streaming")]
    [AllowAnonymous]
    public class StreamingTestController : ControllerBase
    {
        /// <summary>
        /// 测试文本流
        /// </summary>
        [HttpGet("text")]
        public IActionResult TextStream([FromQuery] string? message = "Hello, this is a streaming test!")
        {
            return new SseResult(TextStreamAsync(message));
        }

        private async IAsyncEnumerable<ISseEvent> TextStreamAsync(string? message, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            yield return evt.MessageStart("test-model");

            var words = message.Split(' ');
            var index = 0;

            foreach (var word in words)
            {
                yield return evt.TextBlockStart(index);

                foreach (var c in word)
                {
                    yield return evt.TextDelta(index, c.ToString());
                    await Task.Delay(30, ct);
                }

                yield return evt.BlockStop(index);
                index++;
            }

            yield return evt.MessageStop();
        }

        /// <summary>
        /// 测试思考流（thinking）
        /// </summary>
        [HttpGet("thinking")]
        public IActionResult ThinkingStream()
        {
            return new SseResult(ThinkingStreamAsync());
        }

        private async IAsyncEnumerable<ISseEvent> ThinkingStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            yield return evt.MessageStart("test-model");

            // 思考块
            yield return evt.ThinkingBlockStart(0);

            var thinking = "用户要求讲一个故事。我需要：1. 确定故事主题；2. 创建角色；3. 设计情节。";
            foreach (var chunk in SplitByLength(thinking, 10))
            {
                yield return evt.ThinkingDelta(0, chunk);
                await Task.Delay(50, ct);
            }

            // 思考签名
            yield return evt.ThinkingSignature(0, "abc123signature");
            yield return evt.BlockStop(0);

            // 文本块（故事内容）
            yield return evt.TextBlockStart(1);

            var story = "从前有一只小狐狸，它住在一个美丽的森林里。";
            foreach (var c in story)
            {
                yield return evt.TextDelta(1, c.ToString());
                await Task.Delay(30, ct);
            }

            yield return evt.BlockStop(1);
            yield return evt.MessageStop();
        }

        /// <summary>
        /// 测试工具调用流
        /// </summary>
        [HttpGet("tool-use")]
        public IActionResult ToolUseStream()
        {
            return new SseResult(ToolUseStreamAsync());
        }

        private async IAsyncEnumerable<ISseEvent> ToolUseStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            yield return evt.MessageStart("test-model");

            // 思考
            yield return evt.TextBlockStart(0);

            var thoughts = new[] { "我需要搜索一些信息", "让我搜索天气情况" };
            foreach (var thought in thoughts)
            {
                foreach (var c in thought)
                {
                    yield return evt.TextDelta(0, c.ToString());
                }
                await Task.Delay(20, ct);
            }
            yield return evt.BlockStop(0);

            // 工具调用
            var toolId = Guid.NewGuid().ToString("N");
            yield return evt.ToolUse("search", new() { ["query"] = "北京天气", ["limit"] = 5 });
            await Task.Delay(500, ct);

            // 工具结果
            yield return evt.ToolResult(toolId, new 
            {
                results = new[]
                {
                    new { title = "北京天气预报", url = "https://weather.com/123", snippet = "晴，25°C" },
                    new { title = "今日天气", url = "https://weather.com/456", snippet = "多云，22°C" }
                }
            });

            // 回复
            yield return evt.TextBlockStart(1);
            var response = "根据搜索结果，北京今天天气晴朗，气温25°C。";
            foreach (var c in response)
            {
                yield return evt.TextDelta(1, c.ToString());
                await Task.Delay(20, ct);
            }
            yield return evt.BlockStop(1);

            yield return evt.MessageStop();
        }

        /// <summary>
        /// 测试完整 Agent 流程
        /// </summary>
        [HttpGet("agent")]
        public IActionResult AgentStream([FromQuery] string input = "讲个故事")
        {
            return new SseResult(AgentStreamAsync(input));
        }

        private async IAsyncEnumerable<ISseEvent> AgentStreamAsync(string input, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            yield return evt.MessageStart("MiniMax-M2.1");

            // 思考
            yield return evt.ThinkingBlockStart(0);

            var thinking = $"用户说\"{input}\"，这是一个创作故事的请求。我应该用中文回复，创作一个有趣的故事。";
            foreach (var chunk in SplitByLength(thinking, 15))
            {
                yield return evt.ThinkingDelta(0, chunk);
                await Task.Delay(30, ct);
            }

            // 思考签名
            yield return evt.ThinkingSignature(0, "sig123");
            yield return evt.BlockStop(0);

            // 故事内容
            yield return evt.TextBlockStart(1);

            var story = "小狐狸的星星灯笼\n\n森林里住着一只小狐狸，它有一盏神奇的灯笼...";
            foreach (var c in story)
            {
                yield return evt.TextDelta(1, c.ToString());
                await Task.Delay(25, ct);
            }
            yield return evt.BlockStop(1);

            yield return evt.MessageStop();
        }

        /// <summary>
        /// 测试心跳（ping）
        /// </summary>
        [HttpGet("ping")]
        public IActionResult PingStream([FromQuery] int count = 5)
        {
            return new SseResult(PingStreamAsync(count));
        }

        private async IAsyncEnumerable<ISseEvent> PingStreamAsync(int count, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            for (int i = 0; i < count; i++)
            {
                yield return evt.Ping();
                await Task.Delay(3000, ct);
            }
        }

        /// <summary>
        /// 组合测试：文本 + 工具调用
        /// </summary>
        [HttpGet("combined")]
        public IActionResult CombinedStream()
        {
            return new SseResult(CombinedStreamAsync());
        }

        private async IAsyncEnumerable<ISseEvent> CombinedStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var evt = default(ISseEvent);
            yield return evt.MessageStart("test-model");

            // 思考
            yield return evt.ThinkingBlockStart(0);
            var thinking = "用户问天气，我需要搜索一下。";
            foreach (var chunk in SplitByLength(thinking, 10))
            {
                yield return evt.ThinkingDelta(0, chunk);
                await Task.Delay(30, ct);
            }
            yield return evt.BlockStop(0);

            // 工具调用
            var toolId = Guid.NewGuid().ToString("N");
            yield return evt.ToolUse("search", new() { ["query"] = "上海天气" });
            await Task.Delay(800, ct);

            // 工具结果
            yield return evt.ToolResult(toolId, "晴，28°C");

            // 回复
            yield return evt.TextBlockStart(1);
            var response = "上海今天天气晴朗，气温28°C。";
            foreach (var c in response)
            {
                yield return evt.TextDelta(1, c.ToString());
                await Task.Delay(20, ct);
            }
            yield return evt.BlockStop(1);

            yield return evt.MessageStop();
        }

        private static IEnumerable<string> SplitByLength(string str, int maxLength)
        {
            for (int i = 0; i < str.Length; i += maxLength)
            {
                yield return str.Substring(i, Math.Min(maxLength, str.Length - i));
            }
        }
    }
}
