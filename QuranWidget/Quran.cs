using System.Reflection;
using System.Text;
using System.Text.Json;

namespace QuranWidget;

public static class Quran
{
    public static QuranData Data { get; private set; } = new();
    public static List<Passage> Pool { get; private set; } = new();
    public static int VerseCount { get; private set; }

    public static void Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("QuranWidget.Assets.Data.quran.json")
            ?? throw new InvalidOperationException("Embedded Quran data not found");
        Data = JsonSerializer.Deserialize<QuranData>(stream, Json.Read) ?? new QuranData();

        int g = 1;
        foreach (var s in Data.Surahs)
        {
            foreach (var a in s.Ayahs)
            {
                a.Global = g++;
                a.Surah = s;
            }
        }
        VerseCount = g - 1;

        Pool = new List<Passage>();
        foreach (var (s, a, b) in PoolEntries)
        {
            var p = GetPassage(s, a, b);
            if (p.Verses.Count > 0) Pool.Add(p);
        }
    }

    public static Surah? Surah(int n) =>
        n >= 1 && n <= Data.Surahs.Count ? Data.Surahs[n - 1] : null;

    public static Passage GetPassage(int s, int a, int b = -1)
    {
        var result = new Passage();
        var surah = Surah(s);
        if (surah == null) return result;
        if (b < a) b = a;
        a = Math.Max(1, a);
        b = Math.Min(b, surah.Ayahs.Count);
        for (int i = a; i <= b; i++) result.Verses.Add(surah.Ayahs[i - 1]);
        return result;
    }

    public static Passage DailyPassage(DateTime date)
    {
        if (Pool.Count == 0) return new Passage();
        var seed = date.Year * 10000 + date.Month * 100 + date.Day;
        var rng = new Random(seed);
        return Pool[rng.Next(Pool.Count)];
    }

    public static Passage RandomPassage(Passage? exclude = null)
    {
        if (Pool.Count == 0) return new Passage();
        for (int tries = 0; tries < 8; tries++)
        {
            var p = Pool[Random.Shared.Next(Pool.Count)];
            if (exclude == null || p.Reference != exclude.Reference) return p;
        }
        return Pool[Random.Shared.Next(Pool.Count)];
    }

    public static string ArabicOf(Passage p)
    {
        // \u00A0 non-breaking space + \u2060 word joiners keep the ornate
        // verse-number marker ﴿٢٥﴾ on one line — it must never split.
        return string.Join("\n", p.Verses.Select(v =>
            $"{v.Arabic}\u00A0\u2060\uFD3F\u2060{ToArabicDigits(v.NumberInSurah)}\u2060\uFD3E\u2060"));
    }

    public static string EnglishOf(Passage p) => Translations.EnglishOf(p);

    public static string ToArabicDigits(int n)
    {
        const string digits = "٠١٢٣٤٥٦٧٨٩";
        var sb = new StringBuilder();
        foreach (var ch in n.ToString())
            sb.Append(char.IsAsciiDigit(ch) ? digits[ch - '0'] : ch);
        return sb.ToString();
    }

    // A curated pool of beloved, well-known passages: (surah, from ayah, to ayah)
    static readonly (int, int, int)[] PoolEntries =
    {
        (1,1,7),
        (2,2,5),(2,152,153),(2,153,157),(2,186,186),(2,216,216),(2,255,255),(2,285,286),
        (3,8,8),(3,26,27),(3,139,139),(3,173,173),(3,190,191),(3,200,200),
        (7,23,23),(7,56,56),(7,199,199),
        (8,2,4),
        (9,40,40),(9,51,51),(9,129,129),
        (10,57,58),
        (12,87,87),
        (13,11,11),(13,28,28),
        (14,7,7),
        (15,9,9),
        (16,90,90),(16,97,97),(16,128,128),
        (17,23,24),(17,80,81),
        (18,10,10),
        (19,96,96),
        (20,25,28),(20,114,114),
        (21,87,87),(21,107,107),
        (23,97,98),
        (24,35,35),
        (25,63,63),(25,74,74),
        (28,24,24),
        (29,2,3),(29,69,69),
        (30,21,21),
        (31,17,17),
        (33,70,71),
        (36,58,58),
        (39,53,53),
        (40,60,60),
        (41,33,33),(41,34,35),
        (49,13,13),
        (50,16,16),
        (51,56,56),
        (54,17,17),
        (55,26,27),
        (57,4,4),
        (59,22,24),
        (65,2,3),
        (67,1,2),
        (79,40,41),
        (89,27,30),
        (91,9,10),
        (92,5,7),
        (93,4,8),
        (94,1,8),
        (95,4,4),
        (96,1,5),
        (97,1,5),
        (99,7,8),
        (103,1,3),
        (108,1,3),
        (110,1,3),
        (112,1,4),
        (113,1,5),
        (114,1,6),
    };
}
