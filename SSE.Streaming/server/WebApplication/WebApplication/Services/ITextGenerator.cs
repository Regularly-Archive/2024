namespace WebApp.Services
{
    public interface ITextGenerator
    {
        public IAsyncEnumerable<string> Generate(string prompt, CancellationToken cancellationToken);
    }
}
