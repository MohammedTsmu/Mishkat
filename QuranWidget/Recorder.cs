using System.IO;
using System.Runtime.InteropServices;

namespace QuranWidget;

// Records the user's own recitation through the classic winmm MCI API — no extra
// NuGet packages, works on stock Windows 10/11 with any default recording device.
// Used for the "record yourself, then compare with the reciter" hifz workflow.
public static class Recorder
{
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    static extern int mciSendString(string command, System.Text.StringBuilder? buffer, int bufferSize, IntPtr callback);

    public static bool Recording { get; private set; }

    static string _alias = "";
    static string _file = "";

    static string RecordRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuranWidget", "recordings");

    // Starts recording to a fresh WAV file; returns the file path, or null when
    // no microphone / recording device is available.
    public static string? Start()
    {
        if (Recording) return null;
        try
        {
            Directory.CreateDirectory(RecordRoot);
            _file = Path.Combine(RecordRoot, $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _alias = "qwrec" + Environment.TickCount;
            if (Send($"open new type waveaudio alias {_alias}") != 0) return null;
            if (Send($"record {_alias}") != 0) { Send($"close {_alias}"); return null; }
            Recording = true;
            return _file;
        }
        catch
        {
            return null;
        }
    }

    // Stops the recording and returns the saved file path.
    public static string? Stop()
    {
        if (!Recording) return null;
        try
        {
            Send($"stop {_alias}");
            Send($"save {_alias} \"{_file}\"");
            Send($"close {_alias}");
        }
        catch
        {
            // fall through; Recording is cleared regardless
        }
        Recording = false;
        return File.Exists(_file) ? _file : null;
    }

    public static string? LastFile => Recording ? null : (File.Exists(_file) ? _file : null);

    // Plays a recorded WAV back through MCI (fire-and-forget; StopAll ends it).
    public static void PlayMine(string file)
    {
        StopPlayback();
        Send($"open \"{file}\" alias qwplay");
        Send("play qwplay");
    }

    public static void StopAll()
    {
        if (Recording) Stop();
        StopPlayback();
    }

    static void StopPlayback()
    {
        Send("stop qwplay");
        Send("close qwplay");
    }

    static int Send(string command) =>
        mciSendString(command, null, 0, IntPtr.Zero);

    // Deletes recordings older than a week; called once when the hifz window opens.
    public static void CleanupOld()
    {
        try
        {
            if (!Directory.Exists(RecordRoot)) return;
            foreach (var f in Directory.GetFiles(RecordRoot, "*.wav"))
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7))
                    File.Delete(f);
            }
        }
        catch
        {
            // cleanup is best-effort
        }
    }
}
