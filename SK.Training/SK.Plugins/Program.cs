using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Planning;
using System.ComponentModel;

namespace SK.Plugins;

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

#region
sealed class MathPlugin
{
    [KernelFunction]
    [Description("求两个整数的和")]
    public string Sum([Description("第一个整数")]int a, [Description("第二个整数")]int b) => (a + b).ToString();
}
#endregion

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
                apiKey: "sk-IbKsN4zpWyJCrXUl8YXjDAVewlTuYOifkYgBLhzujhoSuHTd",
                httpClient: httpClient
            )
            .Build();
        
        // 目前 Microsoft.SemanticKernel.Plugins.* 的 Nuget 包都是预发行版，直接使用会提示警告
        #pragma warning disable SKEXP0050 
        kernel.Plugins.AddFromType<TimePlugin>("time");
        kernel.Plugins.AddFromType<MathPlugin>("math");
        #pragma warning restore SKEXP0050 

        var chatFunction = kernel.CreateFunctionFromPrompt("{{ $input }}");

        // 手动调用
        var result = await kernel.InvokeAsync<string>("time", "Today");
        Console.WriteLine($"今天是哪一天 -> {result}");
        result = await kernel.InvokeAsync<string>("time", "Time");
        Console.WriteLine($"现在是几点？ -> {result}");
        result = await kernel.InvokeAsync<string>("math", "Sum", new KernelArguments() { ["a"] = 14, ["b"] = 15 });
        Console.WriteLine($"14 + 15 = ？ -> {result}");

        // 从提示词模板构建一个插件
        var translateFunction = kernel.CreateFunctionFromPrompt(
            @"""
            You’re a AI bot and you will receive a input: {{ $input }}.
            Please translate it into English always and return the translated content only.
            """
        );
        result = await kernel.InvokeAsync<string>(translateFunction, new KernelArguments() { ["input"] = "有朋自远方来，不亦乐乎"});
        Console.WriteLine($"有朋自远方来，不亦乐乎？ -> {result}");
        
        // 通过 Planner 自动调用插件
        

        var input = string.Empty;
        while ((input = Console.ReadLine()) != null)
        {
            Console.WriteLine($"User -> {input}");
            var arguments = new KernelArguments() { ["input"] = input };
            Console.Write("AI -> ");
            await foreach (var message in kernel.InvokeStreamingAsync<string>(chatFunction, arguments: arguments))
            {
                Console.Write(message);
            }
            Console.Write("\r\n");
        }
    }
}


