using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "TMDB（The Movie Database）影视数据库插件。提供电影和电视剧的搜索、信息查询等功能。", Version = "1.1")]
    public class TMDBPlugin : BasePlugin
    {
        [PluginParameter(Description = "TMDB API Key，可在 themoviedb.org 申请")] string API_KEY { get; set; }

        private readonly IHttpClientFactory _httpClientFactory;
        public TMDBPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider) : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("根据关键词搜索电影。返回匹配的电影列表（标题、简介、上映日期等）。")]
        public async Task<string> SeachMoviesAsync(
            [Description("搜索关键词")] string query,
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/search/movie?api_key={API_KEY}&query={query}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据关键词搜索电视剧。返回匹配的剧集列表。")]
        public async Task<string> SeachTVsAsync(
            [Description("搜索关键词")] string query,
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/search/tv?api_key={API_KEY}&query={query}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据 IMDB ID 查询电影信息。返回电影详情（标题、评分、类型、简介等）。")]
        public async Task<string> GetMovieByIMBDAsync(
            [Description("IMDB ID，格式如：tt0137523")] string imdb_id,
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN")
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/find/{imdb_id}?api_key={API_KEY}&external_source=IMDb&language={language}");
            return response;
        }

        [KernelFunction]
        [Description("根据 TMDB 电影 ID 获取电影详细信息。")]
        public async Task<string> GetMovieAsync(
            [Description("TMDB 电影 ID")] string id,
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN")
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/{id}?api_key={API_KEY}&language={language}");
            return response;
        }

        [KernelFunction]
        [Description("获取正在影院上映的电影列表。")]
        public async Task<string> GetPlayingMovies(
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/now_playing?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取即将上映的电影列表。")]
        public async Task<string> GetUpcomingMovies(
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/upcoming?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取最受欢迎的电影列表（按热度排序）。")]
        public async Task<string> GetPopularMovies(
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/popular?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取评分最高的电影列表（Top Rated）。")]
        public async Task<string> GetRecommendMovies(
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/top_rated?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据指定电影 ID 获取相关电影推荐。")]
        public async Task<string> GetTopRatedMovies(
            [Description("电影 ID")] string id,
            [Description("语言，可选值：en-US、zh-CN，默认为 zh-CN")] string language = "zh-CN",
            [Description("分页页码，默认为 1")] int page = 1)
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/{id}/recommendations?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }
    }
}
