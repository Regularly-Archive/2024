using DocumentFormat.OpenXml.Bibliography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using MongoDB.Driver;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "网易云音乐插件。提供歌曲搜索和在线播放功能，可根据歌手名和歌曲名搜索并返回歌曲信息。", Version = "1.1")]
    public class CloudMusicPlugin : BasePlugin
    {
        private const string SEARCH_URL = "http://music.163.com/api/search/get/web?csrf_token=hlpretag=&hlposttag=&s={0}&type=1&offset=0&total=true&limit=2";
        private const string MUSIC_URL = "http://music.163.com/song/media/outer/url?id={0}";

        private const string NOT_FOUND = "抱歉，没有为您找到相关歌曲";

        public CloudMusicPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        [KernelFunction]
        [Description("搜索网易云音乐歌曲。可通过歌手名称、歌曲名称或两者组合进行搜索，返回匹配的歌曲信息（ID、名称、艺术家、专辑等）。")]
        public async Task<string> SearchMusicAsync(
            [Description("歌手名称（可选）")] string artistName = "",
            [Description("歌曲名称（可选，优先使用）")] string songName = "")
        {
            var handler = new HttpClientHandler() { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip };
            using var httpClient = new HttpClient(handler);

            var keyword = songName ?? artistName;
            var searchResult = await SearchByKeyword(httpClient, keyword);
            if (searchResult!.code != 200 || searchResult.result.songs.Length == 0)
                return JsonConvert.SerializeObject(Enumerable.Empty<Song>());

            var song = FilterSong(searchResult.result, artistName, songName);
            return JsonConvert.SerializeObject(new List<Song>() { song });
        }

        [KernelFunction]
        [Description("获取歌曲下载链接")]
        public async Task<string> GetDownloadLink(long songId)
        {
            var handler = new HttpClientHandler() { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip };
            using var httpClient = new HttpClient(handler);
            var response = await httpClient.GetAsync(string.Format(MUSIC_URL, songId));
            if (response.StatusCode == HttpStatusCode.Redirect)
            {
                var location = response.Headers.Location;
                if (location!.AbsoluteUri == "http://music.163.com/404")
                    return NOT_FOUND;

                return location.AbsoluteUri;
            }

            return NOT_FOUND;
        }

        /// <summary>
        /// 根据关键词检索歌曲
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="keyword"></param>
        /// <returns></returns>
        private async Task<MusicSearchApiResult> SearchByKeyword(HttpClient httpClient, string keyword)
        {
            var response = await httpClient.GetAsync(string.Format(SEARCH_URL, UrlEncoder.Default.Encode(keyword)));
            response.EnsureSuccessStatusCode();

            var responseConent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<MusicSearchApiResult>(responseConent);
        }

        /// <summary>
        /// 按艺术家名称和歌曲名称筛选歌曲
        /// </summary>
        /// <param name="result"></param>
        /// <param name="artistName"></param>
        /// <param name="songName"></param>
        /// <returns></returns>
        private Song FilterSong(SongsResult songsResult, string artistName, string songName)
        {
            Song song = null;

            // 当指定艺术家时，优先选择指定艺术家的歌曲
            if (!string.IsNullOrEmpty(artistName))
                song = songsResult.songs.FirstOrDefault(x => x.artists[0].name == artistName);

            // 否则随机返回一首歌曲
            if (song == null || string.IsNullOrEmpty(artistName))
            {
                var random = new Random();
                var idx = random.Next(0, songsResult.songs.Length);
                song = songsResult.songs[idx];
            }

            return song;
        }

        #region Models
        class MusicSearchApiResult
        {
            public SongsResult result { get; set; }
            public int code { get; set; }
        }

        class SongsResult
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
        #endregion
    }
}
