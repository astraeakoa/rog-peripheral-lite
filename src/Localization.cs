using System.Globalization;
using System.Resources;

namespace GHelper;

public static class L
{
    static readonly ResourceManager Resources = new("GHelper.Properties.AppStrings", typeof(L).Assembly);

    // Set before constructing any windows or accessing translated peripheral resources.
    public static void Initialize()
    {
        string? language = AppConfig.GetString("language");
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            if (!string.IsNullOrWhiteSpace(language)) culture = CultureInfo.GetCultureInfo(language);
            if (culture.Name == "kr") culture = CultureInfo.GetCultureInfo("ko");
        }
        catch (CultureNotFoundException) { Logger.WriteLine("Unknown Language: " + language); }
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Text(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentUICulture, Text(key), args);
}
