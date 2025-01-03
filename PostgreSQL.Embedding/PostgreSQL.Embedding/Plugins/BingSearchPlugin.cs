﻿using AngleSharp;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Utils;
using SqlSugar;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "微软必应搜索插件")]
    public class BingSearchPlugin : BasePlugin, ISearchEngine
    {
        private const string SELECTOR_TAG_MAIN = "main";
        private const string SELECTOR_TAG_LINK = "a";
        private const string SELECTOR_TAG_ITEM = ".b_algo";
        private const string SELECTOR_TAG_ITEM_TITLE = "h2";
        private const string SELECTOR_TAG_HREF = "href";
        private const string SELECTOR_TAG_ITEM_DESC = ".b_caption";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public BingSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("使用关键词进行检索")]
        public async Task<string> SearchAsync([Description("关键词")] string keyword)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://bing.com/");

            try
            {
                var response = await httpClient.GetAsync($"https://bing.com/search?q={keyword}");
                response.EnsureSuccessStatusCode();


                var html = await response.Content.ReadAsStringAsync();
                var searchResult = await ExtractSearchResults(keyword, html);
                await SendArtifacts(searchResult);
                return JsonConvert.SerializeObject(searchResult);
            }
            catch (HttpRequestException ex)
            {
                var html = await new HeadlessBrowser().FetchAsync($"https://bing.com/search?q={keyword}");
                var searchResult = await ExtractSearchResults(keyword, html);
                await SendArtifacts(searchResult);
                return JsonConvert.SerializeObject(searchResult);
            }
        }

        private async Task<SearchResult> ExtractSearchResults(string query, string html)
        {
            var seachResult = new SearchResult() { Query = query };

            var config = Configuration.Default.WithDefaultLoader();
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(request => request.Content(html));

            var eleMain = document.QuerySelector(SELECTOR_TAG_MAIN);
            if (eleMain == null) return seachResult;

            var eleItems = eleMain.QuerySelectorAll(SELECTOR_TAG_ITEM);
            if (eleItems == null || !eleItems.Any()) return seachResult;

            seachResult.Entries = eleItems.Select(x =>
            {
                var eleTitle = x.QuerySelector(SELECTOR_TAG_ITEM_TITLE);
                return new Entry()
                {
                    Title = eleTitle.TextContent,
                    Url = eleTitle.QuerySelector(SELECTOR_TAG_LINK).Attributes[SELECTOR_TAG_HREF].Value,
                    Description = x.QuerySelector(SELECTOR_TAG_ITEM_DESC)?.TextContent ?? string.Empty
                };
            })
            .ToList();

            return seachResult;
        }

        private async Task SendArtifacts(SearchResult searchResult)
        {
            if (searchResult == null || !searchResult.Entries.Any()) return;

            var artifact = new LlmArtifactResponseModel("搜索结果", ArtifactType.Search);
            var payloads = searchResult.Entries.Select(x => new
            {
                link = x.Url,
                title = x.Title,
                description = x.Description
            });
            artifact.SetData(payloads);
            await EmitArtifactsAsync(artifact);
        }
    }

    public interface ISearchEngine
    {
        Task<string> SearchAsync(string keyword);
    }
}
