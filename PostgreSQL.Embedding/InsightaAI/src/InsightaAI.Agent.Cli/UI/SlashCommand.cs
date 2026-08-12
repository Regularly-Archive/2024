namespace InsightaAI.Agent.Cli.UI;

/// <summary>
/// A chat slash command available to the interactive prompt.
/// </summary>
public sealed record SlashCommand(string Name, string Description, bool AcceptsArgument = false);
