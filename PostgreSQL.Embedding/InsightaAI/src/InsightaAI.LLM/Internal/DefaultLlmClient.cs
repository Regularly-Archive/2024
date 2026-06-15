using System.Runtime.CompilerServices;
using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Internal;

/// <summary>
/// 默认 LLM 客户端实现
/// </summary>
internal class DefaultLlmClient : ILlmClient
{
    private readonly IProviderAdapter _adapter;
    private readonly ProviderConfig _config;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public string ProviderName => _adapter.Name;
    public bool SupportsReasoning => _adapter.SupportsReasoning;

    public DefaultLlmClient(IProviderAdapter adapter, ProviderConfig config, HttpClient? httpClient = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
    }

    public LlmStream Stream(LlmRequest request)
    {
        var events = StreamEventsAsync(request);
        return new LlmStreamImpl(events);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        // 使用流式 API 并收集结果
        var stream = Stream(request with { Stream = true });
        return await stream.GetResponseAsync(cancellationToken);
    }

    private async IAsyncEnumerable<StreamEvent> StreamEventsAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 创建 HTTP 请求
        var httpRequest = _adapter.CreateRequest(request, _config, stream: true);

        // 发送请求
        HttpResponseMessage? response = null;
        Exception? requestError = null;

        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception ex)
        {
            requestError = ex;
        }

        // 处理请求错误
        if (requestError != null)
        {
            yield return new ErrorEvent
            {
                Error = requestError,
                Recoverable = false
            };
            yield return new DoneEvent { Reason = DoneReason.Error };
            yield break;
        }

        // 处理 HTTP 错误
        if (response == null || !response.IsSuccessStatusCode)
        {
            var errorBody = response != null
                ? await response.Content.ReadAsStringAsync(cancellationToken)
                : "No response received";

            yield return new ErrorEvent
            {
                Error = new HttpRequestException(
                    $"API request failed with status {response?.StatusCode}: {errorBody}"),
                Recoverable = response?.StatusCode is
                    System.Net.HttpStatusCode.TooManyRequests or
                    System.Net.HttpStatusCode.ServiceUnavailable
            };
            yield return new DoneEvent { Reason = DoneReason.Error };
            yield break;
        }

        // 发送开始事件
        yield return new StreamStartEvent
        {
            Model = request.Model,
            Provider = _adapter.Name
        };

        // 解析 SSE 流
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventType = null;
        var dataBuffer = new System.Text.StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            Exception? readError = null;

            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                line = null;
                readError = new OperationCanceledException();
            }
            catch (Exception ex)
            {
                line = null;
                readError = ex;
            }

            // 处理读取错误
            if (readError is OperationCanceledException)
            {
                yield return new DoneEvent { Reason = DoneReason.Aborted };
                yield break;
            }
            if (readError != null)
            {
                yield return new DoneEvent { Reason = DoneReason.Error };
                yield break;
            }

            if (line == null)
            {
                // 流结束
                break;
            }

            // 解析 SSE 格式
            if (line.StartsWith("event: "))
            {
                eventType = line["event: ".Length..].Trim();
            }
            else if (line.StartsWith("data: "))
            {
                dataBuffer.AppendLine(line["data: ".Length..]);
            }
            else if (string.IsNullOrEmpty(line) && dataBuffer.Length > 0)
            {
                // 空行表示事件结束，处理数据
                var data = dataBuffer.ToString().Trim();
                dataBuffer.Clear();

                if (data == "[DONE]")
                {
                    yield return new DoneEvent { Reason = DoneReason.Complete };
                    yield break;
                }

                StreamEvent? parsedEvent = null;
                try
                {
                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(data);
                    parsedEvent = _adapter.ParseStreamEvent(eventType ?? "message", jsonElement);
                }
                catch (JsonException)
                {
                    // 跳过无法解析的事件
                    eventType = null;
                    continue;
                }

                if (parsedEvent != null)
                {
                    yield return parsedEvent;

                    if (parsedEvent is DoneEvent)
                    {
                        yield break;
                    }
                }

                eventType = null;
            }
        }

        // 如果循环正常结束但没有 DoneEvent
        yield return new DoneEvent { Reason = DoneReason.Complete };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
