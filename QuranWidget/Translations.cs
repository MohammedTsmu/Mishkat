using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace QuranWidget;

// Translation management. "en.sahih" is embedded in quran.json; other editions are
// downloaded once from api.alquran.cloud, cached locally and parsed into memory.
public static class Translations
{
    public static readonly (string Id, string Name, string ArName)[] Available =
    {
        ("en.sahih", "Saheeh International", "الترجمة الصحيحة"),
        ("en.pickthall", "Pickthall", "بيكثال"),
        ("en.yusufali", "Yusuf Ali", "يوسف علي"),
        ("en.asad", "Muhammad Asad", "محمد أسد"),
        ("en.hilali", "Hilali & Khan", "الهلالي وخان"),
    };

    public static string DisplayName(int index)
    {
        var t = Available[Math.Clamp(index, 0, Available.Length - 1)];
        return L10n.T(t.Name, t.ArName);
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static Dictionary<int, string[]>? _verses;

    public static string CurrentId => SettingsService.Current.TranslationId;

    static string CachePath(string id)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuranWidget", "translations");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, id + ".json");
    }

    public static bool IsDownloaded(string id) => id == "en.sahih" || File.Exists(CachePath(id));

    public static async Task<bool> DownloadAsync(string id)
    {
        if (IsDownloaded(id)) return true;
        try
        {
            var json = await Http.GetStringAsync($"https://api.alquran.cloud/v1/quran/{id}");
            await File.WriteAllTextAsync(CachePath(id), json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Loads the edition into memory; falls back to the embedded Saheeh text.
    public static void Load(string id)
    {
        try
        {
            if (id != "en.sahih" && File.Exists(CachePath(id)))
            {
                var parsed = ParseApiJson(File.ReadAllText(CachePath(id)));
                if (parsed != null) { _verses = parsed; return; }
            }
        }
        catch
        {
            // fall through to the embedded edition
        }
        _verses = Embedded();
    }

    static Dictionary<int, string[]> Embedded()
    {
        var d = new Dictionary<int, string[]>();
        foreach (var s in Quran.Data.Surahs)
            d[s.Number] = s.Ayahs.Select(a => a.English).ToArray();
        return d;
    }

    static Dictionary<int, string[]>? ParseApiJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("surahs", out var surahs)) return null;
        var d = new Dictionary<int, string[]>();
        foreach (var s in surahs.EnumerateArray())
        {
            int surahNo = s.GetProperty("number").GetInt32();
            var list = new List<string>();
            foreach (var a in s.GetProperty("ayahs").EnumerateArray())
            {
                string text = a.GetProperty("text").GetString() ?? "";
                int n = a.GetProperty("numberInSurah").GetInt32();
                // some editions prefix ayah 1 with the Bismillah; strip it
                if (n == 1 && surahNo != 1 && surahNo != 9)
                    text = StripBismillah(text);
                list.Add(text);
            }
            d[surahNo] = list.ToArray();
        }
        return d.Count == 114 ? d : null;
    }

    static string StripBismillah(string text)
    {
        foreach (var marker in new[]
        {
            "the Gracious, the Merciful. ", "the Most Gracious, the Most Merciful. ",
            "the Beneficent, the Merciful. ", "the Merciful, the Compassionate. ",
        })
        {
            int i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && i < 60) return text[(i + marker.Length)..].TrimStart();
        }
        return text;
    }

    public static string EnglishOf(Passage p)
    {
        if (_verses == null) Load(CurrentId);
        var parts = new List<string>();
        foreach (var v in p.Verses)
        {
            if (_verses != null && _verses.TryGetValue(v.SurahNumber, out var arr) &&
                v.NumberInSurah >= 1 && v.NumberInSurah <= arr.Length)
                parts.Add(arr[v.NumberInSurah - 1]);
            else
                parts.Add(v.English);
        }
        return string.Join(" ", parts);
    }

    public sealed record SearchHit(int S, int A, string Text);

    static string[] ArabicOfSurah(int surahNo)
    {
        var s = Quran.Surah(surahNo);
        return s == null ? Array.Empty<string>() : s.Ayahs.Select(a => a.Arabic).ToArray();
    }

    // Removes harakat/tanwin and normalizes letter variants (alef wasla, hamza
    // seats, alif maqsura, ta marbuta) so Arabic typed on a normal keyboard —
    // without any harakat — still matches the vocalized Quran text.
    public static string StripArabicDiacritics(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if ((c >= '\u064B' && c <= '\u065F') || c == '\u0670' ||
                (c >= '\u06D6' && c <= '\u06ED') || c == '\u0640')
                continue;
            sb.Append(c switch
            {
                '\u0671' => '\u0627', // alef wasla -> alef
                '\u0623' or '\u0625' or '\u0622' => '\u0627', // hamza seats -> alef
                '\u0649' => '\u064A', // alif maqsura -> ya
                '\u0629' => '\u0647', // ta marbuta -> ha
                _ => c,
            });
        }
        return sb.ToString();
    }

    // Searches the current English translation and the Arabic text; Arabic input
    // is matched without requiring diacritics.
    public static List<SearchHit> Search(string query, int max = 12)
    {
        if (_verses == null) Load(CurrentId);
        var hits = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(query) || _verses == null) return hits;
        bool isArabic = query.Any(c => c is >= '\u0600' and <= '\u06FF');
        string needle = isArabic ? StripArabicDiacritics(query) : query.Trim();

        foreach (var (surah, texts) in _verses.OrderBy(k => k.Key))
        {
            var arabic = isArabic ? ArabicOfSurah(surah) : null;
            for (int i = 0; i < texts.Length; i++)
            {
                string hay = isArabic ? StripArabicDiacritics(arabic![i]) : texts[i];
                int idx = hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                string raw = isArabic ? arabic![i] : texts[i];
                int start = Math.Max(0, idx - 28);
                int len = Math.Min(raw.Length - start, 90);
                string snippet = (start > 0 ? "…" : "") + raw.Substring(start, len) + (start + len < raw.Length ? "…" : "");
                hits.Add(new SearchHit(surah, i + 1, snippet));
                if (hits.Count >= max) return hits;
            }
        }
        return hits;
    }
}
