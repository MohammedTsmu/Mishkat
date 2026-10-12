namespace QuranWidget;

// Tiny two-language helper: every visible string is written as
// T("English", "العربية") and resolves by the language chosen in settings.
public static class L10n
{
    public static bool ArabicUI => SettingsService.Current.Language == "ar";

    public static string T(string en, string ar) => ArabicUI ? ar : en;

    public static string Ar(int n) => Quran.ToArabicDigits(n);

    // "Ayah 7" / "Ayahs 1–7" / "الآية ٧" / "الآيات ١–٧"
    public static string AyahsLabel(int from, int? to = null)
    {
        if (to == null || to <= from) return T($"Ayah {from}", $"الآية {Ar(from)}");
        return T($"Ayahs {from}–{to}", $"الآيات {Ar(from)}–{Ar(to ?? from)}");
    }

    // "Surah 1 · Al-Fatihah — Ayah 7" / "سورة الفاتحة — الآية ٧"
    public static string SurahRef(int surah, int from, int? to = null)
    {
        var s = Quran.Surah(surah);
        string name = s == null ? "" : T(s.EnglishName, s.Name);
        return T($"Surah {surah} · {name} — {AyahsLabel(from, to)}",
                 $"سورة {name} — {AyahsLabel(from, to)}");
    }

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
