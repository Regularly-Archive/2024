using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PostgreSQL.Embedding.Plugins;

[KernelPlugin(Description = "A plugin that supports dynamically loading and invoking Skills, including loading and reading resources as well as executing scripts", Version = "1.2")]
public class UseSkillsPlugin : BasePlugin
{
    public UseSkillsPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
    {

    }

    [KernelFunction]
    [Description("List all available skills under the given skills root folder")]
    public IEnumerable<LlmSkillMetadataModel> ListSkills(
        [Description("Absolute path to the skills root folder, e.g. D:\\skills")]string skillsRootFolder)
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
    [Description("Read the SKILL.md manifest file of a skill")]
    public string GetSkillManifest(
        [Description("Absolute path to SKILL.md file, obtained from ListSkills")] string skillManifestPath)
    {
        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        if (!File.Exists(skillManifestPath))
            throw new FileNotFoundException($"The sspecific path not found: {skillManifestPath}");

        return File.ReadAllText(skillManifestPath);
    }

    [KernelFunction]
    [Description("List files inside a skill directory, grouped by type")]
    public SkillFileIndex ListSkillFiles(
        [Description("Absolute path to SKILL.md file")] string skillManifestPath)
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
    [Description("Read a text file inside a skill directory")]
    public string ReadSkillFile(
        [Description("Absolute path to SKILL.md file")] string skillManifestPath, 
        [Description("Relative file path selected from ListSkillFiles")]string fileRelativePath)
    {
        if (!skillManifestPath.EndsWith("SKILL.md")) skillManifestPath = Path.Combine(skillManifestPath, "SKILL.md");

        var fullPath = ResolveSafePath(skillManifestPath, fileRelativePath);
        return File.ReadAllText(fullPath);
    }

    [KernelFunction]
    [Description("Run a script under the scripts folder of a skill")]
    public ScriptExecutionResult RunSkillScript(
        [Description("Absolute path to SKILL.md file")] string skillManifestPath,
        [Description("Script path relative to skill root, must be under scripts/, selected from ListSkillFiles")] string scriptRelativePath,
        [Description("Command line arguments WITHOUT script path, e.g. --input data.json")] string arguments = "")
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
