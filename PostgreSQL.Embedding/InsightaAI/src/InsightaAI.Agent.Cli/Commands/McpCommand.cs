using System.CommandLine;
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
        var command = new Command("mcp", "管理 MCP 服务器配置");

        // list 子命令
        var listCommand = new Command("list", "列出所有 MCP 服务器");
        var listScopeOption = new Option<string?>("--scope", "指定范围: global 或 project");
        listCommand.AddOption(listScopeOption);
        listCommand.SetHandler((scope) => ListAsync(scope), listScopeOption);

        // add 子命令
        var addCommand = new Command("add", "添加 MCP 服务器");
        var nameArgument = new Argument<string>("name", "服务器名称");
        var transportOption = new Option<string>("--transport", () => "stdio", "传输方式: stdio 或 sse");
        var commandOption = new Option<string?>("--command", "stdio 模式的可执行文件路径");
        var argsOption = new Option<string?>("--args", "命令行参数");
        var urlOption = new Option<string?>("--url", "SSE 模式的端点 URL");
        var descriptionOption = new Option<string?>("--description", "服务器描述");
        var addScopeOption = new Option<string?>("--scope", "指定范围: global 或 project (默认 global)");

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
        var removeCommand = new Command("remove", "移除 MCP 服务器");
        var removeNameArgument = new Argument<string>("name", "服务器名称");
        var removeScopeOption = new Option<string?>("--scope", "指定范围: global 或 project");
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
            AnsiConsole.MarkupLine($"[bold blue]{scopeName} MCP Servers[/]");

            var servers = await provider.GetServersAsync();
            if (servers.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]  没有配置 MCP 服务器[/]");
                if (providers.Count > 1) AnsiConsole.WriteLine();
                continue;
            }

            var table = new Table()
                .AddColumn("Name")
                .AddColumn("Transport")
                .AddColumn("Endpoint / Command")
                .AddColumn("Description")
                .Border(TableBorder.Rounded);

            foreach (var server in servers)
            {
                var endpoint = server.Transport == "stdio"
                    ? server.Command ?? ""
                    : server.Endpoint ?? "";

                table.AddRow(
                    server.Name,
                    server.Transport,
                    endpoint,
                    server.Description ?? "");
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
                $"MCP 服务器 '{name}' 已存在，是否覆盖?", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[yellow]已取消[/]");
                return;
            }
        }

        if (transport == "sse" && string.IsNullOrEmpty(url))
        {
            AnsiConsole.MarkupLine("[red]错误: SSE 模式需要指定 --url[/]");
            return;
        }

        if (transport == "stdio" && string.IsNullOrEmpty(command))
        {
            AnsiConsole.MarkupLine("[red]错误: stdio 模式需要指定 --command[/]");
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
        AnsiConsole.MarkupLine($"[green]✓[/] MCP 服务器 '{name}' 已添加到 {targetScope} 范围");
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
            AnsiConsole.MarkupLine($"[green]✓[/] 已从 {scopeName} 范围移除 MCP 服务器: {name}");
            removed = true;
        }

        if (!removed)
        {
            AnsiConsole.MarkupLine($"[yellow]未找到 MCP 服务器: {name}[/]");
        }
    }
}
