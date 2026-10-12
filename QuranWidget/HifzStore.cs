using System.IO;
using Microsoft.Data.Sqlite;

namespace QuranWidget;

// One memorized verse in the spaced-repetition schedule.
public sealed class HifzItem
{
    public int Surah { get; set; }
    public int Ayah { get; set; }
    public int Stage { get; set; }       // index into HifzStore.Intervals
    public int DueDay { get; set; }      // trusted day number when it is due next
    public int AddedDay { get; set; }
    public int Lapses { get; set; }      // how many times it was forgotten
    public string Ref => $"{Surah}:{Ayah}";
}

// Persisted hifz data in SQLite, plus a tamper-resistant day clock.
//
// Why SQLite (per user request): atomic, queryable, and never loses data the way
// a hand-written JSON file can when the app crashes mid-write.
//
// Why the clock resists system-time changes: the schedule is anchored to a
// "trusted day" counter that only ever moves forward and is persisted. Each time
// the app opens, the counter advances to at most the real UTC day count — and
// never backward. So if the user (or an app) moves the system clock back, due
// dates do not move and nothing is lost. A big forward jump is accepted (a real
// absence of weeks is legitimate), but reviews can never be skipped silently:
// the due items simply wait until the user actually recites them.
public static class HifzStore
{
    // Classic hifz review ladder (days after the previous successful review).
    public static readonly int[] Intervals = { 1, 3, 7, 14, 30, 60, 120 };

    static SqliteConnection? _db;
    static readonly object Gate = new();

    static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranWidget", "hifz.db");

    // The trusted day counter (days since 0001-01-01 UTC); never decreases.
    public static int Today { get; private set; }

