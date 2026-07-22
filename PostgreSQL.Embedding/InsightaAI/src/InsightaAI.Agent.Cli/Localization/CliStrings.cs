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
}
