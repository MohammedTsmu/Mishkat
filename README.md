# Quran Widget for Windows

A beautiful desktop widget that shows a **verse of the Quran every day** in Arabic
(Uthmani script, set in the Amiri Quran typeface) with its **English meaning**, a
**recitation player**, a **voice reader** for the translation, and optional
**prayer times** — all in a compact, themeable card that can live right on your
desktop.

![widget](https://img.shields.io/badge/platform-Windows-blue)

## Run it

Double-click **`dist\QuranWidget.exe`** (or run `build.cmd` to rebuild it from source).

The widget appears on your desktop. It lives in the system tray — closing it just
hides it; use the tray icon to exit.

## Features

**Reading**
- **Verse of the Day** — a hand-curated beloved verse chosen deterministically for
  each day, so every device shows the same verse. It refreshes itself at midnight.
- **Arabic in Amiri Quran** — proper Uthmani text; ornamental ayah-end markers
  (`﴿٤٦﴾`) always stay intact when lines wrap.
- **Translation picker** — Saheeh International (built in), plus Pickthall,
  Yusuf Ali, Muhammad Asad and Hilali & Khan (downloaded on demand, cached offline).
- **Search** — jump straight to `2:255`, or full-text search the translation
  **and the Arabic text** (Arabic matches even without diacritics).
- **Favorites** — tap the star to keep verses; browse them from the popup or the
  right-click menu.
- **Save as image** — export the verse card as a high-resolution PNG for sharing.

**Listening**
- **Recitation** — verse-by-verse audio from 7 reciters (Alafasy, Abdul Basit,
  Husary, Minshawy, Sudais, Shatri, Hudhaify), cached locally after first listen.
- **Continuous reading** — keeps going through the following verses, rolls into
  the next surah, and stops where you tell it (end of surah / 10–50 verses / until
  you stop). The widget follows along and **highlights the verse being recited**.
- **Resume** — the reading position is remembered between sessions.
- **Sleep timer** — stop the recitation after 10 / 20 / 30 / 60 minutes, with a
  live countdown so you always know when it will stop.
- **Read the meaning** — reads the translation aloud with your Windows voices.

**Look & feel**
- **4 themes** — Emerald Night, Parchment Light, Midnight Blue, Desert Sepia.
- **Font size** slider for the Arabic text (translation scales with it).
- **Opacity** slider, always-on-top, and a refined **pin to desktop** mode that
  holds the widget at the bottom of your desktop, behind every window.

**Utility**
- **Prayer times** — next prayer in the header, computed locally from your
  location (located once via IP, then fully offline).
- **Offline aware** — a colored wifi-off indicator appears the moment internet is
  gone (recitation stops cleanly, the play button disables) and disappears when
  it's back. Reading, searching and copying always work offline.
- **Multiple widgets** — right-click → *New Verse Widget* (up to 5).
- **Two UI languages** — English or Arabic (with right-to-left layout), chosen in
  Settings; non-Arabic speakers keep English.
- **Optional translation** — hide the translation panel entirely if you only want
  the Arabic text.
- **Hijri date** using the Umm al-Qura calendar.
- **Multi-monitor aware** — widgets remember which screen they were on.

## Controls

| Action | How |
|---|---|
| Move the widget | Drag it anywhere |
| Settings (search, reciter, translation, theme, sizes, timers…) | Gear button |
| Favorites / Save as image / Pin / more | Right-click the widget |
| Hide to tray | ✕ button (or tray icon) |
| Verse of the Day / New widget / Updates / Log / Exit | Tray icon menu |

## Settings & data files

| Data | Location |
|---|---|
| Settings | `%APPDATA%\QuranWidget\settings.json` |
| Translations cache | `%APPDATA%\QuranWidget\translations\` |
| Prayer location cache | `%APPDATA%\QuranWidget\location.json` |
| Audio cache | `%LOCALAPPDATA%\QuranWidget\audio\` |
| Diagnostics log | `%LOCALAPPDATA%\QuranWidget\log.txt` (tray → *Open Log File*) |

## Install & distribute

- Portable: ship `dist\QuranWidget.exe` (needs the .NET Desktop Runtime), or run
  `build.cmd portable` for a self-contained single exe.
- Installer: install [Inno Setup 6](https://jrsoftware.org/isinfo.php), then

  ```cmd
  ISCC.exe installer\QuranWidget.iss
  ```

  which produces `QuranWidgetSetup-2.6.3.exe` with Start-menu shortcut, optional
  autostart and a clean uninstaller.

## Updates

Set `UpdateRepo` (`"owner/repo"`) in `settings.json` to point at your GitHub
repository; the tray's *Check for Updates* compares the latest release tag against
the running version. Left empty, the check is disabled.

## Build from source

Requires the .NET SDK (10+):

```cmd
build.cmd           :: framework-dependent exe (needs .NET Desktop Runtime installed)
build.cmd portable  :: fully self-contained single exe
```

## Credits

- Quran text: [Tanzil Uthmani](https://tanzil.net), served by
  [alquran.cloud](https://alquran.cloud) API (translations too).
- Recitations: [cdn.islamic.network](https://alquran.cloud/cdn) (128 kbit/s).
- Arabic fonts: [Amiri](https://www.amirifont.org) family (SIL Open Font License) —
  see `QuranWidget\Assets\OFL-Amiri.txt`.
