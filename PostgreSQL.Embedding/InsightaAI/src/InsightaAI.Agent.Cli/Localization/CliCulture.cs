using System.Globalization;

namespace InsightaAI.Agent.Cli.Localization;

public static class CliCulture
{
    public const string Auto = "auto";
    public const string English = "en-US";
    public const string Chinese = "zh-CN";

    public static void Configure(string? language)
    {
        var culture = Resolve(language, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static CultureInfo Resolve(string? language, CultureInfo? systemCulture = null)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            string.Equals(language, Auto, StringComparison.OrdinalIgnoreCase))
        {
            var current = systemCulture ?? CultureInfo.CurrentUICulture;
            return current.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo(Chinese)
                : CultureInfo.GetCultureInfo(English);
        }

        if (language.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            language.Equals(Chinese, StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo(Chinese);
        }

        if (language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            language.Equals(English, StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo(English);
        }

        return CultureInfo.GetCultureInfo(English);
    }
}
