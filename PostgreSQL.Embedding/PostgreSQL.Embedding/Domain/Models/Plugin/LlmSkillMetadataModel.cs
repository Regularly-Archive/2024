namespace PostgreSQL.Embedding.Domain.Models.Plugin;

public class LlmSkillMetadataModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string SkillManifestPath { get; set; }
}

public record SkillFileIndex
{
    public List<string> Scripts { get; init; } = new();
    public List<string> References { get; init; } = new();
    public List<string> Assets { get; init; } = new();
}
