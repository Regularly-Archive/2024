using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using System.IO.Compression;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PostgreSQL.Embedding.Llm.Services;

public interface ISkillService
{
    /// <summary>
    /// 从 ZIP 文件解析 Skill，返回 skill name 和 intro
    /// </summary>
    Task<(string Name, string Intro)> ParseSkillFromZipAsync(Stream zipStream);

    /// <summary>
    /// 导入 Skill 到指定 App 的存储目录
    /// </summary>
    Task<LlmAppSkill> ImportSkillAsync(long appId, Stream zipStream);

    /// <summary>
    /// 删除 Skill（同时删除存储文件）
    /// </summary>
    Task DeleteSkillAsync(long appId, long skillId);

    /// <summary>
    /// 复制 Skill 目录到目标位置
    /// </summary>
    void CopySkillDirectory(string sourcePath, string targetPath);
}

public class SkillService : ISkillService
{
    private readonly IRepository<LlmAppSkill> _skillRepository;

    public SkillService(IRepository<LlmAppSkill> skillRepository)
    {
        _skillRepository = skillRepository;
    }

    public async Task<(string Name, string Intro)> ParseSkillFromZipAsync(Stream zipStream)
    {
        // 解压到临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await ExtractZipAsync(zipStream, tempDir);

            // 查找 SKILL.md 文件
            var skillMdPath = FindSkillManifest(tempDir);
            if (skillMdPath == null)
            {
                throw new InvalidOperationException("ZIP 文件中未找到 SKILL.md 文件");
            }

            // 解析 metadata
            var metadata = ParseSkillMetadata(skillMdPath);
            if (metadata == null || string.IsNullOrEmpty(metadata.Name))
            {
                throw new InvalidOperationException("无法解析 SKILL.md 中的 name 字段");
            }

            return (metadata.Name, metadata.Description ?? string.Empty);
        }
        finally
        {
            // 清理临时目录
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    public async Task<LlmAppSkill> ImportSkillAsync(long appId, Stream zipStream)
    {
        // 将流保存到临时文件，避免流被提前 dispose
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");

        await using (var fileStream = new FileStream(tempZipPath, FileMode.Create))
        {
            await zipStream.CopyToAsync(fileStream);
        }

        try
        {
            // 解压并解析 Skill metadata
            using var zipFileStream = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read);
            await ExtractZipAsync(zipFileStream, tempDir);

            // 查找 SKILL.md 文件
            var skillMdPath = FindSkillManifest(tempDir);
            if (skillMdPath == null)
            {
                throw new InvalidOperationException("ZIP 文件中未找到 SKILL.md 文件");
            }

            var skillPath = Path.GetDirectoryName(skillMdPath);

            // 解析 metadata
            var metadata = ParseSkillMetadata(skillMdPath);
            if (metadata == null || string.IsNullOrEmpty(metadata.Name))
            {
                throw new InvalidOperationException("无法解析 SKILL.md 中的 name 字段");
            }

            var name = metadata.Name;
            var intro = metadata.Description ?? string.Empty;

            // 检查是否已存在同名 Skill
            var exists = await _skillRepository.ExistsAsync(x => x.AppId == appId && x.SkillName == name);
            if (exists)
            {
                throw new InvalidOperationException($"技能 \"{name}\" 已存在，请勿重复导入");
            }

            // SKILL.md 所在目录
            var sourceDir = Path.GetDirectoryName(skillMdPath) ?? throw new InvalidOperationException("无法确定 SKILL.md 所在目录");

            // 目标目录：.insighta/{appId}/.skills
            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".insighta",
                appId.ToString(),
                ".skills",
                Path.GetFileName(skillPath)
            );

            // 确保目标目录存在
            Directory.CreateDirectory(targetDir);

            // 复制整个 skill 目录到 .skills 下
            CopySkillDirectory(sourceDir, targetDir);

            // 保存到数据库
            var skill = new LlmAppSkill
            {
                AppId = appId,
                SkillName = name,
                SkillIntro = intro,
                StoragePath = targetDir
            };

            await _skillRepository.AddAsync(skill);
            return skill;
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    public async Task DeleteSkillAsync(long appId, long skillId)
    {
        var skill = await _skillRepository.GetAsync(skillId);
        if (skill == null || skill.AppId != appId)
        {
            throw new InvalidOperationException("Skill 不存在或不属于该应用");
        }

        // 删除存储目录
        if (!string.IsNullOrEmpty(skill.StoragePath) && Directory.Exists(skill.StoragePath))
        {
            Directory.Delete(skill.StoragePath, true);
        }

        // 删除数据库记录
        await _skillRepository.DeleteAsync(skillId);
    }

    public void CopySkillDirectory(string sourcePath, string targetPath)
    {
        foreach (var dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        }

        foreach (var filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            File.Copy(filePath, filePath.Replace(sourcePath, targetPath), true);
        }
    }

    private async Task ExtractZipAsync(Stream zipStream, string destinationDir)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.Combine(destinationDir, entry.FullName);
            if (entry.Name == "")
            {
                // 是目录
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                using var stream = entry.Open();
                using var fileStream = new FileStream(fullPath, FileMode.Create);
                await stream.CopyToAsync(fileStream);
            }
        }
    }

    private string? FindSkillManifest(string rootDir)
    {
        // 优先查找根目录下的 SKILL.md
        var rootMd = Path.Combine(rootDir, "SKILL.md");
        if (File.Exists(rootMd)) return rootMd;

        // 递归查找
        foreach (var dir in Directory.GetDirectories(rootDir))
        {
            var md = Path.Combine(dir, "SKILL.md");
            if (File.Exists(md)) return md;
        }

        return null;
    }

    /// <summary>
    /// 解析 SKILL.md 文件，提取 name 和 description（从 YAML front matter 中解析）
    /// </summary>
    private LlmSkillMetadataModel ParseSkillMetadata(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var yamlLines = new List<string>();

        bool inYaml = false;
        foreach (var line in lines)
        {
            if (line.Trim() == "---")
            {
                if (!inYaml)
                {
                    inYaml = true;
                    continue;
                }
                else
                {
                    break;
                }
            }
            if (inYaml)
                yamlLines.Add(line);
        }

        if (yamlLines.Count == 0)
            return null;

        var yamlText = string.Join("\n", yamlLines);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

        try
        {
            var yamlObj = deserializer.Deserialize<Dictionary<string, object>>(yamlText);

            return new LlmSkillMetadataModel
            {
                Name = yamlObj.ContainsKey("name") ? yamlObj["name"]?.ToString() : null,
                Description = yamlObj.ContainsKey("description") ? yamlObj["description"]?.ToString() : null,
                SkillManifestPath = filePath
            };
        }
        catch
        {
            return null;
        }
    }
}
