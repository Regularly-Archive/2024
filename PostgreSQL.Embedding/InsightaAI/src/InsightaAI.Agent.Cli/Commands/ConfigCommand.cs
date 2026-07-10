using System.CommandLine;
using InsightaAI.Agent.Cli.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// config 命令 - 配置 Provider、Model 和系统设置
/// </summary>
public class ConfigCommand
{
    private static readonly string[] AdapterChoices = ["openai", "openai-response", "anthropic", "gemini"];

    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("config", "配置 Provider、Model 和系统设置");
        command.SetHandler(() => ExecuteAsync());
        return command;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ExecuteAsync()
    {
        var config = CliConfig.Load();
        var auth = AuthConfig.Load();

        AnsiConsole.MarkupLine("[bold blue]InsightaAI 配置向导[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("选择操作:")
                    .AddChoices([
                        "管理 Providers（认证配置）",
                        "管理 Models（模型配置）",
                        "选择主模型",
                        "编辑系统提示词",
                        "编辑其他设置",
                        "保存并退出"
                    ]));

            switch (action)
            {
                case "管理 Providers（认证配置）":
                    ManageProviders(auth);
                    break;
                case "管理 Models（模型配置）":
                    ManageModels(config);
                    break;
                case "选择主模型":
                    SelectPrimaryModel(config);
                    break;
                case "编辑系统提示词":
                    EditSystemPrompt(config);
                    break;
                case "编辑其他设置":
                    EditOtherSettings(config);
                    break;
                case "保存并退出":
                    auth.Save();
                    config.Save();
                    AnsiConsole.MarkupLine("[green]配置已保存[/]");
                    AnsiConsole.MarkupLine($"[dim]  Auth:   {AuthConfig.AuthConfigPath}[/]");
                    AnsiConsole.MarkupLine($"[dim]  Config: {CliConfig.ConfigPath}[/]");
                    return 0;
            }

            AnsiConsole.WriteLine();
        }
    }

    private void ManageProviders(AuthConfig auth)
    {
        while (true)
        {
            var providerNames = auth.Providers.Keys.ToList();
            var options = new List<string> { "添加 Provider" };
            if (providerNames.Count > 0)
            {
                options.Add("编辑 Provider");
                options.Add("删除 Provider");
            }
            options.Add("返回");

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Provider 管理:")
                    .AddChoices(options));

            if (action == "返回") break;

            switch (action)
            {
                case "添加 Provider":
                    AddProvider(auth);
                    break;
                case "编辑 Provider":
                    EditProvider(auth);
                    break;
                case "删除 Provider":
                    DeleteProvider(auth);
                    break;
            }
        }
    }

    private void AddProvider(AuthConfig auth)
    {
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Provider 名称（如 deepseek, kimi, openai）:"));

        if (auth.Providers.ContainsKey(name))
        {
            AnsiConsole.MarkupLine($"[yellow]Provider '{name}' 已存在，将覆盖现有配置[/]");
        }

        var adapter = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择适配器:")
                .AddChoices(AdapterChoices));

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("API Key:")
                .Secret());

        var baseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>("Base URL（可选，直接回车跳过）:")
                .AllowEmpty());

        auth.Providers[name] = new ProviderEntry
        {
            Adapter = adapter,
            ApiKey = apiKey,
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl
        };

        AnsiConsole.MarkupLine($"[green]✓[/] Provider '{name}' 已添加");
    }

    private void EditProvider(AuthConfig auth)
    {
        var name = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择要编辑的 Provider:")
                .AddChoices(auth.Providers.Keys));

        var entry = auth.Providers[name];

        var adapter = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"适配器（当前: {entry.Adapter}）:")
                .AddChoices(AdapterChoices));

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("API Key（直接回车保持不变）:")
                .AllowEmpty()
                .Secret());

        var baseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>($"Base URL（当前: {entry.BaseUrl ?? "(无)"}，直接回车保持不变）:")
                .AllowEmpty());

        entry.Adapter = adapter;
        if (!string.IsNullOrWhiteSpace(apiKey))
            entry.ApiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(baseUrl))
            entry.BaseUrl = baseUrl;

        AnsiConsole.MarkupLine($"[green]✓[/] Provider '{name}' 已更新");
    }

    private void DeleteProvider(AuthConfig auth)
    {
        var name = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择要删除的 Provider:")
                .AddChoices(auth.Providers.Keys));

        if (AnsiConsole.Confirm($"确认删除 Provider '{name}'？"))
        {
            auth.Providers.Remove(name);
            AnsiConsole.MarkupLine($"[green]✓[/] Provider '{name}' 已删除");
        }
    }

    private void ManageModels(CliConfig config)
    {
        while (true)
        {
            var modelKeys = config.Models.Keys.ToList();
            var options = new List<string> { "添加 Model" };
            if (modelKeys.Count > 0)
            {
                options.Add("编辑 Model");
                options.Add("删除 Model");
            }
            options.Add("返回");

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Model 管理:")
                    .AddChoices(options));

            if (action == "返回") break;

            switch (action)
            {
                case "添加 Model":
                    AddModel(config);
                    break;
                case "编辑 Model":
                    EditModel(config);
                    break;
                case "删除 Model":
                    DeleteModel(config);
                    break;
            }
        }
    }

    private void AddModel(CliConfig config)
    {
        var key = AnsiConsole.Prompt(
            new TextPrompt<string>("Model 引用（格式: provider/model_key，如 deepseek/deepseek-chat）:"));

        if (config.Models.ContainsKey(key))
        {
            AnsiConsole.MarkupLine($"[yellow]Model '{key}' 已存在，将覆盖现有配置[/]");
        }

        var modelId = AnsiConsole.Prompt(
            new TextPrompt<string>("Model ID（发送给 API 的 model 参数，如 deepseek-chat）:")
                .DefaultValue(key.Split('/').Last()));

        var maxTokensStr = AnsiConsole.Prompt(
            new TextPrompt<string>("Max Tokens（可选，直接回车跳过）:")
                .AllowEmpty());

        var contextWindowStr = AnsiConsole.Prompt(
            new TextPrompt<string>("Context Window（可选，直接回车使用默认值）:")
                .AllowEmpty());

        var entry = new ModelEntry
        {
            ModelId = modelId
        };

        if (int.TryParse(maxTokensStr, out var maxTokens))
            entry.MaxTokens = maxTokens;

        if (int.TryParse(contextWindowStr, out var contextWindow))
            entry.ContextWindow = contextWindow;

        config.Models[key] = entry;
        AnsiConsole.MarkupLine($"[green]✓[/] Model '{key}' 已添加");
    }

    private void EditModel(CliConfig config)
    {
        var key = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择要编辑的 Model:")
                .AddChoices(config.Models.Keys));

        var entry = config.Models[key];

        var modelId = AnsiConsole.Prompt(
            new TextPrompt<string>($"Model ID（当前: {entry.ModelId}）:")
                .DefaultValue(entry.ModelId));

        var maxTokensStr = AnsiConsole.Prompt(
            new TextPrompt<string>($"Max Tokens（当前: {entry.MaxTokens?.ToString() ?? "默认"}，直接回车保持不变）:")
                .AllowEmpty());

        var contextWindowStr = AnsiConsole.Prompt(
            new TextPrompt<string>($"Context Window（当前: {entry.ContextWindow?.ToString() ?? "默认"}，直接回车保持不变）:")
                .AllowEmpty());

        entry.ModelId = modelId;

        if (!string.IsNullOrWhiteSpace(maxTokensStr) && int.TryParse(maxTokensStr, out var maxTokens))
            entry.MaxTokens = maxTokens;

        if (!string.IsNullOrWhiteSpace(contextWindowStr) && int.TryParse(contextWindowStr, out var contextWindow))
            entry.ContextWindow = contextWindow;

        AnsiConsole.MarkupLine($"[green]✓[/] Model '{key}' 已更新");
    }

    private void DeleteModel(CliConfig config)
    {
        var key = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择要删除的 Model:")
                .AddChoices(config.Models.Keys));

        if (AnsiConsole.Confirm($"确认删除 Model '{key}'？"))
        {
            config.Models.Remove(key);
            AnsiConsole.MarkupLine($"[green]✓[/] Model '{key}' 已删除");
        }
    }

    private void SelectPrimaryModel(CliConfig config)
    {
        if (config.Models.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]没有配置任何 Model，请先添加 Model[/]");
            return;
        }

        var model = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"选择主模型（当前: {config.PrimaryModel}）:")
                .AddChoices(config.Models.Keys));

        config.PrimaryModel = model;
        AnsiConsole.MarkupLine($"[green]✓[/] 主模型已设为 '{model}'");

        // 可选设置副模型
        if (AnsiConsole.Confirm("是否设置独立的副模型（用于上下文压缩等辅助任务）？", false))
        {
            var summaryModel = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("选择副模型:")
                    .AddChoices(config.Models.Keys));

            config.SecondaryModel = summaryModel;
            AnsiConsole.MarkupLine($"[green]✓[/] 副模型已设为 '{summaryModel}'");
        }
    }

    private void EditSystemPrompt(CliConfig config)
    {
        config.SystemPrompt = AnsiConsole.Prompt(
            new TextPrompt<string>("系统提示词:")
                .DefaultValue(config.SystemPrompt));
    }

    private void EditOtherSettings(CliConfig config)
    {
        config.MaxToolRounds = AnsiConsole.Prompt(
            new TextPrompt<int>($"最大工具调用轮次（当前: {config.MaxToolRounds}）:")
                .DefaultValue(config.MaxToolRounds));

        config.EnableBuiltInTools = AnsiConsole.Confirm(
            $"启用内置工具（当前: {config.EnableBuiltInTools}）？",
            config.EnableBuiltInTools);
    }
}
