using System.Reflection;

namespace InsightaAI.Agent.Prompts;

/// <summary>
/// 加载嵌入式提示词模板
/// </summary>
internal static class PromptLoader
{
    private static readonly Assembly Assembly = typeof(PromptLoader).Assembly;
    private static readonly string Namespace = typeof(PromptLoader).Namespace!;

    /// <summary>
    /// 加载嵌入资源中的提示词模板
    /// </summary>
    /// <param name="name">文件名（不含扩展名），如 "anchored-summary"</param>
    /// <returns>模板内容</returns>
    public static async Task<string> LoadAsync(string name)
    {
        var resourceName = $"{Namespace}.{name}.txt";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// 同步加载嵌入资源中的提示词模板
    /// </summary>
    public static string Load(string name)
    {
        var resourceName = $"{Namespace}.{name}.txt";
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
