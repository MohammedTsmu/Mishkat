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

    // Follow the Windows light/dark personalization: parchment while Windows
    // uses light apps, the chosen dark theme while it uses dark.
    public bool SystemSync { get; set; } = true;
    public string LastDarkTheme { get; set; } = "emerald";
    public double ArabicFontSize { get; set; } = 24;

    // Translations ("en.sahih" embedded; others downloaded on demand)
    public string TranslationId { get; set; } = "en.sahih";

    // Sleep timer for recitation (minutes, 0 = off)
    public int SleepMinutes { get; set; }

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
    public string UpdateRepo { get; set; } = "MohammedTsmu/Mishkat";

    public Dictionary<string, WidgetState> Windows { get; set; } = new();
}

public static class SettingsService
{
    public static Settings Current { get; private set; } = new();

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuranWidget");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly string BackupPath = Path.Combine(Dir, "settings.bak.json");

    public static void Load()
    {
        // Load the main file first; if it is missing or unreadable (a hard kill
        // can truncate the JSON mid-write, and an antivirus can hold a lock),
        // fall back to the last known-good backup instead of resetting to
        // defaults and wiping the user's settings on the next save.
        Settings? loaded = TryRead(FilePath);
        if (loaded == null) loaded = TryRead(BackupPath);
        if (loaded != null) Current = loaded;

        if (string.IsNullOrWhiteSpace(Current.UpdateRepo))
            Current.UpdateRepo = "MohammedTsmu/Mishkat";

        // prayer times were removed; drop the stale geolocation cache
        try { File.Delete(Path.Combine(Dir, "location.json")); } catch { /* best effort */ }
    }

    static Settings? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json.Read);
            return s;
        }
        catch
        {
            return null;
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(Current, Json.Write);

            // atomic replace: write a temp file, then swap it in. A plain
            // WriteAllText over settings.json can be truncated if the process
            // dies mid-write (Windows shutdown kills apps quickly), and a
            // half-written file used to reset every setting on the next launch.
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, true);

            // keep one known-good copy for recovery
            try { File.WriteAllText(BackupPath, json); } catch { /* best effort */ }
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
    const string ValueName = "Mishkat";

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
