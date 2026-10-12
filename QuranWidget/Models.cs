using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuranWidget;

public sealed class QuranData
{
    [JsonPropertyName("surahs")]
    public List<Surah> Surahs { get; set; } = new();
}

public sealed class Surah
{
    [JsonPropertyName("n")] public int Number { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("en")] public string EnglishName { get; set; } = "";
    [JsonPropertyName("tr")] public string Meaning { get; set; } = "";
    [JsonPropertyName("ayahs")] public List<Verse> Ayahs { get; set; } = new();
}

public sealed class Verse
{
    [JsonPropertyName("s")] public int SurahNumber { get; set; }
    [JsonPropertyName("a")] public int NumberInSurah { get; set; }
    [JsonPropertyName("ar")] public string Arabic { get; set; } = "";
    [JsonPropertyName("en")] public string English { get; set; } = "";

    [JsonIgnore] public int Global { get; set; }
    [JsonIgnore] public Surah Surah { get; set; } = null!;

    public string Ref => $"{SurahNumber}:{NumberInSurah}";
}

public sealed class Passage
{
    public List<Verse> Verses { get; init; } = new();

    public string Reference
    {
        get
        {
            if (Verses.Count == 0) return "";
            if (Verses.Count == 1) return Verses[0].Ref;
            return $"{Verses[0].Ref}-{Verses[^1].NumberInSurah}";
        }
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Write = new() { WriteIndented = true };
}
