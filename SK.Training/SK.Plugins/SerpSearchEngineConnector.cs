using Microsoft.SemanticKernel.Plugins.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace SK.Plugins
{
#pragma warning disable SKEXP0050
    public class SerpSearchEngineConnector : IWebSearchEngineConnector
#pragma warning restore SKEXP0050
    {
        private readonly string _apiKey;
        private readonly string _engine;

        public SerpSearchEngineConnector(string apiKey, string engine)
        {
            _apiKey = apiKey;
            _engine = engine;
        }

        public async Task<IEnumerable<string>> SearchAsync(string query, int count = 1, int offset = 0, CancellationToken cancellationToken = default)
        {
            try
            {
                var q = UrlEncoder.Default.Encode(query);
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync($"https://serpapi.com/search.json?engine={_engine}&q={q}&location=China&api_key={_apiKey}");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var jObject = JObject.Parse(json);


                var titles = new List<string>();
                var organicResults = (JArray)jObject["organic_results"]!;
                foreach (JObject result in organicResults!)
                {
                    titles.Add(result.Value<string>("title")!);
                }

                return titles;
            }
            catch
            {
                return Enumerable.Empty<string>();
            }

        }
    }
}
