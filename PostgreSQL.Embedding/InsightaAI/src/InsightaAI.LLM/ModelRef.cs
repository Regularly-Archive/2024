namespace InsightaAI.LLM;

/// <summary>
/// 模型引用，格式：provider/model（如 "deepseek/deepseek-v4-flash"）
/// </summary>
public readonly struct ModelRef : IEquatable<ModelRef>
{
    /// <summary>Provider 名称（如 "deepseek"）</summary>
    public string Provider { get; }

    /// <summary>模型 ID（如 "deepseek-v4-flash"）</summary>
    public string ModelId { get; }

    /// <summary>创建模型引用</summary>
    public ModelRef(string provider, string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        Provider = provider;
        ModelId = modelId;
    }

    /// <summary>从 "provider/model" 格式字符串解析</summary>
    /// <exception cref="ArgumentException">格式无效时抛出</exception>
    public static ModelRef Parse(string refStr)
    {
        if (!TryParse(refStr, out var result))
            throw new ArgumentException(
                $"Invalid model reference '{refStr}'. Expected format: 'provider/model' (e.g., 'openai/gpt-4o')");
        return result;
    }

    /// <summary>尝试从 "provider/model" 格式字符串解析</summary>
    public static bool TryParse(string refStr, out ModelRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(refStr))
            return false;

        var parts = refStr.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return false;

        result = new ModelRef(parts[0], parts[1]);
        return true;
    }

    /// <summary>返回 "provider/model" 格式</summary>
    public override string ToString() => $"{Provider}/{ModelId}";

    public bool Equals(ModelRef other) =>
        string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ModelId, other.ModelId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ModelRef other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Provider.ToLowerInvariant(), ModelId.ToLowerInvariant());

    public static bool operator ==(ModelRef left, ModelRef right) => left.Equals(right);
    public static bool operator !=(ModelRef left, ModelRef right) => !left.Equals(right);
}
