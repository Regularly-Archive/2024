using Microsoft.Extensions.Options;
using PostgreSQL.Embedding.Common;

namespace PostgreSQL.Embedding.LlmServices
{
    public class OpenAIChatHandler : HttpClientHandler
    {
        private readonly IOptions<LlmConfig> _llmConfigOptions;
        private readonly LlmServiceProvider _llmServiceProvider;
        public OpenAIChatHandler(IOptions<LlmConfig> llmConfigOptions, LlmServiceProvider llmServiceProvider)
        {
            _llmConfigOptions = llmConfigOptions;
            _llmServiceProvider = llmServiceProvider;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUrl = string.Format(_llmConfigOptions.Value?.ChatEndpoint, _llmServiceProvider.ToString());
            request.RequestUri = new Uri(requestUrl);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
