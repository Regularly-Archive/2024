using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web;
using Microsoft.SemanticKernel.Planning;
using System.ComponentModel;
using Microsoft.SemanticKernel.Planning.Handlebars;
using System.Net.Http.Headers;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using System.Security.Cryptography;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using Newtonsoft.Json.Linq;
using Azure;
using System.Net;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Encodings.Web;

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
    private const string API_KEY = "954e6afaf1c94309856a522e107d6f30";
    private const string UNKNOW = "Unknow";

    [Description("城市代码映射字典定义")]
    private readonly Dictionary<string, string> _cityCodeMaps = new Dictionary<string, string>()
    {
        { "宝鸡","101110901" },
        { "西安","101050311" },
        { "渭南","101110501" },
        { "咸阳","101110200" },
    };

    [KernelFunction]
    [Description("返回指定城市对应代码，如果城市不存在，则返回字符串 'Unknow'.")]
    public string GetCityCode(string cityName)
    {
        if (cityName.EndsWith("市"))
            cityName = cityName.Substring(0, cityName.Length - 1);

        if (_cityCodeMaps.ContainsKey(cityName))
            return _cityCodeMaps[cityName].ToString();

        return UNKNOW;
    }

    [KernelFunction]
    [Description("返回指定城市的天气状况")]
    public async Task<string> GetWeather([Description("城市名称")] string cityName)
    {
        using var handler = new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip };
        using var httpClient = new HttpClient(handler);

        var cityCode = GetCityCode(cityName);

        if (cityCode == UNKNOW)
            return JsonConvert.SerializeObject(new { msg = "Unknow City", code = -1 });

        var payload = await httpClient.GetStringAsync($"https://devapi.qweather.com/v7/weather/now?key={API_KEY}&location={cityCode}");
        return payload;

    }
}
#endregion

#region 
sealed class GeoPlugin
{
    [KernelFunction]
    [Description("返回当前城市所在地址")]
    public async Task<string> GetCurrentLocation()
    {
        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetStringAsync("https://www.ip.cn/api/index?ip=&type=0");
            return JObject.Parse(response).Value<string>("address")!;
        }
    }
}
#endregion

#region
class NeteaseSearchModel
{
    public Result result { get; set; }
    public int code { get; set; }
}

class Result
{
    public Song[] songs { get; set; }
    public int songCount { get; set; }
}

class Song
{
    public long id { get; set; }
    public string name { get; set; }
    public Artist[] artists { get; set; }
    public Album album { get; set; }
    public int duration { get; set; }
    public long copyrightId { get; set; }
    public int status { get; set; }
    public object[] alias { get; set; }
    public int rtype { get; set; }
    public int ftype { get; set; }
    public long mvid { get; set; }
    public int fee { get; set; }
    public object rUrl { get; set; }
    public long mark { get; set; }
}

class Album
{
    public long id { get; set; }
    public string name { get; set; }
    public Artist artist { get; set; }
    public long publishTime { get; set; }
    public int size { get; set; }
    public long copyrightId { get; set; }
    public int status { get; set; }
    public long picId { get; set; }
    public long mark { get; set; }
}

class Artist
{
    public int id { get; set; }
    public string name { get; set; }
    public object picUrl { get; set; }
    public object[] alias { get; set; }
    public int albumSize { get; set; }
    public int picId { get; set; }
    public object fansGroup { get; set; }
    public string img1v1Url { get; set; }
    public int img1v1 { get; set; }
    public object trans { get; set; }
}

sealed class CloudMusicPlugin
{
    private const string SEARCH_URL = "http://music.163.com/api/search/get/web?csrf_token=hlpretag=&hlposttag=&s={0}&type=1&offset=0&total=true&limit=2";
    private const string SONG_URL = "http://music.163.com/song/media/outer/url?id={0}";
    private const string EMPTY_RESULT = "抱歉，没有为您找到相关歌曲";

