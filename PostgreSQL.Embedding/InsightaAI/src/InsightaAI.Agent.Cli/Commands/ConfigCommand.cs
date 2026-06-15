using System.CommandLine;
using InsightaAI.Agent.Cli.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// config 命令 - 配置 LLM 提供商和 API Key
/// </summary>
public class ConfigCommand
{
    private const string ProviderOpenAI = "openai";
    private const string ProviderAnthropic = "anthropic";
    private const string DefaultModelOpenAI = "gpt-4o-mini";
    private const string DefaultModelAnthropic = "claude-sonnet-4-20250514";

    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("config", "配置 LLM 提供商和 API Key");
        command.SetHandler(() => ExecuteAsync());
        return command;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    public async Task<int> ExecuteAsync()
    {
        var config = CliConfig.Load();

        AnsiConsole.MarkupLine("[bold blue]InsightaAI 配置[/]");
        AnsiConsole.WriteLine();

        // Provider 选择
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择 LLM 提供商:")
                .AddChoices([ProviderOpenAI, ProviderAnthropic]));
        config.Provider = provider;

        // Model 输入
        var defaultModel = provider == ProviderOpenAI ? DefaultModelOpenAI : DefaultModelAnthropic;
        config.Model = AnsiConsole.Prompt(
            new TextPrompt<string>("模型名称:")
                .DefaultValue(defaultModel));

        // API Key 输入
        if (provider == ProviderOpenAI)
        {
            config.OpenAiApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("OpenAI API Key:")
                    .AllowEmpty()
                    .Secret());

            var baseUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("OpenAI Base URL (可选，直接回车跳过):")
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                config.OpenAiBaseUrl = baseUrl;
            }
        }
        else
        {
            config.AnthropicApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("Anthropic API Key:")
                    .AllowEmpty()
                    .Secret());

            var baseUrl = AnsiConsole.Prompt(
                new TextPrompt<string>("Anthropic Base URL (可选，直接回车跳过):")
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                config.AnthropicBaseUrl = baseUrl;
            }
        }

        // 系统提示词
        config.SystemPrompt = AnsiConsole.Prompt(
            new TextPrompt<string>("系统提示词:")
                .DefaultValue(config.SystemPrompt));

        config.Save();
        AnsiConsole.MarkupLine("[green]配置已保存到:[/] " + CliConfig.ConfigPath);

        return 0;
    }
}
