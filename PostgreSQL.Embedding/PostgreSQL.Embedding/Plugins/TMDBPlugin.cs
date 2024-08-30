
using Elastic.Clients.Elasticsearch.Analysis;
using Elastic.Transport;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "TMDB 影视数据库插件")]
    public class TMDBPlugin
    {
        private const string API_KEY = "0f79586eb9d92afa2b7266f7928b055c";

        private readonly IHttpClientFactory _httpClientFactory;
        public TMDBPlugin(IHttpClientFactory httpClientFactory) 
        { 
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("根据电影名称搜索电影")]
        public async Task<string> SeachMoviesAsync([Description("关键词")] string query, [Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/search/movie?api_key={API_KEY}&query={query}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据电视剧名称搜索电视剧")]
        public async Task<string> SeachTVsAsync([Description("关键词")] string query, [Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/search/tv?api_key={API_KEY}&query={query}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据 IMDB ID 获取电影信息")]
        public async Task<string> GetMovieByIMBDAsync([Description("IMDB ID")] string imdb_id, [Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/find/{imdb_id}?api_key={API_KEY}&external_source=IMDb&language={language}");
            return response;
        }

        [KernelFunction]
        [Description("获取指定电影")]
        public async Task<string> GetMovieAsync([Description("影片Id")] string id, [Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN")
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/{id}?api_key={API_KEY}&language={language}");
            return response;
        }

        [KernelFunction]
        [Description("获取正在上映的电影")]
        public async Task<string> GetPlayingMovies([Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/now_playing?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取新上映的电影")]
        public async Task<string> GetUpcomingMovies([Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/upcoming?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取最受欢迎的电影")]
        public async Task<string> GetPopularMovies([Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/popular?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("获取评分最高的电影")]
        public async Task<string> GetRecommendMovies([Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/top_rated?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }

        [KernelFunction]
        [Description("根据指定电影推荐相关电影")]
        public async Task<string> GetTopRatedMovies([Description("影片Id")] string id, [Description("语言，可取值为: en-US, zh-CN")] string language = "zh-CN", [Description("当前页数")] int page = 1)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetStringAsync($"https://api.themoviedb.org/3/movie/{id}/recommendations?api_key={API_KEY}&language={language}&page={page}");
            return response;
        }
    }
}
