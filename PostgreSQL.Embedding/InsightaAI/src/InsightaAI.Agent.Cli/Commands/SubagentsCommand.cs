using System.CommandLine;
using System.Text.Json;
using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.Agents.Subagents.Definitions;
using Spectre.Console;

namespace InsightaAI.Agent.Cli.Commands;

/// <summary>Manages globally reusable subagent definitions through the store abstraction.</summary>
public sealed class SubagentsCommand
{
    private readonly ISubagentDefinitionStore _store;
    private readonly CliConfig _config;

    public SubagentsCommand(ISubagentDefinitionStore store, CliConfig config)
    {
        _store = store;
        _config = config;
    }

    public Command Create()
    {
        var command = new Command("subagents", Text("SubagentsDescription"));

        var list = new Command("list", Text("SubagentsListDescription"));
        list.SetHandler(ListAsync);

        var init = new Command("init", Text("SubagentsInitDescription"));
        init.SetHandler(InitializeAsync);

        var id = new Argument<string>("id", Text("SubagentsIdArgumentDescription"));
        var name = new Option<string?>("--name", Text("SubagentsNameOptionDescription"));
        var description = new Option<string?>("--description", Text("SubagentsDescriptionOptionDescription"));
        var model = new Option<string?>("--model", Text("SubagentsModelOptionDescription"));
        var instructions = new Option<string?>("--instructions", Text("SubagentsInstructionsOptionDescription"));
        var tools = new Option<string[]>("--tool", Text("SubagentsToolOptionDescription"))
        {
            AllowMultipleArgumentsPerToken = true
        };

        var create = new Command("create", Text("SubagentsCreateDescription"));
        create.AddArgument(id);
        create.AddOption(name);
        create.AddOption(description);
        create.AddOption(model);
        create.AddOption(instructions);
        create.AddOption(tools);
        create.SetHandler((agentId, agentName, agentDescription, modelReference, customInstructions, toolNames) =>
            CreateAsync(agentId, agentName, agentDescription, modelReference, customInstructions, toolNames),
            id, name, description, model, instructions, tools);

        var removeId = new Argument<string>("id", Text("SubagentsIdArgumentDescription"));
        var yes = new Option<bool>("--yes", Text("SubagentsYesOptionDescription"));
        var remove = new Command("remove", Text("SubagentsRemoveDescription"));
        remove.AddArgument(removeId);
        remove.AddOption(yes);
        remove.SetHandler((agentId, confirmed) => RemoveAsync(agentId, confirmed), removeId, yes);

        var validateId = new Argument<string?>("id", () => null, Text("SubagentsOptionalIdArgumentDescription"));
        var validate = new Command("validate", Text("SubagentsValidateDescription"));
        validate.AddArgument(validateId);
        validate.SetHandler(ValidateAsync, validateId);

        command.AddCommand(list);
        command.AddCommand(init);
        command.AddCommand(create);
        command.AddCommand(remove);
        command.AddCommand(validate);
        return command;
    }

    private async Task ListAsync()
    {
        var definitions = new List<SubagentDefinition>();
        await foreach (var definition in _store.ListAsync())
            definitions.Add(definition);

        if (definitions.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]{Text("SubagentsListEmpty")}[/]");
            return;
        }

        var table = new Table()
            .AddColumn(Text("SubagentsFieldId"))
            .AddColumn(Text("SubagentsFieldName"))
            .AddColumn(Text("SubagentsFieldModel"))
            .AddColumn(Text("SubagentsFieldTools"))
            .AddColumn(Text("SubagentsFieldDescription"))
            .Border(TableBorder.Rounded);

        foreach (var definition in definitions)
        {
            var insighta = definition as InsightaSubagentDefinition;
            table.AddRow(
                new Text(definition.Id),
                new Text(definition.Name),
                new Text(insighta?.Model ?? Text("CommonDefault")),
                new Text(string.Join(", ", insighta?.ToolNames ?? [])),
                new Text(definition.Description ?? string.Empty));
        }

