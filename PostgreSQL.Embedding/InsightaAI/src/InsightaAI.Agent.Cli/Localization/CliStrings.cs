using System.Globalization;
using System.Resources;

namespace InsightaAI.Agent.Cli.Localization;

public static class CliStrings
{
    private static readonly ResourceManager Resources = new(
        "InsightaAI.Agent.Cli.Resources.CliStrings",
        typeof(CliStrings).Assembly);

    public static string Get(string name, CultureInfo? culture = null)
    {
        return Resources.GetString(name, culture ?? CultureInfo.CurrentUICulture) ?? name;
    }

    public static string Format(string name, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(name), args);
    }

    public static string SessionsDescription => Get(nameof(SessionsDescription));
    public static string SessionsListDescription => Get(nameof(SessionsListDescription));
    public static string SessionsDeleteDescription => Get(nameof(SessionsDeleteDescription));
    public static string SessionsDeleteSessionIdOption => Get(nameof(SessionsDeleteSessionIdOption));
    public static string SessionIdEmpty => Get(nameof(SessionIdEmpty));
    public static string SessionListEmpty => Get(nameof(SessionListEmpty));
    public static string ErrorPrefix => Get(nameof(ErrorPrefix));
    public static string SessionListFieldId => Get(nameof(SessionListFieldId));
    public static string SessionListFieldTitle => Get(nameof(SessionListFieldTitle));
    public static string SessionListFieldProvider => Get(nameof(SessionListFieldProvider));
    public static string SessionListFieldModel => Get(nameof(SessionListFieldModel));
    public static string SessionListFieldMessages => Get(nameof(SessionListFieldMessages));
    public static string SessionListFieldCreatedAt => Get(nameof(SessionListFieldCreatedAt));
    public static string SessionListPageFormat => Get(nameof(SessionListPageFormat));
    public static string SessionListPrevious => Get(nameof(SessionListPrevious));
    public static string SessionListNext => Get(nameof(SessionListNext));
    public static string SessionListQuit => Get(nameof(SessionListQuit));
    public static string SessionListContinueHint => Get(nameof(SessionListContinueHint));
    public static string CommonCancelled => Get(nameof(CommonCancelled));
    public static string ScopeGlobal => Get(nameof(ScopeGlobal));
    public static string ScopeProject => Get(nameof(ScopeProject));
    public static string ScopeOptionDescription => Get(nameof(ScopeOptionDescription));
    public static string ScopeOptionDescriptionWithDefault => Get(nameof(ScopeOptionDescriptionWithDefault));
    public static string McpDescription => Get(nameof(McpDescription));
    public static string McpListDescription => Get(nameof(McpListDescription));
    public static string McpAddDescription => Get(nameof(McpAddDescription));
    public static string McpRemoveDescription => Get(nameof(McpRemoveDescription));
    public static string McpNameArgumentDescription => Get(nameof(McpNameArgumentDescription));
    public static string McpTransportOptionDescription => Get(nameof(McpTransportOptionDescription));
    public static string McpCommandOptionDescription => Get(nameof(McpCommandOptionDescription));
    public static string McpArgsOptionDescription => Get(nameof(McpArgsOptionDescription));
    public static string McpUrlOptionDescription => Get(nameof(McpUrlOptionDescription));
    public static string McpDescriptionOptionDescription => Get(nameof(McpDescriptionOptionDescription));
    public static string McpListEmpty => Get(nameof(McpListEmpty));
    public static string McpListFieldName => Get(nameof(McpListFieldName));
    public static string McpListFieldTransport => Get(nameof(McpListFieldTransport));
    public static string McpListFieldEndpoint => Get(nameof(McpListFieldEndpoint));
    public static string McpListFieldDescription => Get(nameof(McpListFieldDescription));
    public static string McpSseUrlRequired => Get(nameof(McpSseUrlRequired));
    public static string McpStdioCommandRequired => Get(nameof(McpStdioCommandRequired));
    public static string SkillsDescription => Get(nameof(SkillsDescription));
    public static string SkillsListDescription => Get(nameof(SkillsListDescription));
    public static string SkillsInstallDescription => Get(nameof(SkillsInstallDescription));
    public static string SkillsUninstallDescription => Get(nameof(SkillsUninstallDescription));
    public static string SkillsPathArgumentDescription => Get(nameof(SkillsPathArgumentDescription));
    public static string SkillsNameArgumentDescription => Get(nameof(SkillsNameArgumentDescription));
    public static string SkillsListEmpty => Get(nameof(SkillsListEmpty));
    public static string SkillsListFieldName => Get(nameof(SkillsListFieldName));
    public static string SkillsListFieldDescription => Get(nameof(SkillsListFieldDescription));
    public static string SkillManifestMissing => Get(nameof(SkillManifestMissing));
    public static string SkillManifestInvalid => Get(nameof(SkillManifestInvalid));
}
