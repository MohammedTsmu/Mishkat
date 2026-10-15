using System.IO;

namespace QuranWidget;

// Central app-data paths. The app was renamed from QuranWidget to Mishkat, so
// its data folders moved too; the first launch after the rename migrates the
// old folders in place so settings, hifz history, downloaded translations,
// audio cache and recordings survive the update.
public static class AppPaths
{
    public static readonly string Roaming = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mishkat");

    public static readonly string Local = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mishkat");

    static AppPaths()
    {
        // Runs once, before any path is handed out: move the old folders over.
        TryMigrate(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuranWidget"), Roaming);
        TryMigrate(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranWidget"), Local);
    }

    static void TryMigrate(string oldDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(oldDir) || Directory.Exists(newDir)) return;
            Directory.Move(oldDir, newDir);
        }
        catch
        {
            // best effort: if the old folder is locked (e.g. the previous
            // version is still running) the app simply starts fresh in the
            // new location rather than failing to launch
        }
    }
}
