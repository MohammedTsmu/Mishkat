using System.Windows;
using System.Windows.Media;

namespace QuranWidget;

// Applies one of four curated themes by replacing the application-wide brushes.
// Every themed surface in the XAML references these via DynamicResource.
public static class ThemeService
{
    public static readonly (string Id, string Name, string ArName)[] Themes =
    {
        ("emerald", "Emerald Night", "ليل زمردي"),
        ("parchment", "Parchment Light", "رقّ فاتح"),
        ("midnight", "Midnight Blue", "أزرق ليلي"),
        ("sepia", "Desert Sepia", "سيبيا صحراوي"),
    };

    public static string DisplayName(int index) =>
        L10n.T(Themes[Math.Clamp(index, 0, Themes.Length - 1)].Name,
               Themes[Math.Clamp(index, 0, Themes.Length - 1)].ArName);

    public static string NameOf(string id) => DisplayName(Array.FindIndex(Themes, t => t.Id == id));

    sealed class Palette
    {
        public string Gold = "", GoldDim = "", TextPrimary = "", TextSecondary = "",
                      TextFaint = "", IconGlyph = "";
        public string Card1 = "", Card2 = "", Card3 = "";
        public string Border1 = "", Border2 = "", Border3 = "";
        public string Surface = "", SurfaceBorder = "";
        public string Accent = "";
        public uint AcrylicTint; // ABGR for SetWindowCompositionAttribute; alpha>0 enables
    }

    static readonly Palette Emerald = new()
    {
        Gold = "#E8C877", GoldDim = "#C9A96B", TextPrimary = "#F7F1E3",
        TextSecondary = "#CFC7B4", TextFaint = "#938C78", IconGlyph = "#E4DCC8",
        Card1 = "#F2172019", Card2 = "#F21E2A21", Card3 = "#F1121A15",
        Border1 = "#59CBA86E", Border2 = "#1FCBA86E", Border3 = "#4FCBA86E",
        Surface = "#F51C261F", SurfaceBorder = "#4DC9A96B", Accent = "#C9A96B",
        AcrylicTint = 0x9E121410,
    };

    static readonly Palette Parchment = new()
    {
        Gold = "#9C7426", GoldDim = "#A88F5C", TextPrimary = "#3A3226",
        TextSecondary = "#6B5F49", TextFaint = "#9C9078", IconGlyph = "#5E5340",
        Card1 = "#F7F4EA", Card2 = "#F1EBDA", Card3 = "#F7F2E6",
        Border1 = "#8FB3926B", Border2 = "#33B3926B", Border3 = "#70B3926B",
        Surface = "#F2ECDC", SurfaceBorder = "#5FA88F5C", Accent = "#A88F5C",
        AcrylicTint = 0, // no acrylic on the light theme
    };

    static readonly Palette Midnight = new()
    {
        Gold = "#9FC3E8", GoldDim = "#6B8CBA", TextPrimary = "#EAF0FA",
        TextSecondary = "#B9C6DE", TextFaint = "#7887A3", IconGlyph = "#D8E2F2",
        Card1 = "#F20F1626", Card2 = "#F216203A", Card3 = "#F10C1220",
        Border1 = "#596B8CBA", Border2 = "#1F6B8CBA", Border3 = "#4F6B8CBA",
        Surface = "#F5161D33", SurfaceBorder = "#4D6B8CBA", Accent = "#6B8CBA",
        AcrylicTint = 0x9E10141E,
    };

    static readonly Palette Sepia = new()
    {
        Gold = "#E0B880", GoldDim = "#B08F5F", TextPrimary = "#F5EDE0",
        TextSecondary = "#CDBBA0", TextFaint = "#94836B", IconGlyph = "#E8DBC4",
        Card1 = "#F2201812", Card2 = "#F22A2018", Card3 = "#F11A130E",
        Border1 = "#59CBA077", Border2 = "#1FCBA077", Border3 = "#4FCBA077",
        Surface = "#F5241B12", SurfaceBorder = "#4DCBA077", Accent = "#B08F5F",
        AcrylicTint = 0x9E181008,
    };

    static Palette Current = Emerald;

    public static uint AcrylicTint => Current.AcrylicTint;

    static Palette For(string id) => id switch
    {
        "parchment" => Parchment,
        "midnight" => Midnight,
        "sepia" => Sepia,
        _ => Emerald,
    };

    public static void Apply(string id)
    {
        var p = For(id);
        Current = p;
        var r = System.Windows.Application.Current.Resources;
        Set(r, "Gold", p.Gold);
        Set(r, "GoldDim", p.GoldDim);
        Set(r, "TextPrimary", p.TextPrimary);
        Set(r, "TextSecondary", p.TextSecondary);
        Set(r, "TextFaint", p.TextFaint);
        Set(r, "IconGlyph", p.IconGlyph);
        Set(r, "Card1", p.Card1);
        Set(r, "Card2", p.Card2);
        Set(r, "Card3", p.Card3);
        Set(r, "Border1", p.Border1);
        Set(r, "Border2", p.Border2);
        Set(r, "Border3", p.Border3);
        Set(r, "Surface", p.Surface);
        Set(r, "SurfaceBorder", p.SurfaceBorder);
        Set(r, "Accent", p.Accent);

        // alpha variants of the accent, used for hovers, tracks, dividers
        foreach (var (key, alpha) in new[]
        {
            ("A00", 0x00), ("A12", 0x12), ("A19", 0x19), ("A22", 0x22), ("A24", 0x24),
            ("A26", 0x26), ("A2A", 0x2A), ("A2E", 0x2E), ("A33", 0x33),
            ("A38", 0x38), ("A3F", 0x3F), ("A4D", 0x4D), ("A59", 0x59),
            ("A66", 0x66), ("A80", 0x80), ("AA8", 0xA8),
        })
        {
            Set(r, key, WithAlpha(p.Accent, (byte)alpha));
        }

        // hover overlays: white on dark themes, ink on the light theme
        bool light = id == "parchment";
        Set(r, "Hover", light ? "#26000000" : "#26FFFFFF");
        Set(r, "HoverSoft", light ? "#12000000" : "#14FFFFFF");
        Set(r, "Shadow", light ? "#5E3A2E1C" : "#000000");
    }

    static string WithAlpha(string hex, byte a) => $"#{a:X2}{hex.TrimStart('#')}";

    static void Set(ResourceDictionary r, string key, string color)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;          // brush (for Foreground/Fill/Stroke/Background)
        r[key + "Color"] = c; // color (for GradientStop.Color / DropShadowEffect.Color)
        if (r[key + "Color"] is System.Windows.Media.Color cc) { } // keep compiler contentment
    }
}