        AnsiConsole.Write(table);
    }

    private async Task InitializeAsync()
    {
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "subagent-templates");
        if (!Directory.Exists(templateRoot))
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {Text("SubagentsTemplatesMissing")}[/]");
            return;
        }

        foreach (var path in Directory.GetFiles(templateRoot, "subagent.json", SearchOption.AllDirectories))
        {
            var definition = JsonSerializer.Deserialize<InsightaSubagentDefinition>(
                await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (definition is null)
                continue;

            if (await _store.FindAsync(definition.Id) is not null)
            {
                AnsiConsole.MarkupLine($"[dim]{CliStrings.Format("SubagentsInitSkippedFormat", Markup.Escape(definition.Id))}[/]");
                continue;
            }

            await _store.CreateAsync(definition);
            AnsiConsole.MarkupLine($"[green]✓[/] {CliStrings.Format("SubagentsInitInstalledFormat", Markup.Escape(definition.Id))}");
        }
    }

    private async Task CreateAsync(
        string id,
        string? name,
        string? description,
        string? model,
        string? instructions,
        string[] toolNames)
    {
        if (!ValidateModelReference(model))
            return;

        try
        {
            await _store.CreateAsync(new InsightaSubagentDefinition
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Description = description ?? string.Empty,
                Model = model,
                Instructions = instructions ?? string.Empty,
                ToolNames = toolNames ?? []
            });
            AnsiConsole.MarkupLine($"[green]✓[/] {CliStrings.Format("SubagentsCreatedFormat", Markup.Escape(id))}");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {Markup.Escape(exception.Message)}[/]");
        }
    }

    private async Task RemoveAsync(string id, bool yes)
    {
        if (!yes && !AnsiConsole.Confirm(CliStrings.Format("SubagentsRemoveConfirmFormat", Markup.Escape(id)), false))
        {
            AnsiConsole.MarkupLine($"[yellow]{CliStrings.CommonCancelled}[/]");
            return;
        }

        try
        {
            if (!await _store.DeleteAsync(id))
            {
                AnsiConsole.MarkupLine($"[yellow]{CliStrings.Format("SubagentsNotFoundFormat", Markup.Escape(id))}[/]");
                return;
            }
            AnsiConsole.MarkupLine($"[green]✓[/] {CliStrings.Format("SubagentsRemovedFormat", Markup.Escape(id))}");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {Markup.Escape(exception.Message)}[/]");
        }
    }

    private async Task ValidateAsync(string? id)
    {
        var definitions = new List<SubagentDefinition>();
        if (!string.IsNullOrWhiteSpace(id))
        {
            var definition = await _store.FindAsync(id);
            if (definition is null)
            {
                AnsiConsole.MarkupLine($"[yellow]{CliStrings.Format("SubagentsNotFoundFormat", Markup.Escape(id))}[/]");
                return;
            }
            definitions.Add(definition);
        }
        else
        {
            await foreach (var definition in _store.ListAsync())
                definitions.Add(definition);
        }

        foreach (var definition in definitions)
        {
            var error = GetValidationError(definition);
            var message = error is null ? Text("SubagentsValidationPassed") : error;
            var color = error is null ? "green" : "red";
            AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(definition.Id)}: {Markup.Escape(message)}[/]");
        }
    }

    private bool ValidateModelReference(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return true;

        try
        {
            CliConfig.ParseModelReference(model);
            if (!_config.Models.ContainsKey(model))
                throw new InvalidOperationException(CliStrings.Format("SubagentsModelNotConfiguredFormat", model));
            return true;
        }
        catch (InvalidOperationException exception)
        {
            AnsiConsole.MarkupLine($"[red]{CliStrings.ErrorPrefix}: {Markup.Escape(exception.Message)}[/]");
            return false;
        }
    }

    private string? GetValidationError(SubagentDefinition definition)
    {
        if (definition is not InsightaSubagentDefinition insighta)
            return Text("SubagentsUnsupportedDefinition");
        if (!ValidateModelReferenceForValidation(insighta.Model, out var error))
            return error;
        if ((insighta.ToolNames ?? []).Any(string.IsNullOrWhiteSpace))
            return Text("SubagentsInvalidToolNames");
        return null;
    }

    private bool ValidateModelReferenceForValidation(string? model, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(model))
            return true;
        try
        {
            CliConfig.ParseModelReference(model);
            if (!_config.Models.ContainsKey(model))
                error = CliStrings.Format("SubagentsModelNotConfiguredFormat", model);
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
        }
        return error is null;
    }

    private static string Text(string key) => CliStrings.Get(key);
}
