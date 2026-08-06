namespace InsightaAI.Agent.Cli.Models;

/// <summary>
/// 将本地化展示文本与稳定匹配值解耦的选择项。
/// Spectre 的 SelectionPrompt 显示 Label，业务逻辑匹配 Value（枚举），
/// 从而让选项文案可以安全国际化。
/// </summary>
public sealed record MenuChoice<TAction>(TAction Value, string Label)
    where TAction : struct, Enum;
