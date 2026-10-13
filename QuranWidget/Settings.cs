using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace QuranWidget;

public sealed class WidgetState
{
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    public int S { get; set; } = -1;
    public int A { get; set; } = -1;
    public string Day { get; set; } = "";
    public string Screen { get; set; } = "";
}

public sealed class Favorite
{
    public int S { get; set; }
    public int A { get; set; }
    public int B { get; set; }
}

public sealed class Settings
{
    public string ReciterId { get; set; } = "ar.alafasy";
    public double Opacity { get; set; } = 1.0;
    public bool AlwaysOnTop { get; set; }
    public bool PinToDesktop { get; set; }
    public bool LaunchAtStartup { get; set; }
    public string VoiceName { get; set; } = "";

    // Continuous reading: 0 = stop at the end of the surah, 1/2/3 = after 10/25/50
    // verses, 4 = keep going until the user stops it.
    public bool ContinueReading { get; set; }
    public int StopAt { get; set; }

    // Appearance
    public string Theme { get; set; } = "emerald";
    public double ArabicFontSize { get; set; } = 24;

    // Translations ("en.sahih" embedded; others downloaded on demand)
    public string TranslationId { get; set; } = "en.sahih";

    // Sleep timer for recitation (minutes, 0 = off)
    public int SleepMinutes { get; set; }

    // Prayer times
    public bool PrayerEnabled { get; set; }
    public double Latitude { get; set; } = double.NaN;
    public double Longitude { get; set; } = double.NaN;
    public string CityName { get; set; } = "";

    // UI language: "en" or "ar"
    public string Language { get; set; } = "en";

    // Whether the translation panel is shown under the Arabic text
    public bool ShowTranslation { get; set; } = true;

    public List<Favorite> Favorites { get; set; } = new();

    // Hifz (memorization) session preferences
    public int HifzSurah { get; set; } = 1;
    public int HifzFrom { get; set; } = 1;
    public int HifzTo { get; set; } = 3;
    public int HifzReps { get; set; } = 7;

    // Daily plan: new verses to memorize per day (0 = free-form)
    public int HifzDailyNew { get; set; } = 5;

    // Daily cap for old-hifz (قديم) review so the load stays manageable
    public int HifzMaxOldReview { get; set; } = 30;

    // Test gate: false = recite-with-text-hidden test, true = fill-in-the-blank
    public bool HifzTestBlank { get; set; }

    // Daily review reminder via the tray icon
    public bool HifzReminder { get; set; }
    public int HifzReminderHour { get; set; } = 20;
    public string HifzLastReminderDay { get; set; } = "";

    // Last continuous-reading position, for resume
    public int ResumeS { get; set; } = -1;
    public int ResumeA { get; set; } = -1;

    // GitHub repo "owner/name" for update checks; empty falls back to the default repo
    public string UpdateRepo { get; set; } = "MohammedTsmu/QuranWidget";

    public Dictionary<string, WidgetState> Windows { get; set; } = new();
}

public static class SettingsService
{
    public static Settings Current { get; private set; } = new();

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuranWidget");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            Current = new Settings();
        }
        if (string.IsNullOrWhiteSpace(Current.UpdateRepo))
            Current.UpdateRepo = "MohammedTsmu/QuranWidget";
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json.Write));
        }
        catch
        {
            // settings persistence is best-effort
        }
    }
}

public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "QuranWidget";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    k.SetValue(ValueName, $"\"{exe}\" --hidden");
            }
            else
            {
                k.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // registry access is best-effort
        }
    }
}
