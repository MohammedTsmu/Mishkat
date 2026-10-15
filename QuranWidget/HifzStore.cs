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
    public int Ease { get; set; }        // consecutive-success bonus (0..3) that stretches intervals
    public int GroupId { get; set; }     // the memorization group / passage this verse belongs to
    public int LastReviewDay { get; set; }
    public bool IsOld => Stage >= 3;     // "old" hifz (قديم) after the first two review levels
    public string Ref => $"{Surah}:{Ayah}";
}

// A memorization group: the passage the user learned and tested in one sitting.
public sealed class HifzGroup
{
    public int Id { get; set; }
    public int Surah { get; set; }
    public int FromAyah { get; set; }
    public int ToAyah { get; set; }
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

    const int MaxEase = 3;
    const int MaxIntervalDays = 180;

    static SqliteConnection? _db;
    static readonly object Gate = new();

    static string DbPath => Path.Combine(AppPaths.Local, "hifz.db");

    // Full path of the database file, for backup/restore.
    public static string DatabasePath => DbPath;

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
            Migrate();
            Today = AdvanceClock();
        }
    }

    // Adds the columns and tables introduced after the first release, keeping
    // every existing row intact.
    static void Migrate()
    {
        var existing = new HashSet<string>();
        using (var cmd = _db!.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(memorized)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existing.Add(r.GetString(1));
        }
        void AddCol(string name, string decl)
        {
            if (existing.Contains(name)) return;
            using var c = _db!.CreateCommand();
            c.CommandText = $"ALTER TABLE memorized ADD COLUMN {name} {decl}";
            c.ExecuteNonQuery();
        }
        AddCol("ease", "INTEGER NOT NULL DEFAULT 0");
        AddCol("group_id", "INTEGER NOT NULL DEFAULT 0");
        AddCol("last_review_day", "INTEGER NOT NULL DEFAULT 0");
        AddCol("is_old", "INTEGER NOT NULL DEFAULT 0");

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS groups(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    surah INTEGER NOT NULL,
                    from_ayah INTEGER NOT NULL,
                    to_ayah INTEGER NOT NULL);
                UPDATE memorized SET is_old = CASE WHEN stage >= 3 THEN 1 ELSE 0 END;
                """;
            cmd.ExecuteNonQuery();
        }

        // Backfill group_id for verses memorized before groups existed: group
        // runs of consecutive ayahs within each surah.
        int ungrouped;
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM memorized WHERE group_id = 0";
            ungrouped = (int)(long)cmd.ExecuteScalar()!;
        }
        if (ungrouped > 0)
        {
            using var tx = _db.BeginTransaction();
            var surahs = new List<int>();
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT DISTINCT surah FROM memorized WHERE group_id = 0";
                using var r = cmd.ExecuteReader();
                while (r.Read()) surahs.Add(r.GetInt32(0));
            }
            foreach (int s in surahs)
            {
                var ayahs = new List<int>();
                using (var cmd = _db.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "SELECT ayah FROM memorized WHERE surah=$s AND group_id=0 ORDER BY ayah";
                    cmd.Parameters.AddWithValue("$s", s);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) ayahs.Add(r.GetInt32(0));
                }
                int start = ayahs[0], prev = ayahs[0];
                void Flush(int from, int to, IEnumerable<int> members)
                {
                    using var ins = _db!.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO groups(surah, from_ayah, to_ayah) VALUES($s,$f,$t)";
                    ins.Parameters.AddWithValue("$s", s);
                    ins.Parameters.AddWithValue("$f", from);
                    ins.Parameters.AddWithValue("$t", to);
                    ins.ExecuteNonQuery();
                    long gid = QueryLong(tx, "SELECT last_insert_rowid()");
                    foreach (int a in members)
                    {
                        using var upd = _db.CreateCommand();
                        upd.Transaction = tx;
                        upd.CommandText = "UPDATE memorized SET group_id=$g WHERE surah=$s AND ayah=$a";
                        upd.Parameters.AddWithValue("$g", gid);
                        upd.Parameters.AddWithValue("$s", s);
                        upd.Parameters.AddWithValue("$a", a);
                        upd.ExecuteNonQuery();
                    }
                }
                foreach (int a in ayahs.Skip(1))
                {
                    if (a == prev + 1) { prev = a; continue; }
                    Flush(start, prev, Enumerable.Range(start, prev - start + 1));
                    start = prev = a;
                }
                Flush(start, prev, Enumerable.Range(start, prev - start + 1));
            }
            tx.Commit();
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

    // Flushes the write-ahead log so the database file is safe to copy.
    public static void Checkpoint()
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            cmd.ExecuteNonQuery();
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
            cmd.CommandText = ItemSelect + " WHERE surah=$s AND ayah=$a";
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return ReadItem(r);
        }
    }

    // Column order: surah, ayah, stage, due_day, added_day, lapses, ease, group_id, last_review_day
    const string ItemSelect = """
        SELECT surah, ayah, stage, due_day, added_day, lapses, ease, group_id, last_review_day
        FROM memorized
        """;

    static HifzItem ReadItem(SqliteDataReader r) => new()
    {
        Surah = r.GetInt32(0), Ayah = r.GetInt32(1),
        Stage = r.GetInt32(2), DueDay = r.GetInt32(3),
        AddedDay = r.GetInt32(4), Lapses = r.GetInt32(5),
        Ease = r.GetInt32(6), GroupId = r.GetInt32(7),
        LastReviewDay = r.GetInt32(8),
    };

    // Marks verses as newly memorized (or re-added): stage 0, due tomorrow.
    // Verses that already sit on the schedule keep their progress — re-marking a
    // group must never reset a verse the user has already been reviewing well.
    // Returns how many verses were actually new.
    public static int AddRange(IEnumerable<(int Surah, int Ayah)> verses)
    {
        lock (Gate)
        {
            if (_db == null) return 0;
            using var tx = _db.BeginTransaction();
            int added = 0;
            var list = verses.ToList();
            int gid = CreateGroup(tx, list);
            foreach (var (s, a) in list)
            {
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO memorized(surah, ayah, stage, due_day, added_day, lapses, ease, group_id)
                    VALUES($s,$a,0,$d+1,$d,0,0,$g)
                    ON CONFLICT(surah, ayah) DO NOTHING;
                    """;
                cmd.Parameters.AddWithValue("$s", s);
                cmd.Parameters.AddWithValue("$a", a);
                cmd.Parameters.AddWithValue("$d", Today);
                cmd.Parameters.AddWithValue("$g", gid);
                added += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return added;
        }
    }

    static int CreateGroup(SqliteTransaction tx, List<(int Surah, int Ayah)> verses)
    {
        if (verses.Count == 0) return 0;
        int surah = verses[0].Surah;
        int from = verses.Min(v => v.Ayah);
        int to = verses.Max(v => v.Ayah);
        // Reuse an identical group if one already exists.
        using (var find = _db!.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM groups WHERE surah=$s AND from_ayah=$f AND to_ayah=$t";
            find.Parameters.AddWithValue("$s", surah);
            find.Parameters.AddWithValue("$f", from);
            find.Parameters.AddWithValue("$t", to);
            var v = find.ExecuteScalar();
            if (v is long id) return (int)id;
        }
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO groups(surah, from_ayah, to_ayah) VALUES($s,$f,$t)";
        cmd.Parameters.AddWithValue("$s", surah);
        cmd.Parameters.AddWithValue("$f", from);
        cmd.Parameters.AddWithValue("$t", to);
        cmd.ExecuteNonQuery();
        return (int)QueryLong(tx, "SELECT last_insert_rowid()");
    }

    static long QueryLong(SqliteTransaction? tx, string text)
    {
        using var cmd = _db!.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = text;
        return (long)cmd.ExecuteScalar()!;
    }

    public static HifzGroup? GroupOf(int groupId)
    {
        lock (Gate)
        {
            if (_db == null || groupId <= 0) return null;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT surah, from_ayah, to_ayah FROM groups WHERE id=$g";
            cmd.Parameters.AddWithValue("$g", groupId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new HifzGroup { Id = groupId, Surah = r.GetInt32(0), FromAyah = r.GetInt32(1), ToAyah = r.GetInt32(2) };
        }
    }

    // The next interval for a verse at `stage` with the given ease: the classic
    // ladder stretched by the ease bonus, capped. Hard verses stay short.
    static int NextInterval(int stage, int ease)
    {
        int stageIdx = Math.Clamp(stage, 0, Intervals.Length - 1);
        int baseDays = Intervals[stageIdx];
        int days = baseDays + ease * (baseDays / 4);
        return Math.Min(days, MaxIntervalDays);
    }

    // A successful review: advance one step on the interval ladder, stretched a
    // little each time the verse keeps being remembered easily.
    public static void Remembered(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var tx = _db.BeginTransaction();
            RememberedTx(tx, surah, ayah);
            tx.Commit();
        }
    }

    static void RememberedTx(SqliteTransaction tx, int surah, int ayah)
    {
        int stage = 0, ease = 0;
        using (var cmd = _db!.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT stage, ease FROM memorized WHERE surah=$s AND ayah=$a";
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { stage = r.GetInt32(0); ease = r.GetInt32(1); }
        }
        int newStage = Math.Min(stage + 1, Intervals.Length - 1);
        if (newStage >= 3) ease = Math.Min(ease + 1, MaxEase); else ease = Math.Max(ease - 1, 0);
        using var upd = _db.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = """
            UPDATE memorized SET stage=$st, due_day=$d, ease=$e, last_review_day=$t, is_old=$o
            WHERE surah=$s AND ayah=$a
            """;
        upd.Parameters.AddWithValue("$st", newStage);
        upd.Parameters.AddWithValue("$d", Today + NextInterval(newStage, ease));
        upd.Parameters.AddWithValue("$e", ease);
        upd.Parameters.AddWithValue("$t", Today);
        upd.Parameters.AddWithValue("$o", newStage >= 3 ? 1 : 0);
        upd.Parameters.AddWithValue("$s", surah);
        upd.Parameters.AddWithValue("$a", ayah);
        upd.ExecuteNonQuery();
        Log(tx, surah, ayah, true);
    }

    // Batch version for a connected group review that passed as a whole.
    public static void RememberedRange(IEnumerable<(int Surah, int Ayah)> verses)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var tx = _db.BeginTransaction();
            foreach (var (s, a) in verses) RememberedTx(tx, s, a);
            tx.Commit();
        }
    }

    // A failed review: back to the start of the ladder and due again the same
    // day (relearn), so a forgotten verse comes back within the same session.
    public static void Forgot(int surah, int ayah)
    {
        lock (Gate)
        {
            if (_db == null) return;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                UPDATE memorized SET stage=0, due_day=$d, ease=0, lapses=lapses+1,
                    last_review_day=$t, is_old=0
                WHERE surah=$s AND ayah=$a;
                """;
            cmd.Parameters.AddWithValue("$d", Today);
            cmd.Parameters.AddWithValue("$t", Today);
            cmd.Parameters.AddWithValue("$s", surah);
            cmd.Parameters.AddWithValue("$a", ayah);
            cmd.ExecuteNonQuery();
            Log(null, surah, ayah, false);
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

    static void Log(SqliteTransaction? tx, int surah, int ayah, bool ok)
    {
        using var cmd = _db!.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO history(surah, ayah, day, ok) VALUES($s,$a,$d,$o)";
        cmd.Parameters.AddWithValue("$s", surah);
        cmd.Parameters.AddWithValue("$a", ayah);
        cmd.Parameters.AddWithValue("$d", Today);
        cmd.Parameters.AddWithValue("$o", ok ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // All scheduled verses due today or earlier, newest-ladder last, ordered so
    // the oldest overdue comes first (deterministic daily old-review selection).
    public static List<HifzItem> DueItems()
    {
        lock (Gate)
        {
            var list = new List<HifzItem>();
            if (_db == null) return list;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = ItemSelect + " WHERE due_day<=$d ORDER BY due_day, surah, ayah";
            cmd.Parameters.AddWithValue("$d", Today);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadItem(r));
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
            cmd.CommandText = ItemSelect + " ORDER BY surah, ayah";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadItem(r));
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

    // ---------- daily plan & streak ----------

    // Verses that entered the schedule today (the daily new portion).
    public static int AddedToday()
    {
        lock (Gate)
        {
            if (_db == null) return 0;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM memorized WHERE added_day=$d";
            cmd.Parameters.AddWithValue("$d", Today);
            return (int)(long)cmd.ExecuteScalar()!;
        }
    }

    // Consecutive days ending today (or yesterday, if today has no reviews yet)
    // with at least one logged review.
    public static int Streak()
    {
        lock (Gate)
        {
            if (_db == null) return 0;
            int streak = 0;
            int day = Today;
            // today may not be reviewed yet — the streak survives until midnight
            if (!DayHasReviews(day)) day--;
            while (DayHasReviews(day)) { streak++; day--; }
            return streak;
        }
    }

    static bool DayHasReviews(int day)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM history WHERE day=$d LIMIT 1";
        cmd.Parameters.AddWithValue("$d", day);
        return cmd.ExecuteScalar() != null;
    }

    // (dayOffsetFromToday, okCount, failCount) for the last `days` days, oldest first.
    public static List<(int Day, int Ok, int Fail)> RecentHistory(int days)
    {
        lock (Gate)
        {
            var list = new List<(int, int, int)>();
            if (_db == null) return list;
            var map = new Dictionary<int, (int, int)>();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT day, SUM(ok), SUM(1-ok) FROM history
                    WHERE day > $from AND day <= $to GROUP BY day
                    """;
                cmd.Parameters.AddWithValue("$from", Today - days);
                cmd.Parameters.AddWithValue("$to", Today);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    map[r.GetInt32(0)] = ((int)(long)r.GetValue(1), (int)(long)r.GetValue(2));
            }
            for (int i = days - 1; i >= 0; i--)
            {
                int day = Today - i;
                var (ok, fail) = map.TryGetValue(day, out var v) ? v : (0, 0);
                list.Add((day, ok, fail));
            }
            return list;
        }
    }
}
