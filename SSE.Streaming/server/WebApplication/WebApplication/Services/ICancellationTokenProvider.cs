namespace WebApp.Services
{
    public interface ICancellationTokenProvider
    {
        CancellationToken GetCancellationToken();
        Task<CancellationToken> GetCancellationTokenAsync();
    }
}