    public static void Open()
    {
        lock (Gate)
        {
            if (_db != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            _db = new SqliteConnection($"Data Source={DbPath}");
            _db.Open();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE IF NOT EXISTS memorized(
                    surah INTEGER NOT NULL,
                    ayah INTEGER NOT NULL,
                    stage INTEGER NOT NULL,
                    due_day INTEGER NOT NULL,
                    added_day INTEGER NOT NULL,
                    lapses INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(surah, ayah));
                CREATE INDEX IF NOT EXISTS ix_due ON memorized(due_day);
                CREATE TABLE IF NOT EXISTS history(
                    surah INTEGER NOT NULL,
                    ayah INTEGER NOT NULL,
                    day INTEGER NOT NULL,
                    ok INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_hist_day ON history(day);
                """;
            cmd.ExecuteNonQuery();
            Today = AdvanceClock();
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            _db?.Dispose();
            _db = null;
        }
    }

    // Advances the persisted day counter toward the real UTC day, never backward.
    static int AdvanceClock()
    {
        int realDay = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay);
        int stored = GetMetaInt("trusted_day", realDay);
        int day = Math.Max(stored, realDay);
        SetMetaInt("trusted_day", day);
        return day;
    }

    // Re-check the clock while the app runs (midnight rollover); also catches a
    // system clock that was corrected forward/backward during a long session.
    public static void RefreshClock()
    {
        lock (Gate)
        {
            if (_db == null) return;
            Today = AdvanceClock();
        }
    }

    static int GetMetaInt(string key, int fallback)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        var v = cmd.ExecuteScalar();
        return v is string s && int.TryParse(s, out int n) ? n : fallback;
    }

    static void SetMetaInt(string key, int value)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value.ToString());
        cmd.ExecuteNonQuery();
    }

    // ---------- schedule operations ----------

    public static HifzItem? Get(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return null;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT stage, due_day, added_day, lapses FROM memorized WHERE surah=$s AND ayah=$a";
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new HifzItem
            {
                Surah = surah, Ayah = ayah,
                Stage = r.GetInt32(0), DueDay = r.GetInt32(1),
                AddedDay = r.GetInt32(2), Lapses = r.GetInt32(3),
            };
        }
    }

    // Marks verses as newly memorized (or re-added): stage 0, due tomorrow.
    public static void AddRange(IEnumerable<(int Surah, int Ayah)> verses)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var tx = _db.BeginTransaction();
            foreach (var (s, a) in verses)
            {
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO memorized(surah, ayah, stage, due_day, added_day, lapses)
                    VALUES($s,$a,0,$d,$d,0)
                    ON CONFLICT(surah, ayah) DO UPDATE SET stage=MIN(stage,0), due_day=$d+1;
                    """;
                cmd.Parameters.AddWithValue("$s", s);
                cmd.Parameters.AddWithValue("$a", a);
                cmd.Parameters.AddWithValue("$d", Today);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    // A successful review: advance one step on the interval ladder.
    public static void Remembered(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return;
            int stage = 0, due = Today;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT stage FROM memorized WHERE surah=$s AND ayah=$a";
                cmd.Parameters.AddWithValue("$s", surah);
                cmd.Parameters.AddWithValue("$a", ayah);
                var v = cmd.ExecuteScalar();
                if (v is long l) stage = (int)l;
            }
            stage = Math.Min(stage + 1, Intervals.Length - 1);
            due = Today + Intervals[stage];
            using var upd = _db.CreateCommand();
            upd.CommandText = "UPDATE memorized SET stage=$st, due_day=$d WHERE surah=$s AND ayah=$a";
            upd.Parameters.AddWithValue("$st", stage);
            upd.Parameters.AddWithValue("$d", due);
            upd.Parameters.AddWithValue("$s", surah);
            upd.Parameters.AddWithValue("$a", ayah);
            upd.ExecuteNonQuery();
            Log(surah, ayah, true);
        }
    }

    // A failed review: back to the start of the ladder, due tomorrow.
    public static void Forgot(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                UPDATE memorized SET stage=0, due_day=$d, lapses=lapses+1
                WHERE surah=$s AND ayah=$a;
                """;
            cmd.Parameters.AddWithValue("$d", Today + Intervals[0]);
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            cmd.ExecuteNonQuery();
            Log(surah, ayah, false);
        }
    }

    // Removes a verse from the schedule entirely.
    public static void Remove(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM memorized WHERE surah=$s AND ayah=$a";
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            cmd.ExecuteNonQuery();
        }
    }

    static void Log(int surah, int ayah, bool ok)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "INSERT INTO history(surah, ayah, day, ok) VALUES($s,$a,$d,$o)";
        cmd.Parameters.AddWithValue("$s", surah);
        cmd.Parameters.AddWithValue("$a", ayah);
        cmd.Parameters.AddWithValue("$d", Today);
        cmd.Parameters.AddWithValue("$o", ok ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // All scheduled verses due today or earlier, ordered by surah/ayah.
    public static List<HifzItem> DueItems()
    {
        lock (Gate)
        {
            var list = new List<HifzItem>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT surah, ayah, stage, due_day, added_day, lapses FROM memorized WHERE due_day<=$d ORDER BY surah, ayah";
            cmd.Parameters.AddWithValue("$d", Today);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new HifzItem
                {
                    Surah = r.GetInt32(0), Ayah = r.GetInt32(1),
                    Stage = r.GetInt32(2), DueDay = r.GetInt32(3),
                    AddedDay = r.GetInt32(4), Lapses = r.GetInt32(5),
                });
            return list;
        }
    }

    // Everything memorized, for stats and progress display.
    public static List<HifzItem> AllItems()
    {
        lock (Gate)
        {
            var list = new List<HifzItem>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT surah, ayah, stage, due_day, added_day, lapses FROM memorized ORDER BY surah, ayah";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new HifzItem
                {
                    Surah = r.GetInt32(0), Ayah = r.GetInt32(1),
                    Stage = r.GetInt32(2), DueDay = r.GetInt32(3),
                    AddedDay = r.GetInt32(4), Lapses = r.GetInt32(5),
                });
            return list;
        }
    }

    // Distinct surah numbers that have memorized verses (for progress display).
    public static List<(int Surah, int Count)> ProgressBySurah()
    {
        lock (Gate)
        {
            var list = new List<(int, int)>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT surah, COUNT(*) FROM memorized GROUP BY surah ORDER BY surah";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt32(0), (int)r.GetInt64(1)));
            return list;
        }
    }

    public static int DueCount()
    {
        lock (Gate)
        {
            if (_db == null) return 0;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM memorized WHERE due_day<=$d";
            cmd.Parameters.AddWithValue("$d", Today);
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    public static int TotalCount()
    {
        lock (Gate)
        {
            if (_db == null) return 0;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM memorized";
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }
}
