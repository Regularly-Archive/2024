namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Reads environment values available to the current Agent.
/// </summary>
public interface IEnvironmentVariableReader
{
    string? Get(string name);
}

/// <summary>
/// Reads values from the current process environment.
/// </summary>
public sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Environment.GetEnvironmentVariable(name);
    }
}
