using System.CommandLine;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Mcp.Local;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// mcp 命令 - 管理 MCP 服务器配置
/// </summary>
public class McpCommand
{
    private static readonly string GlobalMcpConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agents",
        "mcp-servers.json");

    private static readonly string ProjectMcpConfigPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        ".insighta",
        "mcp-servers.json");

    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("mcp", CliStrings.McpDescription);

        // list 子命令
        var listCommand = new Command("list", CliStrings.McpListDescription);
        var listScopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescription);
        listCommand.AddOption(listScopeOption);
        listCommand.SetHandler((scope) => ListAsync(scope), listScopeOption);

        // add 子命令
        var addCommand = new Command("add", CliStrings.McpAddDescription);
        var nameArgument = new Argument<string>("name", CliStrings.McpNameArgumentDescription);
        var transportOption = new Option<string>("--transport", () => "stdio", CliStrings.McpTransportOptionDescription);
        var commandOption = new Option<string?>("--command", CliStrings.McpCommandOptionDescription);
        var argsOption = new Option<string?>("--args", CliStrings.McpArgsOptionDescription);
        var urlOption = new Option<string?>("--url", CliStrings.McpUrlOptionDescription);
        var descriptionOption = new Option<string?>("--description", CliStrings.McpDescriptionOptionDescription);
        var addScopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescriptionWithDefault);

        addCommand.AddArgument(nameArgument);
        addCommand.AddOption(transportOption);
        addCommand.AddOption(commandOption);
        addCommand.AddOption(argsOption);
        addCommand.AddOption(urlOption);
        addCommand.AddOption(descriptionOption);
        addCommand.AddOption(addScopeOption);
        addCommand.SetHandler((name, transport, cmd, args, url, description, scope)
            => AddAsync(name, transport, cmd, args, url, description, scope),
            nameArgument, transportOption, commandOption, argsOption, urlOption, descriptionOption, addScopeOption);

        // remove 子命令
        var removeCommand = new Command("remove", CliStrings.McpRemoveDescription);
        var removeNameArgument = new Argument<string>("name", CliStrings.McpNameArgumentDescription);
        var removeScopeOption = new Option<string?>("--scope", CliStrings.ScopeOptionDescription);
        removeCommand.AddArgument(removeNameArgument);
        removeCommand.AddOption(removeScopeOption);
        removeCommand.SetHandler((name, scope) => RemoveAsync(name, scope), removeNameArgument, removeScopeOption);

        command.AddCommand(listCommand);
        command.AddCommand(addCommand);
        command.AddCommand(removeCommand);

        return command;
    }

    /// <summary>
    /// 获取提供者实例
    /// </summary>
    private static List<(string Scope, JsonMcpServerProvider Provider)> GetProviders(string? scope)
    {
        var providers = new List<(string, JsonMcpServerProvider)>();

        if (scope == null || scope == "global")
        {
            providers.Add(("Global", new JsonMcpServerProvider(GlobalMcpConfigPath)));
        }

        if (scope == null || scope == "project")
        {
            providers.Add(("Project", new JsonMcpServerProvider(ProjectMcpConfigPath)));
        }

        return providers;
    }

    /// <summary>
    /// 列出所有 MCP 服务器
    /// </summary>
    private async Task ListAsync(string? scope)
    {
        var providers = GetProviders(scope);

        foreach (var (scopeName, provider) in providers)
        {
            var directory = Markup.Escape(GetScopeDirectory(scopeName));
            AnsiConsole.MarkupLine($"[bold blue]{GetScopeDisplayName(scopeName)}[/] [dim]({directory})[/]");

            var servers = await provider.GetServersAsync();
            if (servers.Count == 0)
            {
                AnsiConsole.MarkupLine($"[dim]  {CliStrings.McpListEmpty}[/]");
                if (providers.Count > 1) AnsiConsole.WriteLine();
                continue;
            }

            var table = new Table()
                .AddColumn(CliStrings.McpListFieldName)
                .AddColumn(CliStrings.McpListFieldTransport)
                .AddColumn(CliStrings.McpListFieldEndpoint)
                .AddColumn(CliStrings.McpListFieldDescription)
                .Border(TableBorder.Rounded);

            foreach (var server in servers)
            {
                var endpoint = server.Transport == "stdio"
                    ? string.Join(
                        " ",
                        new[] { server.Command ?? "" }
                            .Concat(server.Args ?? [])
                            .Where(part => !string.IsNullOrWhiteSpace(part)))
                    : server.Endpoint ?? "";

                table.AddRow(
                    new Text(server.Name),
                    new Text(server.Transport),
                    new Text(endpoint),
                    new Text(server.Description ?? ""));
            }

            AnsiConsole.Write(table);

            if (providers.Count > 1) AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// 添加 MCP 服务器
    /// </summary>
    private async Task AddAsync(
        string name,
        string transport,
        string? command,
        string? args,
        string? url,
        string? description,
        string? scope)
    {
        var targetScope = scope ?? "global";
        var provider = new JsonMcpServerProvider(
            targetScope == "project" ? ProjectMcpConfigPath : GlobalMcpConfigPath);

        // 检查是否已存在
        var existing = await provider.GetServerAsync(name);
        if (existing != null)
        {
            var overwrite = AnsiConsole.Confirm(
                CliStrings.Format("McpOverwritePromptFormat", Markup.Escape(name)), false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine($"[yellow]{CliStrings.CommonCancelled}[/]");
                return;
            }
        }

        if (transport == "sse" && string.IsNullOrEmpty(url))
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {CliStrings.McpSseUrlRequired}[/]");
            return;
        }

        if (transport == "stdio" && string.IsNullOrEmpty(command))
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {CliStrings.McpStdioCommandRequired}[/]");
            return;
        }

        var config = new McpServerConfig
        {
            Name = name,
            Transport = transport,
            Command = transport == "stdio" ? command : null,
            Args = transport == "stdio" && !string.IsNullOrEmpty(args)
                ? args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : null,
            Endpoint = transport == "sse" ? url : null,
            Description = description
        };

        await provider.AddServerAsync(config);
        var added = CliStrings.Format(
            "McpAddedFormat",
            Markup.Escape(name),
            GetScopeDisplayName(targetScope));
        AnsiConsole.MarkupLine($"[green]✓[/] {added}");
    }

    /// <summary>
    /// 移除 MCP 服务器
    /// </summary>
    private async Task RemoveAsync(string name, string? scope)
    {
        var providers = GetProviders(scope);
        var removed = false;

        foreach (var (scopeName, provider) in providers)
        {
            var existing = await provider.GetServerAsync(name);
            if (existing == null) continue;

            await provider.RemoveServerAsync(name);
            var message = CliStrings.Format(
                "McpRemovedFormat",
                GetScopeDisplayName(scopeName),
                Markup.Escape(name));
            AnsiConsole.MarkupLine($"[green]✓[/] {message}");
            removed = true;
        }

        if (!removed)
        {
            var message = CliStrings.Format("McpNotFoundFormat", Markup.Escape(name));
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
        }
    }

    private static string GetScopeDisplayName(string scope)
    {
        return scope.Equals("project", StringComparison.OrdinalIgnoreCase)
            ? CliStrings.ScopeProject
            : CliStrings.ScopeGlobal;
    }

    private static string GetScopeDirectory(string scope)
    {
        var configPath = scope.Equals("project", StringComparison.OrdinalIgnoreCase)
            ? ProjectMcpConfigPath
            : GlobalMcpConfigPath;

        return Path.GetDirectoryName(configPath) ?? configPath;
    }
}
