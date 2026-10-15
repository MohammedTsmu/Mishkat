using Microsoft.Win32;
using System.Windows;

namespace QuranWidget;

// Watches the Windows light/dark personalization setting and, when
// "Match Windows theme" is enabled, keeps the app in sync: parchment while
// Windows uses light apps, the user's dark theme while it uses dark.
public static class SystemTheme
{
    static string _applied = "";

    // Raised on the UI thread after the theme changed because Windows changed.
    public static event Action? Applied;

    public static bool SystemLight =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is 1;

    public static void Start()
    {
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category != UserPreferenceCategory.General) return;
            // SystemEvents raises on its own thread; hop to the UI thread
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(Reapply);
        };
        Reapply();
    }

    // Applies the theme the system currently asks for. Safe to call any time.
    public static void Reapply()
    {
        var s = SettingsService.Current;
        if (!s.SystemSync)
        {
            _applied = s.Theme;
            return;
        }

        string desired = SystemLight ? "parchment" : EffectiveDark(s);
        if (desired == _applied) return;

        // remember the dark theme so returning to Windows dark restores it
        if (SystemLight && s.Theme is "emerald" or "midnight" or "sepia")
            s.LastDarkTheme = s.Theme;

        s.Theme = desired;
        _applied = desired;
        SettingsService.Save();
        ThemeService.Apply(desired);
        foreach (var w in App.Instance.Widgets.ToList())
        {
            try { if (!w.IsPinned) WindowEffects.EnableAcrylic(w); }
            catch { /* best effort */ }
        }
        App.Instance.RefreshWidgets();
        Applied?.Invoke();
    }

    static string EffectiveDark(Settings s) =>
        s.LastDarkTheme is "midnight" or "sepia" or "emerald" ? s.LastDarkTheme : "emerald";
}
