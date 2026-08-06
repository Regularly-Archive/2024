using System.CommandLine;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>
/// config 命令 - 配置 Provider、Model 和系统设置
/// </summary>
public class ConfigCommand
{
    /// <summary>
    /// 创建命令对象
    /// </summary>
    public Command Create()
    {
        var command = new Command("config", CliStrings.ConfigDescription);

        var providerCommand = new Command("provider", CliStrings.ConfigProviderDescription);
        providerCommand.SetHandler(() => HandleProviderManagerAsync());

        var modelCommand = new Command("model", CliStrings.ConfigModelDescription);
        modelCommand.SetHandler(() => HandleModelManagerAsync());

        var languageCommand = new Command("language", CliStrings.ConfigLanguageDescription);
        languageCommand.SetHandler(() => HandleLanguageDisplayAsync());

        command.AddCommand(providerCommand);
        command.AddCommand(modelCommand);
        command.AddCommand(languageCommand);

        return command;
    }

    private Task<int> HandleProviderManagerAsync()
    {
        var auth = AuthConfig.Load();
        ManageProviders(auth);
        auth.Save();

        AnsiConsole.MarkupLine($"[green]{CliStrings.ConfigSaved}[/]");
        AnsiConsole.MarkupLine($"[dim]  Auth: {Markup.Escape(AuthConfig.AuthConfigPath)}[/]");
        return Task.FromResult(0);
    }

    private Task<int> HandleModelManagerAsync()
    {
        var config = CliConfig.Load();
        ManageModels(config);
        config.Save();

        AnsiConsole.MarkupLine($"[green]{CliStrings.ConfigSaved}[/]");
        AnsiConsole.MarkupLine($"[dim]  Config: {Markup.Escape(CliConfig.ConfigPath)}[/]");
        return  Task.FromResult(0);
    }

    private Task<int> HandleLanguageDisplayAsync()
    {
        var config = CliConfig.Load();
        var language = PromptMenu<LanguageAction>(
            CliStrings.ConfigLanguagePrompt,
            LanguageAction.Cancel,
            new(LanguageAction.Auto, CliStrings.ConfigLanguageAuto),
            new(LanguageAction.English, CliStrings.ConfigLanguageEnglish),
            new(LanguageAction.Chinese, CliStrings.ConfigLanguageChinese));

        if (language == LanguageAction.Cancel)
        {
            return Task.FromResult(0);
        }

        config.Language = language switch
        {
            LanguageAction.Auto => CliCulture.Auto,
            LanguageAction.English => CliCulture.English,
            LanguageAction.Chinese => CliCulture.Chinese,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
        config.Save();
        CliCulture.Configure(config.Language);

        var message = CliStrings.Format("ConfigLanguageSetFormat", config.Language);
        AnsiConsole.MarkupLine($"[green]✓[/] {message}");
        AnsiConsole.MarkupLine($"[dim]  Config: {Markup.Escape(CliConfig.ConfigPath)}[/]");
        return Task.FromResult(0);
    }

    private void ManageProviders(AuthConfig auth)
    {
        AnsiConsole.MarkupLine($"[dim]{CliStrings.ConfigMenuNavigationHint}[/]");

        while (true)
        {
            var providerNames = auth.Providers.Keys.ToList();
            var options = new List<MenuChoice<ProviderAction>>
            {
                new(ProviderAction.Add, CliStrings.ConfigAddProvider)
            };
            if (providerNames.Count > 0)
            {
                options.Add(new(ProviderAction.Edit, CliStrings.ConfigEditProvider));
                options.Add(new(ProviderAction.Delete, CliStrings.ConfigDeleteProvider));
            }
            options.Add(new(ProviderAction.Back, CliStrings.CommonBack));

            var action = PromptMenu(
                CliStrings.ConfigProviderManagementTitle,
                ProviderAction.Back,
                options.ToArray());

            switch (action)
            {
                case ProviderAction.Add:
                    AddProvider(auth);
                    break;
                case ProviderAction.Edit:
                    EditProvider(auth);
                    break;
                case ProviderAction.Delete:
                    DeleteProvider(auth);
                    break;
                case ProviderAction.Back:
                    return;
            }
        }
    }

    private void AddProvider(AuthConfig auth)
    {
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigProviderNamePrompt));

        if (auth.Providers.ContainsKey(name))
        {
            var message = CliStrings.Format("ConfigProviderExistsFormat", Markup.Escape(name));
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
        }

