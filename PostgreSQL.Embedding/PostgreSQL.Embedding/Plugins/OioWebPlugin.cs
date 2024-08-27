using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Json;
using System.ComponentModel;
using System.Text.Json.Serialization;
using static PostgreSQL.Embedding.Plugins.NMCWeatherPlugin;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "教书先生API")]
    public class OioWebPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public OioWebPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("历史上的今天都发生了哪些事情。")]
        public async Task<string> TodayInHistory()
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://api.oioweb.cn/api/common/history");
        }

        [KernelFunction]
        [Description("获取人民币(CNY)相对于各国货币的汇率，支持以下货币：美元(USD)、港币(HKD)、欧元(EUR)、日元(JPY)、澳大利亚元(AUD)、新加坡元(SGD)。")]
        public async Task<string> GetExchangeRate([Description("原始币种")] string source, [Description("目标币种")] string destination)
        {
            var curencies = new List<string>() { "CNY", "USD", "HKD", "EUR", "JPY", "AUD", "SGD" };

            if (!curencies.Contains(source)) return $"暂不支持币种:{source}";
            if (!curencies.Contains(destination)) return $"暂不支持币种:{destination}";

            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var apiResult = await httpClient.GetFromJsonAsync<ApiResult<List<CurrencyModel>>>($"https://api.oioweb.cn/api/common/ExchangeRateQueryInterface");
                if (apiResult.code != 200) return apiResult.msg;

                var currencyList = apiResult.result.data;
                if (source == "CNY")
                {
                    var targetCurrency = currencyList.FirstOrDefault(x => x.c == destination);
                    if (targetCurrency == null) return $"未查询到对应汇率：{destination}";

                    return targetCurrency.v;

                }
                else if (destination == "CNY")
                {
                    var targetCurrency = currencyList.FirstOrDefault(x => x.c == source);
                    if (targetCurrency == null) return $"未查询到对应汇率：{source}";

                    return (1M / decimal.Parse(targetCurrency.v)).ToString("f3");
                }
                else
                {
                    var cnyCurrency = await GetExchangeRate(source, "CNY");

                    var targetCurrency = currencyList.FirstOrDefault(x => x.c == destination);
                    if (targetCurrency == null) return $"未查询到对应汇率：{destination}";

                    return (decimal.Parse(cnyCurrency) * decimal.Parse(targetCurrency.v)).ToString("f3");
                }
            }
        }

        [KernelFunction]
        [Description("每日60秒读懂世界新闻")]
        public async Task<string> Get60sWorldInsight()
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                return await httpClient.GetStringAsync($"https://api.oioweb.cn/api/common/Get60sWorldInsight");
            }
        }

        [KernelFunction]
        [Description("获取公共假期信息")]
        public async Task<string> GetNextHolidayInfo()
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var holidayDayInfoResult = await httpClient.GetFromJsonAsync<HolidayInfoApiResult>($"https://api.oioweb.cn/api/common/getNextHolidayInfo");
            if (holidayDayInfoResult.Result == null || !holidayDayInfoResult.Result.Any())
                return "暂无可用的公共假期";

            var holidays = holidayDayInfoResult.Result.Where(x => x.Start >= DateTime.Today).ToList();
            if (!holidays.Any())
                return "暂无可用的公共假期";

            return JsonSerializerExtensions.Serialize(holidays);
        }

        [KernelFunction]
        [Description("获取必应壁纸，返回 Markdown 形式的图片")]
        public async Task<string> GetBingWallPaper()
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var data = await httpClient.GetFromJsonAsync<WallPaperInfoApiResult>($"https://api.oioweb.cn/api/bing");

            var random = new Random();
            var image = data.Result[random.Next(data.Result.Count)];
            return $"![{image.Title}]({image.Url})";
        }

        [KernelFunction]
        [Description("获取毒鸡汤")]
        public async Task<string> GetSoulWords()
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://api.oioweb.cn/api/SoulWords");
        }

        #region
        internal class HolidayInfo
        {
            [JsonPropertyName("holiday")]
            public DateTime Holiday { get; set; }

            [JsonPropertyName("enName")]
            public string EnName { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("year")]
            public int Year { get; set; }

            [JsonPropertyName("start")]
            public DateTime Start { get; set; }

            [JsonPropertyName("end")]
            public DateTime End { get; set; }

            [JsonPropertyName("occupied")]
            public List<string> Occupied { get; set; }
        }

        internal class HolidayInfoApiResult
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonPropertyName("result")]
            public List<HolidayInfo> Result { get; set; }
        }

        internal class CurrencyModel
        {
            public string c { get; set; }
            public string v { get; set; }
            public string d { get; set; }
        }

        internal class ApiResult<T>
        {
            public int code { get; set; }
            public string msg { get; set; }
            public ApiResultData<T> result { get; set; }
        }

        internal class ApiResultData<T>
        {
            public T data { get; set; }
        }

        internal class WallPaperInfo
        {
            [JsonPropertyName("url")]
            public string Url { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }
        }

        internal class WallPaperInfoApiResult
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonPropertyName("result")]
            public List<WallPaperInfo> Result { get; set; }
        }
        #endregion
    }
}
