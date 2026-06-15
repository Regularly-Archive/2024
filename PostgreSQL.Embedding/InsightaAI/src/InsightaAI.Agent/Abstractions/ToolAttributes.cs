namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// 标记方法为工具
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ToolAttribute : Attribute
{
    /// <summary>工具名称</summary>
    public string Name { get; }

    /// <summary>工具描述</summary>
    public string Description { get; }

    public ToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

/// <summary>
/// 标记方法参数为工具参数
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class ToolParameterAttribute : Attribute
{
    /// <summary>参数描述</summary>
    public string Description { get; }

    /// <summary>是否必填</summary>
    public bool Required { get; set; } = true;

    public ToolParameterAttribute(string description)
    {
        Description = description;
    }
}
