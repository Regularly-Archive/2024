namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// Chat 用例入口。
/// 命令行适配层只负责绑定参数，具体聊天流程由该接口承载。
/// </summary>
public interface IChatApplication
{
    Task<int> RunAsync(string? sessionId, bool continueLast = false);
}
