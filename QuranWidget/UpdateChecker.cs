using System.Net.Http;
using System.Text.Json;

namespace QuranWidget;

// Checks a GitHub repo's latest release tag against the running version.
// Enable by setting UpdateRepo ("owner/repo") in settings.json.
public static class UpdateChecker
{
    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "QuranWidget" } },
    };

    // Returns the new tag if an update exists, null if up to date,
    // and throws nothing — failures surface as (false, reason).
    public static async Task<(bool UpdateAvailable, string Detail)> CheckAsync(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            return (false, "Updates are not configured.");
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (Newer(tag, CurrentVersion()))
                return (true, $"Update available: {tag}");
            return (false, "You are up to date.");
        }
        catch (Exception)
        {
            return (false, "Could not check for updates.");
        }
    }

    static string CurrentVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // Semantic-ish comparison: 1.2.10 > 1.2.9
    static bool Newer(string tag, string current)
    {
        int[] Parse(string s)
        {
            s = s.TrimStart('v', 'V');
            var parts = s.Split('.').Take(3).Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0).ToArray();
            while (parts.Length < 3) parts = parts.Append(0).ToArray();
            return parts;
        }
        var a = Parse(tag);
        var b = Parse(current);
        for (int i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return false;
    }
}
