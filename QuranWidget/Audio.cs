using System.IO;
using System.Net.Http;
using System.Speech.Synthesis;
using System.Windows.Media;

namespace QuranWidget;

public static class Audio
{
    public static readonly (string Id, string Name)[] Reciters =
    {
        ("ar.alafasy", "Mishary Alafasy"),
        ("ar.abdulbasitmurattal", "Abdul Basit (Murattal)"),
        ("ar.husary", "Mahmoud Al-Husary"),
        ("ar.minshawi", "Muhammad Al-Minshawi"),
        ("ar.abdurrahmaansudais", "Abdurrahman As-Sudais"),
        ("ar.shaatree", "Abu Bakr Ash-Shaatree"),
        ("ar.hudhaify", "Ali Al-Hudhaify"),
    };

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
            Status?.Invoke("Sleep timer ended the recitation");
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
        int session = _session;
        PlayingRecitation = true;
        Status?.Invoke("Preparing recitation…");
        try
        {
            while (true)
            {
                var files = new List<string>();
                foreach (var v in passage.Verses)
                {
                    if (session != _session) return;
                    files.Add(await CacheAsync(reciterId, v.Global));
                }
                for (int i = 0; i < files.Count; i++)
                {
                    if (session != _session || !PlayingRecitation) return;
                    Status?.Invoke("Reciting…");
                    onVerse?.Invoke(i);
                    var ok = await PlayFileAsync(files[i]);
                    if (session != _session) return;
                    if (!ok)
                    {
                        PlayingRecitation = false;
                        Status?.Invoke("Could not play the recitation");
                        return;
                    }
                }
                if (next == null) break;
                var following = next();
                if (following == null || following.Verses.Count == 0) break;
                passage = following;
            }
            if (session != _session) return;
            PlayingRecitation = false;
            onVerse?.Invoke(-1);
            Status?.Invoke("");
        }
        catch (Exception)
        {
            if (session != _session) return;
            PlayingRecitation = false;
            Status?.Invoke("Recitation needs an internet connection");
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
        Status?.Invoke("Reading the meaning…");

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
