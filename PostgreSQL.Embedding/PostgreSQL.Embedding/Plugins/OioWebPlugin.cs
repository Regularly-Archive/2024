using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;
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
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                return await httpClient.GetStringAsync($"https://api.oioweb.cn/api/common/history");
            }
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

                } else if (destination == "CNY")
                {
                    var targetCurrency = currencyList.FirstOrDefault(x => x.c == source);
                    if (targetCurrency == null) return $"未查询到对应汇率：{source}";

                    return (1M / decimal.Parse(targetCurrency.v)).ToString("f3");
                } 
                else
                {
                    var middleCurrency = await GetExchangeRate(source, "CNY");

                    var targetCurrency = currencyList.FirstOrDefault(x => x.c == destination);
                    if (targetCurrency == null) return $"未查询到对应汇率：{destination}";

                    return (decimal.Parse(middleCurrency) *  decimal.Parse(targetCurrency.v)).ToString("f3");
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

        internal class CurrencyModel
        {
            public string c { get; set; }
            public string v { get; set; }
            public string d {  get; set; }
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
    }
}
