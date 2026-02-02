using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "动态加载和管理 Skills（技能包）的插件。支持列出可用的技能、读取技能清单、列出技能文件、读取文件内容以及执行脚本。", Version = "1.3")]
public class UseSkillsPlugin : BasePlugin
{
    public UseSkillsPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
    {

    }

    [KernelFunction]
    [Description("列出指定目录下所有可用的 Skills。每个 Skill 必须包含 SKILL.md 清单文件。")]
    public IEnumerable<LlmSkillMetadataModel> ListSkills(
        [Description("Skills 根目录的绝对路径，如：D:\\skills")]string skillsRootFolder)
    {
        if (!Directory.Exists(skillsRootFolder))
            return Enumerable.Empty<LlmSkillMetadataModel>();

        var skillList = new List<LlmSkillMetadataModel>();
        foreach (var dir in Directory.GetDirectories(skillsRootFolder))
        {
            var mdFile = Path.Combine(dir, "SKILL.md");
            if (File.Exists(mdFile))
            {
                var metadata = ParseSkillMetadata(mdFile);
                if (metadata != null)
                {
                    metadata.SkillManifestPath = mdFile;
                    skillList.Add(metadata);
                }
            }
        }

        return skillList;
    }

    [KernelFunction]
    [Description("读取指定 Skill 的 SKILL.md 清单文件内容，包含技能的名称、描述等信息")]
    public string GetSkillManifest(
        [Description("SKILL.md 文件的绝对路径，可从 ListSkills 获取")] string skillManifestPath)
    {
        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        if (!File.Exists(skillManifestPath))
            throw new FileNotFoundException($"The sspecific path not found: {skillManifestPath}");

        return File.ReadAllText(skillManifestPath);
    }

    [KernelFunction]
    [Description("列出 Skill 目录下的所有文件，按类型分组（scripts 脚本、references 引用、assets 资源）")]
    public SkillFileIndex ListSkillFiles(
        [Description("SKILL.md 文件的绝对路径")] string skillManifestPath)
    {
        if (string.IsNullOrWhiteSpace(skillManifestPath))
            throw new ArgumentException("The path for skill manifest file SKILL.md is required.", nameof(skillManifestPath));

        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        if (!File.Exists(skillManifestPath))
            throw new FileNotFoundException($"The specific path not found: {skillManifestPath}");

        var skillFolder = Path.GetDirectoryName(skillManifestPath);

        return new SkillFileIndex
        {
            Scripts = CollectFiles(Path.Combine(skillFolder, "scripts"), skillFolder),
            References = CollectFiles(Path.Combine(skillFolder, "references"), skillFolder),
            Assets = CollectFiles(Path.Combine(skillFolder, "assets"), skillFolder),
        };
    }
   
    [KernelFunction]
    [Description("读取 Skill 目录下的文本文件内容")]
    public string ReadSkillFile(
        [Description("SKILL.md 文件的绝对路径")] string skillManifestPath,
        [Description("文件相对路径，从 ListSkillFiles 获取")]string fileRelativePath)
    {
        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        var fullPath = ResolveSafePath(skillManifestPath, fileRelativePath);
        return File.ReadAllText(fullPath);
    }

    [KernelFunction]
    [Description("执行 Skill 目录下 scripts 文件夹中的脚本文件。支持 PowerShell、Python、Bash 脚本。")]
    public ScriptExecutionResult RunSkillScript(
        [Description("SKILL.md 文件的绝对路径")] string skillManifestPath,
        [Description("脚本文件相对路径，必须在 scripts/ 目录下，从 ListSkillFiles 获取")] string scriptRelativePath,
        [Description("命令行参数（不含脚本路径），例如：--input data.json")] string arguments = "")
    {
        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        var scriptPath = ResolveSafePath(skillManifestPath, scriptRelativePath);

        var psi = new ProcessStartInfo
        {
            FileName = ResolveInterpreter(scriptPath),
            Arguments = BuildArguments(scriptPath, arguments),
            WorkingDirectory = Path.GetDirectoryName(skillManifestPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)(TimeSpan.FromSeconds(30)).TotalMilliseconds))
        {
            process.Kill(true);
            return new ScriptExecutionResult { TimedOut = true };
        }

        return new ScriptExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputTask.Result,
            StandardError = errorTask.Result,
            Hint = "If you need to run another script, call ListSkillFiles again."
        };
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
                Id = Guid.NewGuid().ToString(),
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

    private string ResolveSafePath(string skillManifestPath, string relativePath)
    {
        var rootPath = Path.GetDirectoryName(skillManifestPath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Access to paths outside the skill directory is not allowed.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The resource not found: {relativePath}");
        }

        return fullPath;
    }

    private static string ResolveInterpreter(string scriptPath)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();

        return ext switch
        {
            ".ps1" => "pwsh",
            ".py" => "python",
            ".sh" => "C:\\Program Files\\Git\\bin\\bash.exe",
            _ => throw new NotSupportedException($"Unsupported script type: {ext}")
        };
    }

    private string BuildArguments(string scriptPath, string arguments)
        => string.IsNullOrWhiteSpace(arguments)
            ? $"\"{scriptPath}\""
            : $"\"{scriptPath}\" {arguments}";

    private List<string> CollectFiles(string dirPath, string rootPath)
    {
        var files = new List<string>();
        if (!Directory.Exists(dirPath)) return files;

        foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootPath, file).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(relative);
        }

        return files;
    }
}
