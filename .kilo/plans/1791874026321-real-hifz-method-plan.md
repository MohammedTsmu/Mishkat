# Real Hifz Method — Complete Memorization System Upgrade

**Project:** QuranWidget (WPF, .NET 10, C#) — `QuranWidget/`
**Goal:** Upgrade the existing hifz system from "isolated-verse SRS" to a system that implements the full classic memorization method: connected memorization (الربط), a test gate before memorizing, connected review, old/new review split, adaptive scheduling, daily plan, self-recording, reminders, and backup.

Existing files to modify: `HifzStore.cs`, `HifzWindow.xaml`, `HifzWindow.xaml.cs`, `Settings.cs`, `L10n.cs`, `MainWindow.xaml.cs` (reminder entry point), `Audio.cs` (recording). Build with `build.cmd`.

All UI strings must go through `L10n.T(en, ar)`; keep Arabic RTL flow as-is.

---

## Phase 1 — Core method (the pieces that make hifz stick)

### 1. Schema upgrade (HifzStore.cs)
Extend the SQLite schema with `ALTER TABLE` guarded migrations on `Open()`:
- `memorized` add columns: `ease INTEGER NOT NULL DEFAULT 0`, `group_id INTEGER` (identifies the memorization group / passage), `last_review_day INTEGER`, `is_old INTEGER NOT NULL DEFAULT 0` (old/new split flag).
- New table `groups(id INTEGER PRIMARY KEY AUTOINCREMENT, surah INTEGER, from_ayah INTEGER, to_ayah INTEGER, name TEXT)`; backfill `group_id` on existing rows by grouping consecutive verses.
- New table `plan(day INTEGER, new_count INTEGER)` or a settings key for daily new-portion.
- Bump the persisted meta version so migration runs once. Existing data must survive migration (never drop tables).

### 2. Fix AddRange punishment bug (HifzStore.cs)
`AddRange` currently resets already-memorized verses: `stage=MIN(stage,0)`. Change the `ON CONFLICT` clause to **keep the existing schedule** for verses already memorized (`DO NOTHING`-style: only insert new rows), or at minimum never lower a stage above 0 without a failed review. Log a `history` row when a previously-known verse is re-added so the user sees it.

### 3. Connected memorization (الربط) — HifzWindow memorize tab
After "Mark known" on verse N (and it is not the last verse of the group), insert a **link step** before showing verse N+1:
- Show a "linked recitation" card: text hidden, status text: "Recite from the first verse of the group through this one" (من أول المجموعة إلى هذه الآية).
- Buttons: "Done — next verse" / "Repeat the link" (replays the group audio up to current verse via `Audio.PlayRecitation` with a passage from group start to N).
- Persist link step completion per verse in session state only (no DB needed). After the last verse, one final full-group link recitation (from group start to end) is required before "Mark group as memorized" enables.

### 4. Test gate (الامتحان) — HifzWindow
`FinishSession_Click` no longer saves immediately. Instead show a **test card** over the session card:
- Mode A (default): recite the whole group with text hidden; user self-verifies by revealing, then "I passed" / "Repeat the test".
- Mode B (optional toggle in settings, default off): fill-in-the-blank — show the group text with 2 random verses blanked; user says them, taps blanks to verify.
- On pass → `HifzStore.AddRange(...)` with current behavior. On "Repeat the test" → loop back (and suggest rehearsing the failed verse via the existing loop player).
- Only after a pass does the status message confirm the ladder (1, 3, 7, 14, 30, 60, 120 days).

### 5. Connected review (وِرد متصل) — HifzWindow review tab
Add a **group review** flow alongside the existing per-verse review (keep both):
- Due list is grouped: consecutive due verses of the same `group_id` collapse into one entry labeled with the range (e.g. "البقرة ٢٥٥–٢٦٠").
- Selecting a group entry: text hidden, user recites the whole connected passage, then reveals to verify; then per-verse self-check row appears ("Confirm each verse" — user taps any verse they stumbled on to mark it forgot individually, via existing `Forgot`), and "Group passed" calls `Remembered` for each verse in the group (batch method `HifzStore.RememberedRange` in one transaction).
- Play button plays the whole group passage.

### 6. Old/new review split (حديث / قديم) — HifzStore + review tab
- New verses (stage ≤ 2) are **حديث** (recent): due daily until stage 3.
- Verses at stage ≥ 3 are **قديم** (old): reviewed on their ladder, but capped to a daily load (settings: max old reviews per day, default 30; nearest-due-first, remainder stays due).
- Review tab shows two sections with counts: "Today's new review (حديث)" and "Today's old review (قديم)". The daily old-review selection is derived each day (deterministic ordering by `due_day, surah, ayah`) — do not persist a separate queue.

### 7. Adaptive scheduling — HifzStore
Use `Lapses` and `ease` instead of the pure fixed ladder:
- Keep `Intervals` as base. On success: next interval = `Intervals[stage] + ease * (Intervals[stage] / 4)` capped at 180 days, where `ease` grows +1 per consecutive success above stage 2 (max 3) and resets to 0 on forget.
- On forget: stage resets to 0 (as now) and interval-1 repeat is added the same day ("relearn": due again in 10 minutes conceptually → simply due immediately in today's list again after finishing the rest; simplest correct behavior: `due_day = Today`, so it reappears in the same session).
- Keep the tamper-resistant day clock unchanged.

---

## Phase 2 — Planning, self-check, reminders, safety

### 8. Daily plan (خطة الحفظ)
- Settings: `HifzDailyNew` (verses of new hifz per day, default 5; 0 = free-form). The memorize tab shows "Today's portion: X of Y verses" counted from `history`/`added_day == Today`.
- Progress tab: add a 30-day strip of `history` aggregates (recited / forgotten counts per day) and a streak counter (consecutive days with any review logged). Query from the existing `history` table.
- Optional target: settings `HifzTargetSurah` — progress tab shows percent complete of that surah.

### 9. Record & compare (تسجيل الصوت) — Audio.cs + review/memorize cards
- Add a recording utility using `System.Media`/WASAPI-free approach: `NAudio` is not currently referenced, so implement with `System.Speech`? No — use the **Win32 `winmm` MCI** (`mciSendString` record wave) via the existing `Native.cs` P/Invoke file to avoid new packages; or add `NAudio` NuGet if MCI proves unreliable on Win11. Prefer MCI first (no new dependency).
- "Record" button on the hide-and-recite card (memorize link step, test, and review): records WAV to `%LOCALAPPDATA%\QuranWidget\recordings\`, then "Play mine / Play reciter" A-B compare buttons; auto-delete after 7 days on startup.
- Recording button hidden if microphone access fails; log to the existing log.

### 10. Daily reminder — MainWindow / tray
- Settings: `HifzReminder` (bool, default off) + `HifzReminderHour` (default 20:00).
- When enabled and `HifzStore.DueCount() > 0`, show the existing tray/notification balloon (reuse the tray icon code) at the set hour: "N آية مستحقة للمراجعة اليوم" — click opens HifzWindow on the review tab. One shot per day, checked in the existing timer/dispatcher loop; must call `HifzStore.RefreshClock()` before counting.

### 11. Backup / export
- Progress tab: "Backup" button → copies `hifz.db` (checkpoint/WAL first: `PRAGMA wal_checkpoint(TRUNCATE)`) to a user-chosen path via `SaveFileDialog` (default filename `hifz-backup-YYYY-MM-DD.db`), and "Restore" → closes the store, replaces the file from the chosen backup, reopens, refreshes UI. Warn with a confirmation dialog on restore that current progress will be replaced.
- Also offer "Export CSV" of the memorized list (surah, ayah, stage, lapses) for peace of mind.

---

## Out of scope
- Tajweed color-coding, mushaf page rendering, online sheikh verification, mobile sync.
- Changing the interval ladder philosophy beyond item 7 (no full FSRS implementation).

## Validation
- `build.cmd` must succeed; run the app and walk through: memorize a 3-verse group with link steps → fail the test once → pass → confirm rows in `hifz.db` (`sqlite3` or a debug dump), stage 0, due tomorrow.
- Review: verify group collapse in due list, group pass advances all verses, forgetting one verse in a group doesn't advance it.
- Migration: run with a pre-upgrade `hifz.db` (create one with the current build first) and confirm no data loss.
- Clock: change system time back 2 days while app closed; confirm due dates don't shift.
- Reminder: set hour 1 minute ahead, confirm balloon appears once and opens review tab.
- Backup: export, wipe data, restore, confirm identical counts.
- Both languages (AR/EN) and RTL layout checked for every new UI element.
