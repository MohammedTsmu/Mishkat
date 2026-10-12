namespace QuranWidget;

// Tiny two-language helper: every visible string is written as
// T("English", "العربية") and resolves by the language chosen in settings.
public static class L10n
{
    public static bool ArabicUI => SettingsService.Current.Language == "ar";

    public static string T(string en, string ar) => ArabicUI ? ar : en;

    public static string Prayer(string name) => name switch
    {
        "Fajr" => T("Fajr", "الفجر"),
        "Sunrise" => T("Sunrise", "الشروق"),
        "Dhuhr" => T("Dhuhr", "الظهر"),
        "Asr" => T("Asr", "العصر"),
        "Maghrib" => T("Maghrib", "المغرب"),
        "Isha" => T("Isha", "العشاء"),
        _ => name,
    };
}
