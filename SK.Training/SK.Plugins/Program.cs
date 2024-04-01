using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Planning;
using System.ComponentModel;
using Microsoft.SemanticKernel.Planning.Handlebars;
using System.Net.Http.Headers;

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
    public string Sum([Description("第一个整数")] int a, [Description("第二个整数")] int b) => (a + b).ToString();
}
#endregion

#region
sealed class WeatherPlugin
{
    [Description("城市代码映射字典定义")]
    private readonly Dictionary<string, int> _cityCodeMaps = new Dictionary<string, int>()
    {
        { "宝鸡",57016 },
        { "西安",57036 },
        { "渭南",57045 },
        { "咸阳",57048 },
    };

    [KernelFunction]
    [Description("返回指定城市的代码，如果城市不存在，则返回:unknow.")]
    public string GetCityCode(string cityName)
    {
        if (_cityCodeMaps.ContainsKey(cityName)) return _cityCodeMaps[cityName].ToString();
        return "unknow";
    }

    [KernelFunction]
    [Description("返回指定城市的天气状况")]
    public async Task<string> GetWeather([Description("城市名称")]string cityName)
    {
        var cityCode = GetCityCode(cityName);
        if (cityCode == "unknow") return "{\"msg\":\"unknow city\", \"code\": -1}";

        using var client = new HttpClient();
        client.BaseAddress = new Uri("http://www.nmc.cn");
        client.DefaultRequestHeaders.Add("User-Agent", "CJAVAPY BOT");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"rest/weather?stationid={cityCode}&_=1672315767048";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadAsStringAsync();

        return result;
    }
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
        #pragma warning restore SKEXP0050
        kernel.Plugins.AddFromType<MathPlugin>("math");
        kernel.Plugins.AddFromType<WeatherPlugin>("weather");

        // 手动调用插件
        var result = await kernel.InvokeAsync<string>("time", "Today");
        Console.WriteLine($"今天是哪一天 -> {result}");
        result = await kernel.InvokeAsync<string>("time", "Time");
        Console.WriteLine($"现在是几点？ -> {result}");
        result = await kernel.InvokeAsync<string>("math", "Sum", new KernelArguments() { ["a"] = 14, ["b"] = 15 });
        Console.WriteLine($"14 + 15 = ？ -> {result}");
        result = await kernel.InvokeAsync<string>("weather", "GetWeather", new KernelArguments() { ["cityName"] = "西安" });
        Console.WriteLine($"西安的天气如何？ -> {result}");


        // 从提示词模板构建一个翻译插件
        var translateFunction = kernel.CreateFunctionFromPrompt(
            @"""
            You’re a AI bot and you are good at English. I need you help for the following input:

            {{ $input }}

            Please translate it into English always and return the translated content only.
            """
        );
        result = await kernel.InvokeAsync<string>(translateFunction, new KernelArguments() { ["input"] = "有朋自远方来，不亦乐乎" });
        Console.WriteLine($"有朋自远方来，不亦乐乎？ -> {result}");

        // 通过 Planner 自动调用插件
        // 目前支持下面两种 Planner
        // FunctionCallingStepwisePlanner: 基于 OpenAI 的 Function Calling
        // HandlebarsPlanner: 基于 Handlebars 模板的提示词工程

        #pragma warning disable SKEXP0060
        // 基于 Handlebars 的
        var handlebarPlanner = new HandlebarsPlanner();
        #pragma warning restore SKEXP0060


        var input = string.Empty;
        while ((input = Console.ReadLine()) != null)
        {
            Console.WriteLine($"User -> {input}");

            // 从 Planner 生成 Plan
            #pragma warning disable SKEXP0060
            var plan = await handlebarPlanner.CreatePlanAsync(kernel, input);
            #pragma warning restore SKEXP0060

            #pragma warning disable SKEXP0060
            var output = await plan.InvokeAsync(kernel);
            #pragma warning restore SKEXP0060

            #pragma warning disable SKEXP0060
            Console.WriteLine($"AI -> {output}");
            #pragma warning disable SKEXP0060
        }
    }
}


