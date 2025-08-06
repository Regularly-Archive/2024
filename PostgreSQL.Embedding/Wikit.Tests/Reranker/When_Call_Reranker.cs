using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Utils;
using Shouldly;

namespace Wikit.Tests.Reranker;

public class When_Call_Reranker
{
    private readonly IPythonEnvironment _pythonEnvironment;
    public When_Call_Reranker()
    {
        var serviceProvider = new ServiceCollection()
            .AddHttpClient()
            .AddHttpContextAccessor()
            .AddPythonRuntime(new PythonConfig()
            {
                PythonExecute = "C:\\Program Files\\Python\\Python310\\",
                PythonVersion = "3.10"
            })
            .BuildServiceProvider();

        _pythonEnvironment = serviceProvider.GetService<IPythonEnvironment>();
    }


    [Fact]
    public void It_Should_Compute_Scores_With_Flash_Reranker()
    {
        var flashReranker = _pythonEnvironment.FlashReranker();
        var scores = flashReranker.ComputeScores("How to speedup LLMs?", new List<string>
        {
            "Introduce *lookahead decoding*: - a parallel decoding algo to accelerate LLM inference - w/o the need for a draft model or a data store - linearly decreases # decoding steps relative to log(FLOPs) used per decoding step.",
            "LLM inference efficiency will be one of the most crucial topics for both industry and academia, simply because the more efficient you are, the more $$$ you will save. vllm project is a must-read for this direction, and now they have just released the paper",
            "There are many ways to increase LLM inference throughput (tokens/second) and decrease memory footprint, sometimes at the same time. Here are a few methods I’ve found effective when working with Llama 2. These methods are all well-integrated with Hugging Face. This list is far from exhaustive; some of these techniques can be used in combination with each other and there are plenty of others to try. - Bettertransformer (Optimum Library): Simply call `model.to_bettertransformer()` on your Hugging Face model for a modest improvement in tokens per second. - Fp4 Mixed-Precision (Bitsandbytes): Requires minimal configuration and dramatically reduces the model's memory footprint. - AutoGPTQ: Time-consuming but leads to a much smaller model and faster inference. The quantization is a one-time cost that pays off in the long run.",
            "Ever want to make your LLM inference go brrrrr but got stuck at implementing speculative decoding and finding the suitable draft model? No more pain! Thrilled to unveil Medusa, a simple framework that removes the annoying draft model while getting 2x speedup.",
            "vLLM is a fast and easy-to-use library for LLM inference and serving. vLLM is fast with: State-of-the-art serving throughput Efficient management of attention key and value memory with PagedAttention Continuous batching of incoming requests Optimized CUDA kernels",

        });

        this.ShouldSatisfyAllConditions(
            () => scores.Count.ShouldBe(4)
        );
    }
}
