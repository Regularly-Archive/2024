namespace PostgreSQL.Embedding.Handlers
{
    public class OpenAIProxyHandler : HttpClientHandler
    {
        private readonly string OPENAI_BASE_URL = "api.openai.com";
        private readonly IConfiguration _configuration;
        public OpenAIProxyHandler(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null && request.RequestUri.Host.Equals(OPENAI_BASE_URL, StringComparison.OrdinalIgnoreCase))
            {
                var proxyUrl = _configuration["OpenAIConfig:ProxuUrl"];
                request.RequestUri = new Uri($"{proxyUrl}/{request.RequestUri.PathAndQuery}");
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
