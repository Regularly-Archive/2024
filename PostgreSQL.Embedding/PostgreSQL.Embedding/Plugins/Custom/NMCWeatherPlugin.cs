using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text.Json.Serialization;
using ThirdParty.Json.LitJson;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "中央气象台天气预报插件。提供中国各省市地区的天气预报查询功能，返回详细的天气信息（温度、湿度、风力、降水等）。", Version = "1.1")]
    public class NMCWeatherPlugin : BasePlugin
    {
        private readonly Dictionary<string, string> _provincesMap = new Dictionary<string, string>()
        {
            { "ABJ", "北京市" },
            { "ATJ", "天津市" },
            { "AHE", "河北省" },
            { "ASX", "山西省" },
            { "ANM", "内蒙古自治区" },
            { "ALN", "辽宁省" },
            { "AJL", "吉林省" },
            { "AHL", "黑龙江省" },
            { "ASH", "上海市" },
            { "AJS", "江苏省" },
            { "AZJ", "浙江省" },
            { "AAH", "安徽省" },
            { "AFJ", "福建省" },
            { "AJX", "江西省" },
            { "ASD", "山东省" },
            { "AHA", "河南省" },
            { "AHB", "湖北省" },
            { "AHN", "湖南省" },
            { "AGD", "广东省" },
            { "AGX", "广西壮族自治区" },
            { "AHI", "海南省" },
            { "ACQ", "重庆市" },
            { "ASC", "四川省" },
            { "AGZ", "贵州省" },
            { "AYN", "云南省" },
            { "AXZ", "西藏自治区" },
            { "ASN", "陕西省" },
            { "AGS", "甘肃省" },
            { "AQH", "青海省" },
            { "ANX", "宁夏回族自治区" },
            { "AXJ", "新疆维吾尔自治区" },
            { "AXG", "香港特别行政区" },
            { "AAM", "澳门特别行政区" },
            { "ATW", "台湾省" }
        };

        private readonly IHttpClientFactory _httpClientFactory;
        public NMCWeatherPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("获取当前所在位置的天气信息。通过 IP 定位获取当前位置，返回实时天气数据。")]
        public async Task<string> GetCurrentWeather()
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                var position = await httpClient.GetFromJsonAsync<NMCPosition>("http://www.nmc.cn/rest/position");
                var weather = await httpClient.GetStringAsync($"http://www.nmc.cn/rest/weather?stationid={position.Code}");

                return weather;
            }
        }

        [KernelFunction]
        [Description("获取指定城市的天气预报。输入省份和城市名称，返回该地区的天气信息。")]
        public async Task<string> GetWeather(
            [Description("省份名称，如：浙江省、广东省")] string provinceName,
            [Description("城市名称，如：杭州市、广州市")] string cityName)
        {
            var provinceCode = _provincesMap.FirstOrDefault(x => x.Value.IndexOf(provinceName) != -1).Key;
            if (string.IsNullOrEmpty(provinceCode)) return "没有找到该省份信息";

            using (var httpClient = _httpClientFactory.CreateClient())
            {
                if (cityName.EndsWith("市")) cityName = cityName.TrimEnd('市');
                var cities = await httpClient.GetFromJsonAsync<List<NMCCity>>($"http://www.nmc.cn/rest/province/{provinceCode}");
                var city = cities.FirstOrDefault(x => x.City.IndexOf(cityName) != -1);

                if (city == null) return "没有找到该城市信息";
                var weather = await httpClient.GetStringAsync($"http://www.nmc.cn/rest/weather?stationid={city.Code}");
                return weather;
            }
        }

        internal class NMCPosition
        {
            [JsonPropertyName("code")]
            public string Code { get; set; }

            [JsonPropertyName("province")]
            public string Province { get; set; }

            [JsonPropertyName("city")]
            public string City { get; set; }

            [JsonPropertyName("url")]
            public string Url { get; set; }
        }

        internal class NMCCity
        {
            [JsonPropertyName("city")]
            public string City { get; set; }

            [JsonPropertyName("code")]
            public string Code { get; set; }
        }
    }
}
