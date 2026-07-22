using System.CommandLine;
using InsightaAI.Agent.Cli.Localization;
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
        var command = new Command("skills", CliStrings.SkillsDescription);

        // list 子命令
        var listCommand = new Command("list", CliStrings.SkillsListDescription);
        var scopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescription);
        listCommand.AddOption(scopeOption);
        listCommand.SetHandler((scope) => ListSkillsAsync(scope), scopeOption);

        // install 子命令
        var installCommand = new Command("install", CliStrings.SkillsInstallDescription);
        var pathArgument = new Argument<string>("path", CliStrings.SkillsPathArgumentDescription);
        var installScopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescriptionWithDefault);
        installCommand.AddArgument(pathArgument);
        installCommand.AddOption(installScopeOption);
        installCommand.SetHandler((path, scope) => InstallSkillAsync(path, scope), pathArgument, installScopeOption);

        // uninstall 子命令
        var uninstallCommand = new Command("uninstall", CliStrings.SkillsUninstallDescription);
        var nameArgument = new Argument<string>("name", CliStrings.SkillsNameArgumentDescription);
        var uninstallScopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescription);
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
        var directory = Markup.Escape(skillsDir);
        AnsiConsole.MarkupLine($"[bold blue]{GetScopeDisplayName(scopeName)}[/] [dim]({directory})[/]");

        if (!Directory.Exists(skillsDir))
        {
            AnsiConsole.MarkupLine($"[dim]  {CliStrings.SkillsListEmpty}[/]");
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
            AnsiConsole.MarkupLine($"[dim]  {CliStrings.SkillsListEmpty}[/]");
            return;
        }

        var table = new Table()
            .AddColumn(CliStrings.SkillsListFieldName)
            .AddColumn(CliStrings.SkillsListFieldDescription)
            .Border(TableBorder.Rounded);

        foreach (var skill in skills)
        {
            table.AddRow(new Text(skill.Name), new Text(skill.Description ?? string.Empty));
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
            var message = CliStrings.Format(
                "SkillSourceDirectoryNotFoundFormat",
                Markup.Escape(sourcePath));
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {message}[/]");
            return;
        }

        var skillMdPath = Path.Combine(sourcePath, "SKILL.md");
        if (!File.Exists(skillMdPath))
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {CliStrings.SkillManifestMissing}[/]");
            return;
        }

        // 解析 SKILL.md 获取 skill 名称
        var content = await File.ReadAllTextAsync(skillMdPath);
        var metadata = LocalSkillProvider.ParseMetadata(content);

        if (metadata == null)
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {CliStrings.SkillManifestInvalid}[/]");
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
            var overwrite = AnsiConsole.Confirm(
                CliStrings.Format("SkillOverwritePromptFormat", Markup.Escape(metadata.Name)));
            if (!overwrite)
            {
                AnsiConsole.MarkupLine($"[yellow]{CliStrings.CommonCancelled}[/]");
                return;
            }
            Directory.Delete(skillTargetDir, true);
        }

        // 复制目录
        CopyDirectory(sourcePath, skillTargetDir);

        var installed = CliStrings.Format(
            "SkillInstalledFormat",
            Markup.Escape(metadata.Name),
            Markup.Escape(skillTargetDir));
        AnsiConsole.MarkupLine($"[green]✓[/] {installed}");
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
            var message = CliStrings.Format("SkillNotFoundFormat", Markup.Escape(skillName));
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
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
        var message = CliStrings.Format(
            "SkillRemovedFormat",
            GetScopeDisplayName(scopeName),
            Markup.Escape(skillName));
        AnsiConsole.MarkupLine($"[green]✓[/] {message}");
        return true;
    }

    private static string GetScopeDisplayName(string scope)
    {
        return scope.Equals("project", StringComparison.OrdinalIgnoreCase)
            ? CliStrings.ScopeProject
            : CliStrings.ScopeGlobal;
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
