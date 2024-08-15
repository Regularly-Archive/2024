using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebApp.Services;

namespace WebApp.Hubs
{
    public class ChatHub : Hub
    {
        private ILogger<ChatHub> _logger;
        private ITextGenerator _textGenerator;
        private static Dictionary<string, CancellationTokenSource> _cancellationTokens = new();

        public ChatHub(ILogger<ChatHub> logger, ITextGenerator textGenerator)
        {
            _logger = logger;
            _textGenerator = textGenerator;
        }

        public async Task Generate(string requestId, string prompt)
        {
            var cts = new CancellationTokenSource();
            _cancellationTokens[requestId] = cts;

            try
            {
                cts.Token.ThrowIfCancellationRequested();

                await foreach (var item in _textGenerator.Generate(prompt, cts.Token))
                {
                    await Clients.Caller.SendAsync("ReceiveChunks", JsonSerializer.Serialize(new { text = item }), requestId, cts.Token);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation("The task is canceled.");
            }

        }

        public async Task Cancel(string requestId)
        {
            if (_cancellationTokens.TryGetValue(requestId, out var cts))
            {
                await cts.CancelAsync();
                await Clients.Caller.SendAsync("GenerationCancelled", requestId, cts.Token);

                _cancellationTokens.Remove(requestId);
            }
        }
    }
}
