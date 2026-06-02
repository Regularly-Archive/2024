using System.CommandLine;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Skills.Local;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// skills 命令 - 管理 Agent Skills
/// </summary>
public class SkillsCommand
{
    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("skills", "管理 Agent Skills");

        // list 子命令
        var listCommand = new Command("list", "列出所有可用的 Skills");
        var scopeOption = new Option<string?>("--scope", "指定范围: global 或 project");
        listCommand.AddOption(scopeOption);
        listCommand.SetHandler((scope) => ListSkillsAsync(scope), scopeOption);

        // install 子命令
        var installCommand = new Command("install", "安装 Skill");
        var pathArgument = new Argument<string>("path", "Skill 目录路径");
        var installScopeOption = new Option<string?>("--scope", "指定范围: global 或 project (默认 global)");
        installCommand.AddArgument(pathArgument);
        installCommand.AddOption(installScopeOption);
        installCommand.SetHandler((path, scope) => InstallSkillAsync(path, scope), pathArgument, installScopeOption);

        // uninstall 子命令
        var uninstallCommand = new Command("uninstall", "卸载 Skill");
        var nameArgument = new Argument<string>("name", "Skill 名称");
        var uninstallScopeOption = new Option<string?>("--scope", "指定范围: global 或 project");
        uninstallCommand.AddArgument(nameArgument);
        uninstallCommand.AddOption(uninstallScopeOption);
        uninstallCommand.SetHandler((name, scope) => UninstallSkillAsync(name, scope), nameArgument, uninstallScopeOption);

        command.AddCommand(listCommand);
        command.AddCommand(installCommand);
        command.AddCommand(uninstallCommand);

        return command;
    }

    /// <summary>
    /// 列出所有可用的 Skills
    /// </summary>
    private async Task ListSkillsAsync(string? scope)
    {
        var showGlobal = scope == null || scope == "global";
        var showProject = scope == null || scope == "project";

        if (showGlobal)
        {
            await ListSkillsInDirectory(CliConfig.GlobalSkillsDir, "Global");
        }

        if (showProject)
        {
            if (showGlobal) AnsiConsole.WriteLine();
            await ListSkillsInDirectory(CliConfig.ProjectSkillsDir, "Project");
        }
    }

    /// <summary>
    /// 列出指定目录下的 Skills
    /// </summary>
    private async Task ListSkillsInDirectory(string skillsDir, string scopeName)
    {
        AnsiConsole.MarkupLine($"[bold blue]{scopeName} Skills[/] ({skillsDir})");

        if (!Directory.Exists(skillsDir))
        {
            AnsiConsole.MarkupLine("[dim]  目录不存在[/]");
            return;
        }

        var provider = new LocalSkillProvider(skillsDir);
        var skills = new List<SkillMetadata>();

        await foreach (var skill in provider.ListSkillsAsync())
        {
            skills.Add(skill);
        }

        if (skills.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]  没有安装任何 Skills[/]");
            return;
        }

        var table = new Table()
            .AddColumn("Name")
            .AddColumn("Description")
            .Border(TableBorder.Rounded);

        foreach (var skill in skills)
        {
            table.AddRow(skill.Name, skill.Description);
        }

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// 安装 Skill
    /// </summary>
    private async Task InstallSkillAsync(string sourcePath, string? scope)
    {
        var targetDir = scope == "project" ? CliConfig.ProjectSkillsDir : CliConfig.GlobalSkillsDir;

        // 验证源路径
        if (!Directory.Exists(sourcePath))
        {
            AnsiConsole.MarkupLine($"[red]错误: 目录不存在: {sourcePath}[/]");
            return;
        }

        var skillMdPath = Path.Combine(sourcePath, "SKILL.md");
        if (!File.Exists(skillMdPath))
        {
            AnsiConsole.MarkupLine("[red]错误: 目录中没有找到 SKILL.md 文件[/]");
            return;
        }

        // 解析 SKILL.md 获取 skill 名称
        var content = await File.ReadAllTextAsync(skillMdPath);
        var metadata = LocalSkillProvider.ParseMetadata(content);

        if (metadata == null)
        {
            AnsiConsole.MarkupLine("[red]错误: 无法解析 SKILL.md 文件，请检查格式[/]");
            return;
        }

        // 确保目标目录存在
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var skillTargetDir = Path.Combine(targetDir, metadata.Name);

        // 检查是否已存在
        if (Directory.Exists(skillTargetDir))
        {
            var overwrite = AnsiConsole.Confirm($"Skill '{metadata.Name}' 已存在，是否覆盖?");
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[yellow]已取消[/]");
                return;
            }
            Directory.Delete(skillTargetDir, true);
        }

        // 复制目录
        CopyDirectory(sourcePath, skillTargetDir);

        AnsiConsole.MarkupLine($"[green]✓[/] Skill '{metadata.Name}' 已安装到: {skillTargetDir}");
    }

    /// <summary>
    /// 卸载 Skill
    /// </summary>
    private async Task UninstallSkillAsync(string skillName, string? scope)
    {
        var showGlobal = scope == null || scope == "global";
        var showProject = scope == null || scope == "project";
        var removed = false;

        if (showGlobal)
        {
            removed |= await RemoveSkillFromDirectory(CliConfig.GlobalSkillsDir, skillName, "Global");
        }

        if (showProject)
        {
            removed |= await RemoveSkillFromDirectory(CliConfig.ProjectSkillsDir, skillName, "Project");
        }

        if (!removed)
        {
            AnsiConsole.MarkupLine($"[yellow]未找到 Skill: {skillName}[/]");
        }
    }

    /// <summary>
    /// 从指定目录删除 Skill
    /// </summary>
    private async Task<bool> RemoveSkillFromDirectory(string skillsDir, string skillName, string scopeName)
    {
        if (!Directory.Exists(skillsDir))
        {
            return false;
        }

        var skillDir = Path.Combine(skillsDir, skillName);
        if (!Directory.Exists(skillDir))
        {
            return false;
        }

        // 验证是否是有效的 Skill 目录
        var skillMdPath = Path.Combine(skillDir, "SKILL.md");
        if (!File.Exists(skillMdPath))
        {
            return false;
        }

        Directory.Delete(skillDir, true);
        AnsiConsole.MarkupLine($"[green]✓[/] 已从 {scopeName} 范围删除 Skill: {skillName}");
        return true;
    }

    /// <summary>
    /// 递归复制目录
    /// </summary>
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        // 复制文件
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var targetFile = Path.Combine(targetDir, fileName);
            File.Copy(file, targetFile, true);
        }

        // 递归复制子目录
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var targetSubDir = Path.Combine(targetDir, dirName);
            CopyDirectory(dir, targetSubDir);
        }
    }
}
