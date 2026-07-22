using System.Runtime.CompilerServices;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace InsightaAI.Agent.Skills.Local;

/// <summary>
/// 本地文件系统 Skill 提供者
/// 扫描指定目录下的 SKILL.md 文件
/// </summary>
public class LocalSkillProvider : ISkillProvider
{
    private static readonly IDeserializer FrontmatterDeserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly string _skillsDirectory;

    public string ProviderName => "local";

    /// <summary>
    /// 创建本地 Skill 提供者
    /// </summary>
    /// <param name="skillsDirectory">Skills 目录路径，如 ~/.insighta/.skills</param>
    public LocalSkillProvider(string skillsDirectory)
    {
        _skillsDirectory = skillsDirectory;
    }

    /// <summary>
    /// 使用默认路径创建（~/.insighta/.skills）
    /// </summary>
    public LocalSkillProvider() : this(GetDefaultSkillsDirectory()) { }

    public async IAsyncEnumerable<SkillMetadata> ListSkillsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_skillsDirectory))
        {
            yield break;
        }

        foreach (var skillDir in Directory.GetDirectories(_skillsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skillMdPath = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(skillMdPath))
            {
                continue;
            }

            SkillMetadata? metadata = null;
            try
            {
                metadata = await ParseMetadataAsync(skillMdPath, cancellationToken);
            }
            catch
            {
                // 跳过解析失败的 skill
                continue;
            }

            if (metadata != null)
            {
                yield return metadata;
            }
        }
    }

    public async Task<ISkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default)
    {
        var skillDir = Path.Combine(_skillsDirectory, skillName);
        var skillMdPath = Path.Combine(skillDir, "SKILL.md");

        if (!File.Exists(skillMdPath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(skillMdPath, cancellationToken);
        var (metadata, instructions) = ParseSkillMd(content);

        // 验证 name 与目录名一致
        if (metadata.Name != skillName)
        {
            return null;
        }

        return new LocalSkill(metadata, instructions, skillDir);
    }

    /// <summary>
    /// 从内容解析元数据（静态方法，供 CLI 使用）
    /// </summary>
    public static SkillMetadata? ParseMetadata(string content, string? expectedDirName = null)
    {
        try
        {
            var (metadata, _) = ParseSkillMd(content);

            // 如果指定了目录名，验证一致性
            if (expectedDirName != null)
            {
                var dirName = Path.GetFileName(expectedDirName);
                if (metadata.Name != dirName)
                {
                    return null;
                }
            }

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 只解析元数据（不读取完整内容）
    /// </summary>
    private async Task<SkillMetadata?> ParseMetadataAsync(string skillMdPath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(skillMdPath, cancellationToken);
        var (metadata, _) = ParseSkillMd(content);

        return metadata;
    }

    /// <summary>
    /// 解析 SKILL.md 文件，提取 frontmatter 和 body
    /// </summary>
    private static (SkillMetadata Metadata, string Instructions) ParseSkillMd(string content)
    {
        var lines = content.Split('\n');

        // 提取 YAML frontmatter
        var yamlLines = new List<string>();
        var bodyStartIndex = 0;
        bool inFrontmatter = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (i == 0 && line.Trim() == "---")
            {
                inFrontmatter = true;
                continue;
            }

            if (inFrontmatter && line.Trim() == "---")
            {
                bodyStartIndex = i + 1;
                break;
            }

            if (inFrontmatter)
            {
                yamlLines.Add(line);
            }
        }

        // 解析 YAML
        var yamlContent = string.Join("\n", yamlLines);
        var metadata = ParseYamlMetadata(yamlContent);

        // 提取 body
        var bodyLines = lines.Skip(bodyStartIndex);
        var instructions = string.Join("\n", bodyLines).Trim();

        return (metadata, instructions);
    }

    /// <summary>
    /// 使用标准 YAML 解析器解析 frontmatter
    /// </summary>
    private static SkillMetadata ParseYamlMetadata(string yaml)
    {
        var frontmatter = FrontmatterDeserializer.Deserialize<SkillFrontmatter>(yaml)
            ?? throw new InvalidOperationException("SKILL.md frontmatter is empty");

        if (string.IsNullOrWhiteSpace(frontmatter.Name))
        {
            throw new InvalidOperationException("SKILL.md frontmatter must contain 'name' field");
        }

        if (string.IsNullOrWhiteSpace(frontmatter.Description))
        {
            throw new InvalidOperationException("SKILL.md frontmatter must contain 'description' field");
        }

        return new SkillMetadata
        {
            Name = frontmatter.Name,
            Description = frontmatter.Description,
            AllowedTools = FormatAllowedTools(frontmatter.AllowedTools)
        };
    }

    private static string? FormatAllowedTools(object? allowedTools)
    {
        return allowedTools switch
        {
            null => null,
            string value => value,
            IEnumerable<object> values => string.Join(" ", values),
            _ => allowedTools.ToString()
        };
    }

    private static string GetDefaultSkillsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".agents", "skills");
    }

    private sealed class SkillFrontmatter
    {
        public string? Name { get; init; }

        public string? Description { get; init; }

        public object? AllowedTools { get; init; }
    }
}