        var adapter = SelectAdapter(CliStrings.ConfigSelectAdapter);
        if (adapter == null)
        {
            return;
        }

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigApiKeyPrompt)
                .Secret());

        var baseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigBaseUrlOptionalPrompt)
                .AllowEmpty());

        auth.Providers[name] = new ProviderEntry
        {
            Adapter = adapter,
            ApiKey = apiKey,
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl
        };

        var added = CliStrings.Format("ConfigProviderAddedFormat", Markup.Escape(name));
        AnsiConsole.MarkupLine($"[green]✓[/] {added}");
    }

    private void EditProvider(AuthConfig auth)
    {
        var name = PromptSelection(CliStrings.ConfigSelectProviderToEdit, auth.Providers.Keys);
        if (name == null)
        {
            return;
        }

        var entry = auth.Providers[name];

        var adapter = SelectAdapter(
            CliStrings.Format("ConfigAdapterCurrentFormat", Markup.Escape(entry.Adapter)));
        if (adapter == null)
        {
            return;
        }

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigApiKeyKeepPrompt)
                .AllowEmpty()
                .Secret());

        var currentBaseUrl = entry.BaseUrl ?? CliStrings.CommonNone;
        var baseUrl = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.Format(
                    "ConfigBaseUrlCurrentFormat",
                    Markup.Escape(currentBaseUrl)))
                .AllowEmpty());

        entry.Adapter = adapter;
        if (!string.IsNullOrWhiteSpace(apiKey))
            entry.ApiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(baseUrl))
            entry.BaseUrl = baseUrl;

        var updated = CliStrings.Format("ConfigProviderUpdatedFormat", Markup.Escape(name));
        AnsiConsole.MarkupLine($"[green]✓[/] {updated}");
    }

    private void DeleteProvider(AuthConfig auth)
    {
        var name = PromptSelection(CliStrings.ConfigSelectProviderToDelete, auth.Providers.Keys);
        if (name == null)
        {
            return;
        }

        var prompt = CliStrings.Format("ConfigDeleteProviderConfirmFormat", Markup.Escape(name));
        if (AnsiConsole.Confirm(prompt))
        {
            auth.Providers.Remove(name);
            var deleted = CliStrings.Format("ConfigProviderDeletedFormat", Markup.Escape(name));
            AnsiConsole.MarkupLine($"[green]✓[/] {deleted}");
        }
    }

    private void ManageModels(CliConfig config)
    {
        AnsiConsole.MarkupLine($"[dim]{CliStrings.ConfigMenuNavigationHint}[/]");

        while (true)
        {
            var modelKeys = config.Models.Keys.ToList();
            var options = new List<MenuChoice<ModelAction>>
            {
                new(ModelAction.Add, CliStrings.ConfigAddModel)
            };
            if (modelKeys.Count > 0)
            {
                options.Add(new(ModelAction.Edit, CliStrings.ConfigEditModel));
                options.Add(new(ModelAction.Delete, CliStrings.ConfigDeleteModel));
                options.Add(new(ModelAction.SelectPrimary, CliStrings.ConfigSelectPrimaryModel));
            }
            options.Add(new(ModelAction.Back, CliStrings.CommonBack));

            var action = PromptMenu(
                CliStrings.ConfigModelManagementTitle,
                ModelAction.Back,
                options.ToArray());

            switch (action)
            {
                case ModelAction.Add:
                    AddModel(config);
                    break;
                case ModelAction.Edit:
                    EditModel(config);
                    break;
                case ModelAction.Delete:
                    DeleteModel(config);
                    break;
                case ModelAction.SelectPrimary:
                    SelectPrimaryModel(config);
                    break;
                case ModelAction.Back:
                    return;
            }
        }
    }

    private void AddModel(CliConfig config)
    {
        var key = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigModelReferencePrompt));

        if (config.Models.ContainsKey(key))
        {
            var message = CliStrings.Format("ConfigModelExistsFormat", Markup.Escape(key));
            AnsiConsole.MarkupLine($"[yellow]{message}[/]");
        }

        var modelId = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigModelIdPrompt)
                .DefaultValue(key.Split('/').Last()));

        var maxTokensStr = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigMaxTokensOptionalPrompt)
                .AllowEmpty());

        var contextWindowStr = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.ConfigContextWindowOptionalPrompt)
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
        var added = CliStrings.Format("ConfigModelAddedFormat", Markup.Escape(key));
        AnsiConsole.MarkupLine($"[green]✓[/] {added}");
    }

    private void EditModel(CliConfig config)
    {
        var key = PromptSelection(CliStrings.ConfigSelectModelToEdit, config.Models.Keys);
        if (key == null)
        {
            return;
        }

        var entry = config.Models[key];

        var modelId = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.Format(
                    "ConfigModelIdCurrentFormat",
                    Markup.Escape(entry.ModelId)))
                .DefaultValue(entry.ModelId));

        var currentMaxTokens = entry.MaxTokens?.ToString() ?? CliStrings.CommonDefault;
        var maxTokensStr = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.Format(
                    "ConfigMaxTokensCurrentFormat",
                    currentMaxTokens))
                .AllowEmpty());

        var currentContextWindow = entry.ContextWindow?.ToString() ?? CliStrings.CommonDefault;
        var contextWindowStr = AnsiConsole.Prompt(
            new TextPrompt<string>(CliStrings.Format(
                    "ConfigContextWindowCurrentFormat",
                    currentContextWindow))
                .AllowEmpty());

        entry.ModelId = modelId;

        if (!string.IsNullOrWhiteSpace(maxTokensStr) && int.TryParse(maxTokensStr, out var maxTokens))
            entry.MaxTokens = maxTokens;

        if (!string.IsNullOrWhiteSpace(contextWindowStr) && int.TryParse(contextWindowStr, out var contextWindow))
            entry.ContextWindow = contextWindow;

        var updated = CliStrings.Format("ConfigModelUpdatedFormat", Markup.Escape(key));
        AnsiConsole.MarkupLine($"[green]✓[/] {updated}");
    }

    private void DeleteModel(CliConfig config)
    {
        var key = PromptSelection(CliStrings.ConfigSelectModelToDelete, config.Models.Keys);
        if (key == null)
        {
            return;
        }

        var prompt = CliStrings.Format("ConfigDeleteModelConfirmFormat", Markup.Escape(key));
        if (AnsiConsole.Confirm(prompt))
        {
            config.Models.Remove(key);
            var deleted = CliStrings.Format("ConfigModelDeletedFormat", Markup.Escape(key));
            AnsiConsole.MarkupLine($"[green]✓[/] {deleted}");
        }
    }

    private void SelectPrimaryModel(CliConfig config)
    {
        if (config.Models.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{CliStrings.ConfigNoModels}[/]");
            return;
        }

        var model = PromptSelection(
            CliStrings.Format(
                "ConfigSelectPrimaryModelCurrentFormat",
                Markup.Escape(config.PrimaryModel)),
            config.Models.Keys);
        if (model == null)
        {
            return;
        }

        config.PrimaryModel = model;
        var primarySet = CliStrings.Format("ConfigPrimaryModelSetFormat", Markup.Escape(model));
        AnsiConsole.MarkupLine($"[green]✓[/] {primarySet}");

        // 可选设置副模型
        if (AnsiConsole.Confirm(CliStrings.ConfigConfigureSecondaryModelPrompt, false))
        {
            var summaryModel = PromptSelection(
                CliStrings.ConfigSelectSecondaryModel,
                config.Models.Keys);
            if (summaryModel == null)
            {
                return;
            }

            config.SecondaryModel = summaryModel;
            var secondarySet = CliStrings.Format(
                "ConfigSecondaryModelSetFormat",
                Markup.Escape(summaryModel));
            AnsiConsole.MarkupLine($"[green]✓[/] {secondarySet}");
        }
    }

    private static TAction PromptMenu<TAction>(
        string title,
        params MenuChoice<TAction>[] choices)
        where TAction : struct, Enum
    {
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice<TAction>>()
                .Title(title)
                .UseConverter(choice => choice.Label)
                .AddChoices(choices));

        return selected.Value;
    }

    private static TAction PromptMenu<TAction>(
        string title,
        TAction cancelResult,
        params MenuChoice<TAction>[] choices)
        where TAction : struct, Enum
    {
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice<TAction>>()
                .Title(title)
                .UseConverter(choice => choice.Label)
                .AddChoices(choices)
                .AddCancelResult(new MenuChoice<TAction>(cancelResult, string.Empty)));

        return selected.Value;
    }

    private static string? SelectAdapter(string title)
    {
        var adapter = PromptMenu<AdapterAction>(
            title,
            AdapterAction.Cancel,
            new(AdapterAction.OpenAi, CliStrings.ConfigAdapterOpenAi),
            new(AdapterAction.OpenAiResponse, CliStrings.ConfigAdapterOpenAiResponse),
            new(AdapterAction.Anthropic, CliStrings.ConfigAdapterAnthropic),
            new(AdapterAction.Gemini, CliStrings.ConfigAdapterGemini));

        return adapter switch
        {
            AdapterAction.OpenAi => "openai",
            AdapterAction.OpenAiResponse => "openai-response",
            AdapterAction.Anthropic => "anthropic",
            AdapterAction.Gemini => "gemini",
            AdapterAction.Cancel => null,
            _ => throw new ArgumentOutOfRangeException(nameof(adapter))
        };
    }

    private static string? PromptSelection(string title, IEnumerable<string> choices)
    {
        const string cancelResult = "\0";

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .UseConverter(Markup.Escape)
                .AddChoices(choices)
                .AddCancelResult(cancelResult));

        return selected == cancelResult ? null : selected;
    }

    private sealed record 
        MenuChoice<TAction>(TAction Value, string Label)
        where TAction : struct, Enum;

    private enum ProviderAction
    {
        Add,
        Edit,
        Delete,
        Back
    }

    private enum AdapterAction
    {
        OpenAi,
        OpenAiResponse,
        Anthropic,
        Gemini,
        Cancel
    }

    private enum ModelAction
    {
        Add,
        Edit,
        Delete,
        SelectPrimary,
        Back
    }

    private enum LanguageAction
    {
        Auto,
        English,
        Chinese,
        Cancel
    }
}
