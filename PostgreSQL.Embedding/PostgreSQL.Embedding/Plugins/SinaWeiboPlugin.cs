using Microsoft.SemanticKernel;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "新浪微博插件")]
    public class SinaWeiboPlugin : BasePlugin
    {
        private const string ANALYSE_WEIBO_FEEDS_PROMPT =
            """
            [Role] 
            你是一位精通社交媒体数据分析的专家，擅长通过用户在社交媒体上的行为和言论来分析其用户画像。

            [Goals]
            你的目标是精准地描绘微博用户的基本属性、情绪倾向、性格特征和兴趣爱好。

            请根据给定的上下文，参照下面的示例进行对微博用户进行分析
            1、用户画像摘要：年龄、性别、职业、教育背景等基本信息。
            2、情绪变化图表：展示用户情绪随时间的变化趋势。
            3、性格分析：根据用户言论分析其性格特征，如外向性、神经质等。
            4、兴趣爱好列表：根据用户关注的话题和参与的活动推断其兴趣爱好。

            现在开始分析，下面是微博用户的相关信息：

            Profile: 

            {{$profile}}

            Feeds:

            {{$feeds}}

            """;

        private const string MOCKING_WEIBO_FEEDS_PROMPT =
            """"
            You are a professional commentator known for your edgy and provocative style. 
            Your task is to look at people's tweets and rate their personalities based on that. Be edgy and provocative, be mean a little. Don't be cringy. 
            Here's a good attempt of a roast: 
            """Alright, let's break this down. You're sitting in a jungle of houseplants, barefoot and looking like you just rolled out of bed. 
            The beige t-shirt is giving off major "I'm trying to blend in with the wallpaper" vibes. And those black pants? They scream "I couldn't be bothered to find something that matches." 
            But hey, at least you look comfortable. Comfort is key, right? Just maybe not when you're trying to make a fashion statement."""

            Input:

            <Profile>{{$profile}}</Profile>
            <Feeds>{{$feeds}}</Feeds>

            Output (请用中文输出):

            """";

        [PluginParameter(Description = "抓取最新微博数目")]
        private int DEFAULT_RETRIEVE_NUMBER = 50;

        private IHttpClientFactory _httpClientFactory;
        public SinaWeiboPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// 分析指定微博用户
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        [KernelFunction]
        [Description("分析指定微博用户")]
        public async Task<string> AnalyseAsync([Description("微博用户Id")] string uid, Kernel kernel)
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var profile = await GetWeiboProfileAsync(httpClient, uid);
            var feeds = await GetWeiboFeedsAsync(httpClient, uid);

            var clonedKernel = kernel.Clone();
            var promptTemplate = new CallablePromptTemplate(ANALYSE_WEIBO_FEEDS_PROMPT);
            promptTemplate.AddVariable("profile", JsonConvert.SerializeObject(profile));
            promptTemplate.AddVariable("feeds", string.Join("\r\n", feeds.Select(x => x.Content)));

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            return functionResult.GetValue<string>();
        }

        /// <summary>
        /// 嘲讽指定微博用户
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="kernel"></param>
        /// <returns></returns>
        [KernelFunction]
        [Description("嘲讽指定微博用户")]
        public async Task<string> MockingAsync([Description("微博用户Id")] string uid, Kernel kernel)
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var profile = await GetWeiboProfileAsync(httpClient, uid);
            var feeds = await GetWeiboFeedsAsync(httpClient, uid);

            var clonedKernel = kernel.Clone();
            var promptTemplate = new CallablePromptTemplate(MOCKING_WEIBO_FEEDS_PROMPT);
            promptTemplate.AddVariable("profile", JsonConvert.SerializeObject(profile));
            promptTemplate.AddVariable("feeds", string.Join("\r\n", feeds.Select(x => x.Content)));

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            return functionResult.GetValue<string>();
        }


        private async Task<WeiboProfile> GetWeiboProfileAsync(HttpClient httpClient, string uid)
        {
            var response = await httpClient.GetStringAsync($"https://m.weibo.cn/api/container/getIndex?type=uid&value={uid}");

            var jObject = JObject.Parse(response);
            if (jObject["ok"].Value<int>() != 1)
                throw new ArgumentException("请确认微博用户 ID 是否正确");

            var userInfo = jObject["data"]["userInfo"].ToString();
            return JsonConvert.DeserializeObject<WeiboProfile>(userInfo);
        }

        private async Task<IEnumerable<WeiboFeed>> GetWeiboFeedsAsync(HttpClient httpClient, string uid, int? limit = null)
        {
            if (limit == null) limit = DEFAULT_RETRIEVE_NUMBER;

            var sinceId = string.Empty;
            var containerId = await GetContainerId(httpClient, uid);

            var feeds = new List<WeiboFeed>();
            while (feeds.Count < limit)
            {
                var pagedFeeds = await ExtractWeiFeedsAsync(httpClient, uid, containerId, sinceId);
                feeds.AddRange(pagedFeeds.Feeds);
                sinceId = pagedFeeds.SinceId;
            }

            return feeds.Take(limit.Value).ToList();
        }

        private async Task<string> GetContainerId(HttpClient httpClient, string uid)
        {
            var response = await httpClient.GetStringAsync($"https://m.weibo.cn/api/container/getIndex?type=uid&value={uid}");

            var tabsInfo = JObject.Parse(response)["data"]["tabsInfo"]["tabs"].Children();
            return tabsInfo.FirstOrDefault(x => x["tabKey"].Value<string>() == "weibo")["containerid"].Value<string>();
        }

        private async Task<PagedFeeds> ExtractWeiFeedsAsync(HttpClient httpClient, string uid, string containerId, string sinceId)
        {
            var response = await httpClient.GetStringAsync($"https://m.weibo.cn/api/container/getIndex?type=uid&value={uid}&containerid={containerId}&since_id={sinceId}");

            var jObject = JObject.Parse(response);
            var newSinceId = jObject["data"]["cardlistInfo"]["since_id"].Value<string>();
            var mblogs = jObject["data"]["cards"].Select(x => x["mblog"]);
            var mblogs_json = JsonConvert.SerializeObject(mblogs);

            var feeds = JsonConvert.DeserializeObject<IEnumerable<WeiboFeed>>(mblogs_json);
            return new PagedFeeds() { Feeds = feeds, SinceId = newSinceId };
        }


        internal class WeiboProfile
        {
            [JsonProperty("screen_name")]
            public string NickName { get; set; }

            [JsonProperty("gender")]
            public string Gender { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("follow_count")]
            public string FollowCount { get; set; }

            [JsonProperty("followers_count")]
            public string FollowersCount { get; set; }

            [JsonProperty("avatar_hd")]
            public string AvatarUrl { get; set; }

            [JsonProperty("profile_url")]
            public string ProfileUrl { get; set; }
        }

        internal class WeiboFeed
        {
            [JsonProperty("created_at")]
            public string CreatedAt { get; set; }

            [JsonProperty("text")]
            public string Content { get; set; }
        }

        internal class PagedFeeds
        {
            public string SinceId { get; set; }
            public IEnumerable<WeiboFeed> Feeds { get; set; }
        }
    }
}
