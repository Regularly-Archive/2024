using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace WebApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly ILogger<ChatController> _logger;
        public ChatController(ILogger<ChatController> logger)
        {
            _logger = logger;
        }

        [HttpGet("streaming")]
        [HttpPost("streaming")]
        public async Task GetStreamingAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = "天之道，损有余而补不足，是故虚胜实，不足胜有余。其意博，其理奥，其趣深，天地之象分，阴阳之候列，变化之由表，死生之兆彰，不谋而遗迹自同";

                HttpContext.Response.ContentType = "text/event-stream";

                foreach (var item in text)
                {
                    var payload = JsonSerializer.Serialize(new { text = item.ToString() });
                    var message = $"data: {payload}\n\n";

                    await HttpContext.Response.WriteAsync(message, Encoding.UTF8, cancellationToken);
                    await HttpContext.Response.Body.FlushAsync(cancellationToken);
                    await Task.Delay(200);
                }

                await HttpContext.Response.WriteAsync("data: [DONE]", cancellationToken);
                await HttpContext.Response.Body.FlushAsync(cancellationToken);
                await HttpContext.Response.CompleteAsync();
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation("Operation is canceled.");
            }
            
        }
    }
}
