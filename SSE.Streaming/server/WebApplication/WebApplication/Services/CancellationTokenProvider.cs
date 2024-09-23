
namespace WebApp.Services
{
    public class CancellationTokenProvider : ICancellationTokenProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public CancellationTokenProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public CancellationToken GetCancellationToken()
        {
            return _httpContextAccessor?.HttpContext?.RequestAborted ?? CancellationToken.None;
        }

        public Task<CancellationToken> GetCancellationTokenAsync()
        {
            var cancellationToken =  _httpContextAccessor?.HttpContext?.RequestAborted ?? CancellationToken.None;
            return Task.FromResult(cancellationToken);
        }
    }
}
