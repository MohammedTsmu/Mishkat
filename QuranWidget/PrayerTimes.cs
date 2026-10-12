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
        try
        {
            var json = await Http.GetStringAsync("https://ipapi.co/json/");
            var loc = ParseLocate(json);
            if (loc != null)
            {
                await File.WriteAllTextAsync(CachePath(), JsonSerializer.Serialize(loc));
                return loc;
            }
        }
        catch
        {
            // try the fallback below
        }
        try
        {
            var json = await Http.GetStringAsync("http://ip-api.com/json/?fields=status,lat,lon,city");
            var loc = ParseLocate(json);
            if (loc != null)
            {
                await File.WriteAllTextAsync(CachePath(), JsonSerializer.Serialize(loc));
                return loc;
            }
        }
        catch
        {
            // give up; caller decides what to show
        }
        return null;
    }

    static Location? ParseLocate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() != "success")
                return null;
            if (!root.TryGetProperty("latitude", out var la) || !root.TryGetProperty("longitude", out var lo))
                return null;
            string city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
            return new Location(la.GetDouble(), lo.GetDouble(), city);
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
