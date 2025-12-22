namespace PostgreSQL.Embedding.Common.Models.Plugin;

public class ScriptExecutionResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; }
    public string StandardError { get; set; }
    public string Hint { get; set; }
    public bool TimedOut { get; set; }
}
