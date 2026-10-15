using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Windows.Media;

namespace QuranWidget;

public static class Audio
{
    public static readonly (string Id, string Name, string ArName)[] Reciters =
    {
        ("ar.alafasy", "Mishary Alafasy", "مشاري العفاسي"),
        ("ar.abdulbasitmurattal", "Abdul Basit (Murattal)", "عبد الباسط (مرتل)"),
        ("ar.husary", "Mahmoud Al-Husary", "محمود الحصري"),
        ("ar.minshawi", "Muhammad Al-Minshawi", "محمد المنشاوي"),
        ("ar.abdurrahmaansudais", "Abdurrahman As-Sudais", "عبد الرحمن السديس"),
        ("ar.shaatree", "Abu Bakr Ash-Shaatree", "أبو بكر الشاطري"),
        ("ar.hudhaify", "Ali Al-Hudhaify", "علي الحذيفي"),
    };

    public static string ReciterName(int i) =>
        L10n.T(Reciters[Math.Clamp(i, 0, Reciters.Length - 1)].Name,
               Reciters[Math.Clamp(i, 0, Reciters.Length - 1)].ArName);

    public static event Action<string>? Status;
    public static bool PlayingRecitation { get; private set; }
    public static bool ReadingMeaning { get; private set; }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    static MediaPlayer? _player;
    static TaskCompletionSource<bool>? _tcs;
    static SpeechSynthesizer? _tts;
    static int _session;
    static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranWidget", "audio");

    public static void StopAll()
    {
        _session++;
        PlayingRecitation = false;
        ReadingMeaning = false;
        try { _tts?.SpeakAsyncCancelAll(); } catch { /* best effort */ }
        try { _player?.Stop(); } catch { /* best effort */ }
        _tcs?.TrySetResult(false);
        Status?.Invoke("");
    }

    static System.Threading.Timer? _sleepTimer;

    // When the armed sleep timer will stop the recitation (UTC); null when off.
    public static DateTime? SleepEnd { get; private set; }

    // Arms a sleep timer that stops any recitation after `minutes`; 0 cancels it.
    public static void SetSleepTimer(int minutes)
    {
        _sleepTimer?.Dispose();
        _sleepTimer = null;
        if (minutes <= 0) { SleepEnd = null; return; }
        SleepEnd = DateTime.UtcNow.AddMinutes(minutes);
        _sleepTimer = new System.Threading.Timer(_ =>
        {
            _sleepTimer?.Dispose();
            _sleepTimer = null;
            SleepEnd = null;
            StopAll();
            Status?.Invoke(L10n.T("Sleep timer ended the recitation","أنهى مؤقت النوم التلاوة"));
        }, null, minutes * 60_000, System.Threading.Timeout.Infinite);
    }

    // When `next` is provided it is called after each passage finishes; returning a
    // new passage keeps the recitation going (continuous reading), returning null
    // ends it. `next` runs on the UI thread and may update the widget display.
    // `onVerse` is invoked with the index of each verse as it starts playing.
    public static async void PlayRecitation(Passage passage, string reciterId,
        Func<Passage?>? next = null, Action<int>? onVerse = null)
    {
        StopAll();
        if (passage.Verses.Count == 0) return;
        await RunLoop(passage, reciterId, next, onVerse, startVerse: 0);
    }

    // Live playback state, so the reciter can be switched mid-verse and the
    // recitation continues from where it reached with the new voice.
    static Passage _playingPassage = new();
    static int _playingVerse;
    static string _playingReciter = "";
    static Func<Passage?>? _playingNext;
    static Action<int>? _playingOnVerse;

    public static string PlayingReciterId => _playingReciter;

    // Switches the reciter while recitation is playing: fetches the new voice's
    // audio (downloading it on demand) and continues from the verse that was
    // playing. Does nothing when nothing is playing or the reciter is unchanged.
    public static void SwitchReciter(string newReciterId)
    {
        if (!PlayingRecitation) return;
        if (newReciterId == _playingReciter) return;
        var passage = _playingPassage;
        int verse = _playingVerse;
        var next = _playingNext;
        var onVerse = _playingOnVerse;
        StopAll();   // bumps the session, which ends the old loop after the current file
        _ = RunLoop(passage, newReciterId, next, onVerse, startVerse: verse);
    }