    [KernelFunction]
    [Description("搜索并播放指定名称的歌曲")]
    public async Task<string> SearchMusic([Description("艺术家名称")] string artistName, [Description("歌曲名称")] string songName)
    {
        var handler = new HttpClientHandler() { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip };
        using (var httpClient = new HttpClient(handler))
        {
            var searchResult = await SearchByKeyword(httpClient, songName);
            if (searchResult!.code != 200 || searchResult.result.songs.Length == 0)
            {
                return EMPTY_RESULT;
            }

            var song = FilterSong(searchResult.result, artistName, songName);
            var artist = song.artists.FirstOrDefault()?.name;

            var response = await httpClient.GetAsync(string.Format(SONG_URL, song.id));
            if (response.StatusCode == HttpStatusCode.Redirect)
            {
                var location = response.Headers.Location;
                if (location!.AbsoluteUri == "http://music.163.com/404")
                {
                    return EMPTY_RESULT;
                }

                response = await httpClient.GetAsync(location);
                var downloadPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "download");
                if (!Directory.Exists(downloadPath)) Directory.CreateDirectory(downloadPath);
                downloadPath = Path.Combine(downloadPath, $"{song.name}.mp3");

                using (var fileStream = File.OpenWrite(downloadPath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                var processStartInfo = new ProcessStartInfo();
                processStartInfo.FileName = downloadPath;
                processStartInfo.UseShellExecute = true;
                Process.Start(processStartInfo);

                return $"已为您找到 {artist} 的《{song.name}》";
            }

            return EMPTY_RESULT;
        }
    }

    private async Task<NeteaseSearchModel> SearchByKeyword(HttpClient httpClient, string keyword)
    {
        var response = await httpClient.GetAsync(string.Format(SEARCH_URL, UrlEncoder.Default.Encode(keyword)));
        response.EnsureSuccessStatusCode();

        var responseConent = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<NeteaseSearchModel>(responseConent);
    }

    private Song FilterSong(Result result, string artistName, string songName)
    {
        Song song = null;

        if (!string.IsNullOrEmpty(artistName))
            song = result.songs.FirstOrDefault(x => x.artists[0].name == artistName);

        if (song == null || string.IsNullOrEmpty(artistName))
        {
            var random = new Random();
            var idx = random.Next(0, result.songs.Length);
            song = result.songs[idx];
        }

        return song;
    }
}
#endregion

public class Program
{
    private const string Serp_API_KEY = "2853397bb5232322e1c2418a63ecd27e1f037d421c54f3f03a67309726ed0ff0";

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

        // 目前 Microsoft.SemanticKernel.Plugins.* 的 Nuget 包都是预发行版，直接使用会提示警告
#pragma warning disable SKEXP0050
        kernel.Plugins.AddFromType<TimePlugin>("time");
#pragma warning restore SKEXP0050
        kernel.Plugins.AddFromType<MathPlugin>("math");
        kernel.Plugins.AddFromType<WeatherPlugin>("weather");
        kernel.Plugins.AddFromType<GeoPlugin>("Geo");

        // 可以使用 Bing 或者 Google 的插件，使用效果一般，需要绑定信用卡
        var serpConnector = new SerpSearchEngineConnector(apiKey: Serp_API_KEY, engine: "bing");
#pragma warning disable SKEXP0050
        var searchEnginePlugin = new WebSearchEnginePlugin(serpConnector);
#pragma warning restore SKEXP0050
        kernel.Plugins.AddFromObject(searchEnginePlugin);
        kernel.Plugins.AddFromType<CloudMusicPlugin>("cloud_music");

        // 手动调用插件
        var result = await kernel.InvokeAsync<string>("time", "Today");
        Console.WriteLine($"今天是哪一天 -> {result}");
        result = await kernel.InvokeAsync<string>("time", "Time");
        Console.WriteLine($"现在是几点？ -> {result}");
        result = await kernel.InvokeAsync<string>("math", "Sum", new KernelArguments() { ["a"] = 14, ["b"] = 15 });
        Console.WriteLine($"14 + 15 = ？ -> {result}");
        result = await kernel.InvokeAsync<string>("weather", "GetWeather", new KernelArguments() { ["cityName"] = "西安" });
        Console.WriteLine($"西安的天气如何？ -> {result}");
        result = await kernel.InvokeAsync<string>("cloud_music", "SearchMusic", new KernelArguments() { ["songName"] = "爱在西元前", ["artistName"] = "" });
        Console.WriteLine($"播放《爱在西元前》 -> {result}");


        // 从提示词模板构建一个翻译插件
        var translateFunction = kernel.CreateFunctionFromPrompt(
            @"""
            You’re a AI bot and you are good at English. I need your help for the following input:

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

        // 基于 Handlebars 的 Planner
#pragma warning disable SKEXP0060
        var handlebarPlanner = new HandlebarsPlanner();
#pragma warning restore SKEXP0060


        var input = string.Empty;
        while ((input = Console.ReadLine()) != null)
        {
            Console.WriteLine($"User -> {input}");

            var output = string.Empty;
            try
            {
                // 从 Planner 生成 Plan，执行 Plan
                // 目前，Plan 的生成和执行都不稳定，完全看运气
#pragma warning disable SKEXP0060
                var plan = await handlebarPlanner.CreatePlanAsync(kernel, input);
                output = await plan.InvokeAsync(kernel);
#pragma warning restore SKEXP0060
            }
            catch (Exception ex)
            {
                output = ex.Message;
            }

            Console.WriteLine($"AI -> {output}");
        }
    }
}


