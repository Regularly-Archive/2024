using Newtonsoft.Json;
using System.Text;

namespace PostgreSQL.Embedding.Common
{
    public class SSEEmitter
    {
        private const string NEW_LINE = "\n\n";
        private readonly HttpContext _httpContext;

        public SSEEmitter(HttpContext httpContext)
        {
            _httpContext = httpContext;
            if (!_httpContext.Response.HasStarted)
            {
                _httpContext.Response.ContentType = "text/event-stream";
                _httpContext.Response.Headers["Cache-Control"] = "no-cache";
                _httpContext.Response.Headers["Connection"] = "keep-alive";
            }
        }

        public async Task EmitAsync<TData>(TData data, CancellationToken cancellationToken = default) where TData : class
        {
            // Todo: 需要考虑取消场景
            string message = $"data: {JsonConvert.SerializeObject(data)}{NEW_LINE}";
            await _httpContext.Response.WriteAsync(message, Encoding.UTF8, cancellationToken);
            await _httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        public async Task EmitAsync(string data, CancellationToken cancellationToken = default)
        {
            // Todo: 需要考虑取消场景
            string message = $"data: {data}{NEW_LINE}";
            await _httpContext.Response.WriteAsync(message, Encoding.UTF8, cancellationToken);
            await _httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (!_httpContext.Response.HasStarted) return;
            await _httpContext.Response.CompleteAsync();
        }
    }
}
