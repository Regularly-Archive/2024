using System.Text;
using Microsoft.SemanticKernel;

namespace SK.BasicChat;

# region
class OpenAIProxyHandler : HttpClientHandler
{
    private string _proxyUrl;
    public OpenAIProxyHandler(string proxyUrl)
    {
        _proxyUrl = proxyUrl;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.RequestUri = new Uri(_proxyUrl);
        return base.SendAsync(request, cancellationToken);
    }
}
# endregion

public class Program
{
    public static async Task Main(string[] args)
    {
        // 当需要使用一个代理服务或者与 OpenAI 兼容的服务时，可以使用自定义的 Handler 进行处理
        // 如使用 OpenAI 的 API 接口，可以直接构造一个 HttpClient
        var openaiProxyHandler = new OpenAIProxyHandler("https://api.moonshot.cn/v1/chat/completions");
        var httpClient = new HttpClient(openaiProxyHandler);


        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: "moonshot-v1-8k",
                apiKey: "",
                httpClient: httpClient
            )
            .Build();
        
        var chatFunction = kernel.CreateFunctionFromPrompt("{{ $input }}");

        var input = string.Empty;
        while ((input = Console.ReadLine()) != null)
        {
            Console.WriteLine($"User -> {input}");

            Console.Write("AI -> ");
            var arguments = new KernelArguments() { ["input"] = input };
            await foreach (var message in kernel.InvokeStreamingAsync<string>(chatFunction, arguments: arguments))
            {
                Console.Write(message);
            }
            Console.Write("\r\n");
        }
    }
}


