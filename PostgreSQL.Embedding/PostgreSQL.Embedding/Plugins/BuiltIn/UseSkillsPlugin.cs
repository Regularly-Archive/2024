using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "动态加载和管理 Skills（技能包）的插件。支持列出可用的技能、读取技能清单、列出技能文件、读取文件内容以及执行脚本。", Version = "2.0")]
public class UseSkillsPlugin : BasePlugin
{
    private readonly IRepository<LlmAppSkill> _skillRepository;
    private readonly SandboxService? _sandboxService;

    public UseSkillsPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _skillRepository = serviceProvider.GetRequiredService<IRepository<LlmAppSkill>>();
        _sandboxService = serviceProvider.GetService<SandboxService>();
    }

    /// <summary>
    /// 从数据库获取 Skill 记录
    /// </summary>
    private async Task<LlmAppSkill?> GetSkillAsync(long appId, string skillName)
    {
        return await _skillRepository.FindAsync(x => x.AppId == appId && x.SkillName == skillName);
    }

    /// <summary>
    /// 在沙箱中执行命令
    /// </summary>
    private async Task<CommandResult> ExecuteInSandboxAsync(Kernel kernel, string command)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
        var sessionId = Path.GetFileName(sandboxContext.SessionDir);
        var volumeMappings = sandboxContext.GetVolumeMappings();

        var session = await _sandboxService!.GetOrCreateSessionAsync(sessionId, volumeMappings);
        return await _sandboxService.ExecuteAsync(sessionId, command);
    }

    /// <summary>
    /// 获取 AppId
    /// </summary>
    private long GetAppId(Kernel kernel)
    {
        return kernel.GetAgentExecutionContext().GetAppId();
    }

    [KernelFunction]
    [Description("列出当前应用所有可用的 Skills。每个 Skill 必须包含 SKILL.md 清单文件。")]
    public async Task<IEnumerable<LlmSkillMetadataModel>> ListSkills(
        Kernel kernel)
    {
        var appId = GetAppId(kernel);
        var skills = await _skillRepository.FindListAsync(x => x.AppId == appId);

        var skillList = new List<LlmSkillMetadataModel>();
        foreach (var skill in skills)
        {
            var skillMdPath = Path.Combine(skill.StoragePath, "SKILL.md");
            if (File.Exists(skillMdPath))
            {
                var metadata = ParseSkillMetadata(skillMdPath);
                if (metadata != null)
                {
                    metadata.SkillManifestPath = ResolveSkillManifestPath(skill.StoragePath);
                    skillList.Add(metadata);
                }
            }
        }

        return skillList;
    }

    [KernelFunction]
    [Description("读取指定 Skill 的 SKILL.md 清单文件内容，包含技能的名称、描述等信息")]
    public async Task<string> GetSkillManifest(
        Kernel kernel,
        [Description("Skill 名称，从 ListSkills 获取")] string skill_name)
    {
        var appId = GetAppId(kernel);
        var skill = await GetSkillAsync(appId, skill_name);

        if (skill == null)
            throw new FileNotFoundException($"Skill '{skill_name}' not found in database");

        var skillManifestPath = Path.Combine(skill.StoragePath, "SKILL.md");
        if (!File.Exists(skillManifestPath))
            throw new Exception("Unable to locate skill manifest file SKILL.md");

        return File.ReadAllText(skillManifestPath);
    }

    [KernelFunction]
    [Description("列出 Skill 目录下的所有文件，按类型分组（scripts 脚本、references 引用、assets 资源）")]
    public async Task<string> ListSkillFiles(
        Kernel kernel,
        [Description("Skill 名称，从 ListSkills 获取")] string skill_name)
    {
        var appId = GetAppId(kernel);
        var skill = await GetSkillAsync(appId, skill_name);

        if (skill == null)
            throw new FileNotFoundException($"Skill '{skill_name}' not found in database");

        var skillPathInSandbox = ResolveSkillRootPath(skill.StoragePath);

        // 在沙箱中执行 ls -la
        var result = await ExecuteInSandboxAsync(kernel, $"ls -la \"{skillPathInSandbox}\"");

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to list skill files: {result.Stderr}");

        return result.Stdout;
    }

    [KernelFunction]
    [Description("读取 Skill 目录下的文本文件内容")]
    public async Task<string> ReadSkillFile(
        Kernel kernel,
        [Description("Skill 名称，从 ListSkills 获取")] string skill_name,
        [Description("文件相对路径，从 ListSkillFiles 获取")] string fileRelativePath)
    {
        var appId = GetAppId(kernel);
        var skill = await GetSkillAsync(appId, skill_name);

        if (skill == null)
            throw new FileNotFoundException($"Skill '{skill_name}' not found in database");

        var skillPathInSandbox = ResolveSkillRootPath(skill.StoragePath);
        var filePathInSandbox = $"{skillPathInSandbox}/{fileRelativePath}";

        var result = await ExecuteInSandboxAsync(kernel, $"cat \"{filePathInSandbox}\"");

        if (result.ExitCode != 0)
            throw new FileNotFoundException($"File not found: {fileRelativePath}");

        return result.Stdout;
    }

    [KernelFunction]
    [Description("执行 Skill 目录下 scripts 文件夹中的脚本文件。支持 PowerShell、Python、Bash 脚本。")]
    public async Task<string> RunSkillScript(
        [Description("执行 scripts/ 目录下的脚本或者命令")] string command,
        Kernel kernel)
    {
        var result = await ExecuteInSandboxAsync(kernel, command);
        return result.ExitCode == 0 ? result.Stdout : result.Stderr;
    }

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

            var metadata = new LlmSkillMetadataModel
            {
                Name = yamlObj.ContainsKey("name") ? yamlObj["name"]?.ToString() : null,
                Description = yamlObj.ContainsKey("description") ? yamlObj["description"]?.ToString() : null
            };

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveSkillRootPath(string storagePath)
    {
        var skillName = Path.GetFileName(storagePath);
        return $"/sandbox/.skills/{skillName}";
    }

    private string ResolveSkillManifestPath(string storagePath)
    {
        var skillName = Path.GetFileName(storagePath);
        return $"/sandbox/.skills/{skillName}/SKILL.md";
    }
}
