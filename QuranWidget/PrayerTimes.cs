using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace QuranWidget;

// Prayer times computed locally from latitude/longitude (Muslim World League angles:
// Fajr 18°, Isha 17°, Asr with shadow factor 1). Location is resolved once via IP
// geolocation and cached, so after the first run this works fully offline.
public static class PrayerTimes
{
    public static readonly string[] Names = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha" };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Providers tried in order. ip-api.com is first because it has no rate limit
    // for non-commercial use; ipapi.co throttles hard (429) and the widget used
    // to hammer it every 30 seconds while failing, which kept it throttled.
    static readonly string[] Providers =
    {
        "http://ip-api.com/json/?fields=status,lat,lon,city",
        "https://ipwho.is/",
        "https://ipapi.co/json/",
    };

    // If every provider fails, back off instead of retrying immediately —
    // repeated 30-second retries kept us in the providers' rate-limit penalty box.
    static DateTime _lastAllFail = DateTime.MinValue;
    static readonly TimeSpan FailBackoff = TimeSpan.FromMinutes(3);

    static string CachePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuranWidget");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "location.json");
    }

    public sealed record Location(double Lat, double Lon, string City);

    public static async Task<Location?> LocateAsync()
    {
        try
        {
            if (File.Exists(CachePath()))
            {
                var cached = JsonSerializer.Deserialize<Location>(File.ReadAllText(CachePath()));
                if (cached != null) return cached;
            }
        }
        catch
        {
            // cache unreadable; re-locate
        }
        if (DateTime.UtcNow - _lastAllFail < FailBackoff) return null;

        foreach (var url in Providers)
        {
            try
            {
                var json = await Http.GetStringAsync(url);
                var loc = ParseLocate(json);
                if (loc != null)
                {
                    _lastAllFail = DateTime.MinValue;
                    await File.WriteAllTextAsync(CachePath(), JsonSerializer.Serialize(loc));
                    return loc;
                }
                Log($"provider returned unusable response: {url}");
            }
            catch (Exception ex)
            {
                Log($"provider failed: {url} — {ex.Message}");
            }
        }
        _lastAllFail = DateTime.UtcNow;
        return null;
    }

    static void Log(string message) => Trace(message);

    public static void Trace(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranWidget");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "locate.log"),
                $"{DateTime.Now:u} {message}\n");
        }
        catch
        {
            // diagnostics are best-effort
        }
    }

    static Location? ParseLocate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // ip-api.com and ipwho.is report "status" ("success" string / true bool);
            // ipapi.co has no status field at all
            if (root.TryGetProperty("status", out var st))
            {
                if (st.ValueKind == JsonValueKind.String && st.GetString() != "success")
                    return null;
                if (st.ValueKind == JsonValueKind.False)
                    return null;
            }
            // ip-api.com uses lat/lon; ipapi.co and ipwho.is use latitude/longitude.
            // Accepting both here fixes the fallback that could never parse its
            // own provider's response.
            double? lat = null, lon = null;
            if (root.TryGetProperty("latitude", out var la) && la.ValueKind == JsonValueKind.Number)
                lat = la.GetDouble();
            else if (root.TryGetProperty("lat", out var la2) && la2.ValueKind == JsonValueKind.Number)
                lat = la2.GetDouble();
            if (root.TryGetProperty("longitude", out var lo) && lo.ValueKind == JsonValueKind.Number)
                lon = lo.GetDouble();
            else if (root.TryGetProperty("lon", out var lo2) && lo2.ValueKind == JsonValueKind.Number)
                lon = lo2.GetDouble();
            if (lat == null || lon == null)
                return null;
            string city = root.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? "" : "";
            return new Location(lat.Value, lon.Value, city);
        }
        catch
        {
            return null;
        }
    }

    static (double decl, double eqTime) SunPosition(DateTime date)
    {
        double n = date.DayOfYear;
        double gamma = 2 * Math.PI / 365 * (n - 1 + (date.Hour - 12) / 24.0);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                  - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        double decl =
            0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        return (decl, eqTime);
    }

    // Returns the six times for the date, as local clock times.
    public static TimeSpan[] TimesFor(DateTime date, double lat, double lon)
    {
        double tz = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
        var (decl, eq) = SunPosition(date);
        double dhuhr = 12 + tz - lon / 15.0 - eq / 60.0;

        double sinLat = Math.Sin(lat * Math.PI / 180);
        double cosLat = Math.Cos(lat * Math.PI / 180);
        double sinD = Math.Sin(decl), cosD = Math.Cos(decl);

        double HourAngle(double angleDeg)
        {
            double a = angleDeg * Math.PI / 180;
            double x = (-Math.Sin(a) - sinLat * sinD) / (cosLat * cosD);
            return double.Clamp(x, -1, 1) * 180 / Math.PI / 15.0;
        }

        // Asr: shadow factor 1
        double asrAngle = Math.Atan(1 / (1 + Math.Tan(Math.Abs(lat - decl) * Math.PI / 180))) * 180 / Math.PI;
        double asrOffset = double.Clamp(
            (Math.Sin(asrAngle * Math.PI / 180) - sinLat * sinD) / (cosLat * cosD), -1, 1) * 180 / Math.PI / 15.0;

        double fajr = dhuhr - HourAngle(18);
        double sunrise = dhuhr - HourAngle(0.833);
        double asr = dhuhr + asrOffset;
        double maghrib = dhuhr + HourAngle(0.833);
        double isha = dhuhr + HourAngle(17);

        TimeSpan Norm(double h)
        {
            h = ((h % 24) + 24) % 24;
            return TimeSpan.FromHours(h);
        }
        return new[] { Norm(fajr), Norm(sunrise), Norm(dhuhr), Norm(asr), Norm(maghrib), Norm(isha) };
    }

    // Next prayer after "now" (wraps to tomorrow's Fajr); null if unavailable.
    public static (string Name, string Time)? Next(double lat, double lon)
    {
        var today = TimesFor(DateTime.Now, lat, lon);
        var now = DateTime.Now.TimeOfDay;
        for (int i = 0; i < today.Length; i++)
        {
            if (i == 1) continue; // Sunrise is not a prayer
            if (today[i] > now)
                return (Names[i], today[i].ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        }
        var tomorrow = TimesFor(DateTime.Now.AddDays(1), lat, lon);
        return (Names[0], tomorrow[0].ToString(@"hh\:mm", CultureInfo.InvariantCulture));
    }
}