    static async Task RunLoop(Passage passage, string reciterId,
        Func<Passage?>? next, Action<int>? onVerse, int startVerse)
    {
        int session = _session;   // captured after the caller bumped it via StopAll
        PlayingRecitation = true;
        _playingPassage = passage;
        _playingReciter = reciterId;
        _playingNext = next;
        _playingOnVerse = onVerse;
        _playingVerse = Math.Clamp(startVerse, 0, Math.Max(0, passage.Verses.Count - 1));
        Status?.Invoke(L10n.T("Preparing recitation…","جارٍ تحضير التلاوة…"));
        try
        {
            while (true)
            {
                // fetch (and cache) the remaining verses of this passage
                for (int v = _playingVerse; v < passage.Verses.Count; v++)
                {
                    if (session != _session) { Log($"cache loop exit: session {session} != {_session}"); return; }
                    await CacheAsync(reciterId, passage.Verses[v].Global);
                }
                for (int i = _playingVerse; i < passage.Verses.Count; i++)
                {
                    if (session != _session || !PlayingRecitation) { Log($"play loop exit: session {session} vs {_session}, playing={PlayingRecitation}"); return; }
                    _playingVerse = i;
                    Status?.Invoke(L10n.T("Reciting…","تلاوة…"));
                    onVerse?.Invoke(i);
                    var file = Path.Combine(CacheRoot, reciterId, passage.Verses[i].Global + ".mp3");
                    var ok = await PlayFileAsync(file);
                    if (session != _session) return;
                    if (!ok)
                    {
                        PlayingRecitation = false;
                        Log($"playback failed: {file}");
                        Status?.Invoke(L10n.T("Could not play the recitation","تعذر تشغيل التلاوة"));
                        return;
                    }
                }
                if (next == null) break;
                var following = next();
                if (following == null || following.Verses.Count == 0) break;
                passage = following;
                _playingPassage = passage;
                _playingVerse = 0;
            }
            if (session != _session) return;
            PlayingRecitation = false;
            onVerse?.Invoke(-1);
            Status?.Invoke("");
        }
        catch (Exception ex)
        {
            Log($"loop exception (session {session}, now {_session}): {ex.Message}");
            if (session != _session) return;
            PlayingRecitation = false;
            Status?.Invoke(L10n.T("Recitation needs an internet connection","تحتاج التلاوة إلى اتصال بالإنترنت"));
        }
    }

    static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuranWidget", "log.txt"), $"{DateTime.Now:u} {message}\n");
        }
        catch
        {
            // diagnostics are best-effort
        }
    }

    static async Task<string> CacheAsync(string reciter, int globalAyah)
    {
        var dir = Path.Combine(CacheRoot, reciter);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, globalAyah + ".mp3");
        if (File.Exists(path)) return path;

        var url = $"https://cdn.islamic.network/quran/audio/128/{reciter}/{globalAyah}.mp3";
        var bytes = await Http.GetByteArrayAsync(url);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    // ---------- audio downloads (listen offline) ----------

    public static bool Downloading { get; private set; }

    // Progress of a bulk download: 0..100 percent; -1 done, -2 cancelled, -3 failed.
    public static event Action<int>? DownloadPercent;

    static CancellationTokenSource? _dlCts;

    // Downloads the recitation audio of the given surahs for a reciter, so it
    // plays back offline later.
    public static async Task<bool> DownloadAudioAsync(string reciter, IEnumerable<int> surahNumbers)
    {
        if (Downloading) return false;
        Downloading = true;
        _dlCts = new CancellationTokenSource();
        var ct = _dlCts.Token;
        try
        {
            var list = surahNumbers.Select(n => Quran.Surah(n)).Where(s => s != null).Cast<Surah>().ToList();
            int total = list.Sum(s => s.Ayahs.Count);
            int done = 0;
            foreach (var surah in list)
            {
                foreach (var v in surah.Ayahs)
                {
                    ct.ThrowIfCancellationRequested();
                    await CacheAsync(reciter, v.Global);
                    done++;
                    DownloadPercent?.Invoke(done * 100 / Math.Max(1, total));
                }
            }
            DownloadPercent?.Invoke(-1);
            return true;
        }
        catch (OperationCanceledException)
        {
            DownloadPercent?.Invoke(-2);
            return false;
        }
        catch
        {
            DownloadPercent?.Invoke(-3);
            return false;
        }
        finally
        {
            Downloading = false;
            _dlCts = null;
        }
    }

    public static void CancelDownload() => _dlCts?.Cancel();

    static Task<bool> PlayFileAsync(string file)
    {
        EnsurePlayer();
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _player!.Open(new Uri(file));
        _player.Play();
        return _tcs.Task;
    }

    static void EnsurePlayer()
    {
        if (_player != null) return;
        _player = new MediaPlayer();
        _player.MediaEnded += (_, _) => _tcs?.TrySetResult(true);
        _player.MediaFailed += (_, _) => _tcs?.TrySetResult(false);
    }

    public static void SpeakMeaning(string text, string preferredVoice)
    {
        StopAll();
        if (string.IsNullOrWhiteSpace(text)) return;
        ReadingMeaning = true;
        Status?.Invoke(L10n.T("Reading the meaning…","جارٍ قراءة المعنى…"));

        var synth = new SpeechSynthesizer();
        _tts = synth;
        try
        {
            var voices = synth.GetInstalledVoices().Where(v => v.Enabled).ToList();
            var pick = voices.FirstOrDefault(v => v.VoiceInfo.Name == preferredVoice)
                       ?? voices.FirstOrDefault(v => v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en")
                       ?? voices.FirstOrDefault();
            if (pick != null) synth.SelectVoice(pick.VoiceInfo.Name);
        }
        catch
        {
            // fall back to the default voice
        }
        synth.SpeakCompleted += (_, _) =>
        {
            ReadingMeaning = false;
            if (ReferenceEquals(_tts, synth)) _tts = null;
            try { synth.Dispose(); } catch { /* best effort */ }
            Status?.Invoke("");
        };
        synth.SpeakAsync(text);
    }

    public static List<string> VoiceNames()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
