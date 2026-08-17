namespace InsightaAI.Agent.Security;

/// <summary>工具调用拒绝规则</summary>
public sealed record DenyRule(string Pattern, DenyMatchMode Mode);

/// <summary>拒绝规则的匹配方式</summary>
public enum DenyMatchMode
{
    Exact,
    Glob,
    Regex
}
