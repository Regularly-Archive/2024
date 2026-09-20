namespace InsightaAI.Agent.Vision;

/// <summary>
/// 视觉分析服务：加载图片并调用视觉模型，返回文字描述。
/// </summary>
public interface IVisionService
{
    /// <summary>
    /// 分析图片并返回描述。
    /// </summary>
    /// <param name="source">本地文件路径或 http(s) URL</param>
    /// <param name="prompt">给视觉模型的指令（如具体问题）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>视觉模型生成的文字描述</returns>
    Task<string> AnalyzeAsync(string source, string prompt, CancellationToken cancellationToken = default);
}
