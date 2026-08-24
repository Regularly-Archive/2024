namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Shell 命令执行器接口
/// </summary>
public interface IShellExecutor
{
    /// <summary>
    /// 执行 Shell 命令
    /// </summary>
    /// <param name="command">要执行的命令</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ShellResult> ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional shell capability for executors that can forward process output while it is produced.
/// BashTool continues to support plain IShellExecutor implementations.
/// </summary>
public interface IStreamingShellExecutor : IShellExecutor
{
    Task<ShellResult> ExecuteStreamingAsync(
        string command,
        string? workingDirectory,
        Func<ToolOutputStream, string, CancellationToken, ValueTask> onOutput,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shell 命令执行结果
/// </summary>
public record ShellResult
{
    /// <summary>
    /// 退出码
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// 标准输出
    /// </summary>
    public string Stdout { get; init; } = "";

    /// <summary>
    /// 标准错误
    /// </summary>
    public string Stderr { get; init; } = "";

    /// <summary>
    /// 是否执行成功
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// 执行耗时
    /// </summary>
    public TimeSpan Duration { get; init; }
}
