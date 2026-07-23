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
    public static string CommonBack => Get(nameof(CommonBack));
    public static string CommonNone => Get(nameof(CommonNone));
    public static string CommonDefault => Get(nameof(CommonDefault));
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
    public static string ConfigDescription => Get(nameof(ConfigDescription));
    public static string ConfigProviderDescription => Get(nameof(ConfigProviderDescription));
    public static string ConfigModelDescription => Get(nameof(ConfigModelDescription));
    public static string ConfigLanguageDescription => Get(nameof(ConfigLanguageDescription));
    public static string ConfigLanguagePrompt => Get(nameof(ConfigLanguagePrompt));
    public static string ConfigLanguageAuto => Get(nameof(ConfigLanguageAuto));
    public static string ConfigLanguageEnglish => Get(nameof(ConfigLanguageEnglish));
    public static string ConfigLanguageChinese => Get(nameof(ConfigLanguageChinese));
    public static string ConfigSelectPrimaryModel => Get(nameof(ConfigSelectPrimaryModel));
    public static string ConfigSaved => Get(nameof(ConfigSaved));
    public static string ConfigMenuNavigationHint => Get(nameof(ConfigMenuNavigationHint));
    public static string ConfigProviderManagementTitle => Get(nameof(ConfigProviderManagementTitle));
    public static string ConfigAddProvider => Get(nameof(ConfigAddProvider));
    public static string ConfigEditProvider => Get(nameof(ConfigEditProvider));
    public static string ConfigDeleteProvider => Get(nameof(ConfigDeleteProvider));
    public static string ConfigProviderNamePrompt => Get(nameof(ConfigProviderNamePrompt));
    public static string ConfigSelectAdapter => Get(nameof(ConfigSelectAdapter));
    public static string ConfigAdapterOpenAi => Get(nameof(ConfigAdapterOpenAi));
    public static string ConfigAdapterOpenAiResponse => Get(nameof(ConfigAdapterOpenAiResponse));
    public static string ConfigAdapterAnthropic => Get(nameof(ConfigAdapterAnthropic));
    public static string ConfigAdapterGemini => Get(nameof(ConfigAdapterGemini));
    public static string ConfigApiKeyPrompt => Get(nameof(ConfigApiKeyPrompt));
    public static string ConfigBaseUrlOptionalPrompt => Get(nameof(ConfigBaseUrlOptionalPrompt));
    public static string ConfigSelectProviderToEdit => Get(nameof(ConfigSelectProviderToEdit));
    public static string ConfigApiKeyKeepPrompt => Get(nameof(ConfigApiKeyKeepPrompt));
    public static string ConfigSelectProviderToDelete => Get(nameof(ConfigSelectProviderToDelete));
    public static string ConfigModelManagementTitle => Get(nameof(ConfigModelManagementTitle));
    public static string ConfigAddModel => Get(nameof(ConfigAddModel));
    public static string ConfigEditModel => Get(nameof(ConfigEditModel));
    public static string ConfigDeleteModel => Get(nameof(ConfigDeleteModel));
    public static string ConfigModelReferencePrompt => Get(nameof(ConfigModelReferencePrompt));
    public static string ConfigModelIdPrompt => Get(nameof(ConfigModelIdPrompt));
    public static string ConfigMaxTokensOptionalPrompt => Get(nameof(ConfigMaxTokensOptionalPrompt));
    public static string ConfigContextWindowOptionalPrompt => Get(nameof(ConfigContextWindowOptionalPrompt));
    public static string ConfigSelectModelToEdit => Get(nameof(ConfigSelectModelToEdit));
    public static string ConfigSelectModelToDelete => Get(nameof(ConfigSelectModelToDelete));
    public static string ConfigNoModels => Get(nameof(ConfigNoModels));
    public static string ConfigConfigureSecondaryModelPrompt => Get(nameof(ConfigConfigureSecondaryModelPrompt));
    public static string ConfigSelectSecondaryModel => Get(nameof(ConfigSelectSecondaryModel));
}
