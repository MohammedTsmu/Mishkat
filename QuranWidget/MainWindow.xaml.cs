using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace QuranWidget;

public partial class MainWindow : Window
{
    public string Id { get; }
    public bool IsDaily { get; }

    Passage _passage = new();
    DateTime _shownDate = DateTime.Today;
    bool _ready;
    DispatcherTimer? _posSave;
    DispatcherTimer? _dayTimer;
    int _reciterIndex;

    const string GlyphPlay = "\uE768";
    const string GlyphPause = "\uE769";
    const string GlyphVolume = "\uE767";
    const string GlyphStop = "\uE71A";

    public MainWindow(string id, bool daily)
    {
        Id = id;
        IsDaily = daily;
        InitializeComponent();
        Icon = IconFactory.CreateImageSource(64);

        ApplySettings();
        RestorePosition();
        LoadState();

        Audio.Status += OnAudioStatus;
        StartConnectivityMonitor();


        if (IsDaily)
        {
            _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _dayTimer.Tick += (_, _) => CheckDayRollover();
            _dayTimer.Start();
        }

        Loaded += (_, _) =>
        {
            UpdateScrollLimits();
            SaveWindowState();
            OnLoadedStartupPin();
        };
        _ready = true;
    }

    public string CurrentReference => _passage.Reference;

    public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();

    // ---------- verse state ----------

    void LoadState()
    {
        var st = GetState();
        var todayKey = DateTime.Today.ToString("yyyy-MM-dd");

        if (IsDaily)
        {
            _shownDate = DateTime.Today;
            if (st.Day == todayKey && st.S > 0)
                _passage = Quran.GetPassage(st.S, st.A);
            else
                _passage = Quran.DailyPassage(DateTime.Today);
        }
        else if (st.S > 0)
        {
            _passage = Quran.GetPassage(st.S, st.A);
        }
        else
        {
            _passage = Quran.RandomPassage();
        }

        if (_passage.Verses.Count == 0)
        {
            _shownDate = DateTime.Today;
            _passage = Quran.DailyPassage(DateTime.Today);
        }
        if (_passage.Verses.Count == 0)
            _passage = Quran.GetPassage(112, 1, 4);

        ApplyPassage(animate: false);
    }

    // Keep the widget on screen: the verse and translation areas share the available
    // screen height so the footer buttons are always reachable.
    void UpdateScrollLimits()
    {
        var wa = SystemParameters.WorkArea;
        double avail = Math.Max(340, wa.Height * 0.62);
        ArabicScroll.MaxHeight = Math.Round(avail * 0.58);
        TranslationScroll.MaxHeight = Math.Round(avail * 0.42);
    }

    void ApplyPassage(bool animate)
    {
        UpdateScrollLimits();
        ArabicScroll.ScrollToTop();
        TranslationScroll.ScrollToTop();

        var first = _passage.Verses[0];
        var last = _passage.Verses[^1];
        SurahArabicText.Text = first.Surah.Name;
        SurahEnglishText.Text = L10n.T(
            $"{first.Surah.EnglishName} · {first.Surah.Meaning} — {L10n.AyahsLabel(first.NumberInSurah, last.NumberInSurah)}",
            $"{first.Surah.Name} — {L10n.AyahsLabel(first.NumberInSurah, last.NumberInSurah)}");

        ArabicText.Text = "";
        BuildArabicInlines(_passage);
        TranslationText.Text = Quran.EnglishOf(_passage);

        if (IsDaily)
        {
            ModeText.Text = L10n.T("VERSE OF THE DAY", "آية اليوم");
            DateText.Text = L10n.T(
                $"{GregorianToday()}  ·  {HijriToday()}",
                $"{HijriToday()}  ·  {GregorianToday()}");
        }
        else
        {
            ModeText.Text = L10n.T("A VERSE TO REFLECT ON", "آية للتأمل");
            DateText.Text = L10n.T("Tap ⟳ to explore another verse", "اضغط ⟳ لآية أخرى");
        }

        ResetAudioButtons();

        if (animate) AnimateIn();
        SaveWindowState();
    }

    static string HijriToday()
    {
        try
        {
            var cal = new UmAlQuraCalendar();
            var now = DateTime.Today;
            int y = cal.GetYear(now), m = cal.GetMonth(now), d = cal.GetDayOfMonth(now);
            string[] en = { "Muharram", "Safar", "Rabi' al-Awwal", "Rabi' al-Thani",
                            "Jumada al-Ula", "Jumada al-Akhirah", "Rajab", "Sha'ban",
                            "Ramadan", "Shawwal", "Dhu al-Qi'dah", "Dhu al-Hijjah" };
            string[] ar = { "محرم", "صفر", "ربيع الأول", "ربيع الآخر",
                            "جمادى الأولى", "جمادى الآخرة", "رجب", "شعبان",
                            "رمضان", "شوال", "ذو القعدة", "ذو الحجة" };
            int i = Math.Min(m, 12) - 1;
            return L10n.T($"{d} {en[i]} {y} AH", $"{d} {ar[i]} {y} هـ");
        }
        catch
        {
            return "";
        }
    }

    // Gregorian date formatted in the UI language; in Arabic this avoids mixing
    // English text into the right-to-left header, which scrambled the line.
    static string GregorianToday()
    {
        if (!L10n.ArabicUI)
            return DateTime.Today.ToString("dddd, d MMMM yyyy");
        try
        {
            var ar = System.Globalization.CultureInfo.GetCultureInfo("ar");
            return DateTime.Today.ToString("dddd، d MMMM yyyy", ar);
        }
        catch
        {
            return DateTime.Today.ToString("dddd, d MMMM yyyy");
        }
    }

    void CheckDayRollover()
    {
        if (!IsDaily) return;
        var today = DateTime.Today;
        if (_shownDate != today)
        {
            _shownDate = today;
            _passage = Quran.DailyPassage(today);
            ApplyPassage(animate: true);
        }
        else
        {
            DateText.Text = L10n.T(
                $"{GregorianToday()}  ·  {HijriToday()}",
                $"{HijriToday()}  ·  {GregorianToday()}");
        }
    }

    public void ShowAndResetDaily()
    {
        if (IsDaily)
        {
            _shownDate = DateTime.Today;
            _passage = Quran.DailyPassage(DateTime.Today);
            ApplyPassage(animate: true);
        }
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // Renders Arabic verses with the ornate verse-number marker U+FD3F..U+FD3E as an atomic
    // inline element: WPF can wrap before or after it, but never inside it, so the
    // brackets and digits always stay together.
    readonly List<System.Windows.Documents.Run> _verseRuns = new();

    void BuildArabicInlines(Passage p)
    {
        ArabicText.Inlines.Clear();
        _verseRuns.Clear();
        var font = (System.Windows.Media.FontFamily)FindResource("FontQuran");
        var fg = ArabicText.Foreground;
        for (int i = 0; i < p.Verses.Count; i++)
        {
            var v = p.Verses[i];
            if (i > 0) ArabicText.Inlines.Add(new System.Windows.Documents.LineBreak());
            var run = new System.Windows.Documents.Run(v.Arabic + "\u00A0");
            _verseRuns.Add(run);
            ArabicText.Inlines.Add(run);
            var marker = new TextBlock
            {
                Text = $"\uFD3F{Quran.ToArabicDigits(v.NumberInSurah)}\uFD3E",
                FontFamily = font,
                FontSize = ArabicText.FontSize,
                Foreground = fg,
            };
            ArabicText.Inlines.Add(new System.Windows.Documents.InlineUIContainer(marker));
        }
    }

    void HighlightVerse(int indexInPassage)
    {
        var normal = (System.Windows.Media.Brush)FindResource("TextPrimary");
        var gold = (System.Windows.Media.Brush)FindResource("Gold");
        for (int i = 0; i < _verseRuns.Count; i++)
            _verseRuns[i].Foreground = i == indexInPassage ? gold : normal;
        if (indexInPassage >= 0 && indexInPassage < _verseRuns.Count)
            ScrollToVerse(indexInPassage);
    }

    // Keeps the recited verse visible: scrolls the Arabic text to the highlighted
    // run and moves the translation proportionally, so live highlighting is never
    // off-screen at the bottom.
    void ScrollToVerse(int i)
    {
        var run = _verseRuns[i];
        var rect = run.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
        if (!rect.IsEmpty)
            ArabicScroll.ScrollToVerticalOffset(Math.Max(0, rect.Y - 14));

        double total = TranslationText.ActualHeight - TranslationScroll.ViewportHeight;
        if (total > 4 && _verseRuns.Count > 1)
            TranslationScroll.ScrollToVerticalOffset(total * i / (_verseRuns.Count - 1));
    }

    void AnimateIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BodyGrid.BeginAnimation(OpacityProperty, fade);
        BodyTt.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    // ---------- audio ----------

    // ---------- connectivity indicator ----------

    // Recitation streams audio from the internet; texts work offline. The header
    // wifi-off vector icon appears whenever internet is truly unavailable — detected
    // both from Windows network events and from recitation failures ("connected but
    // no internet" is verified with a lightweight probe).
    bool _offlineShown;
    DispatcherTimer? _probeTimer;
    static readonly HttpClient ProbeHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    void OnAudioStatus(string msg)
    {
        if (msg.Length == 0)
        {
            ResetAudioButtons();
            StopShimmer();
            return;
        }
        if (msg.Contains("internet", StringComparison.OrdinalIgnoreCase))
            GoOffline();
        if (msg.StartsWith("Preparing", StringComparison.OrdinalIgnoreCase))
            StartShimmer();
        else
            StopShimmer();
        ShowStatus(msg);
    }

    void StartConnectivityMonitor()
    {
        NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        _probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _probeTimer.Tick += (_, _) => { if (_offlineShown) ProbeInternet(); };
        _probeTimer.Start();
        UpdateConnectivity();
    }

    void NetworkChanged(object? sender, EventArgs e) => OnConnectivityMaybeChanged();

    void OnConnectivityMaybeChanged()
    {
        // events can arrive on a background thread and in bursts; coalesce on the UI thread
        Dispatcher.BeginInvoke(UpdateConnectivity);
    }

    void UpdateConnectivity()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            GoOffline();
            return;
        }
        // adapter says "up" but there may be no internet (router without uplink)
        if (_offlineShown) ProbeInternet();
    }

    async void ProbeInternet()
    {
        try
        {
            using var resp = await ProbeHttp.GetAsync(
                "https://cdn.islamic.network/quran/audio/128/ar.alafasy/1.mp3",
                HttpCompletionOption.ResponseHeadersRead);
            if (resp.IsSuccessStatusCode) GoOnline();
        }
        catch
        {
            // still offline; the probe timer will retry
        }
    }

    void GoOffline()
    {
        if (_offlineShown) { PlayButton.IsEnabled = false; return; }
        _offlineShown = true;
        OfflineIcon.Visibility = Visibility.Visible;
        OfflineIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(1400))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        PlayButton.IsEnabled = false;
        PlayButton.Content = GlyphPlay;
        if (Audio.PlayingRecitation)
        {
            Audio.StopAll();
            ShowStatus(L10n.T("Recitation stopped — no internet", "أُوقفت التلاوة — لا يوجد إنترنت"));
        }
    }

    void GoOnline()
    {
        if (!_offlineShown) return;
        _offlineShown = false;
        OfflineIcon.BeginAnimation(OpacityProperty, null);
        OfflineIcon.Visibility = Visibility.Collapsed;
        PlayButton.IsEnabled = true;
        ShowStatus(L10n.T("Back online — recitation is available again", "عاد الاتصال — التلاوة متاحة مجدداً"));
    }

    // ---------- skeleton shimmer ----------

    DoubleAnimation? _shimmerAnim;

    void StartShimmer()
    {
        ShimmerBorder.Visibility = Visibility.Visible;
        _shimmerAnim = new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1100))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        ShimmerTt.BeginAnimation(TranslateTransform.XProperty, _shimmerAnim);
        ShimmerBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    void StopShimmer()
    {
        if (ShimmerBorder.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => { if (ShimmerBorder.Opacity <= 0) ShimmerBorder.Visibility = Visibility.Collapsed; };
        ShimmerBorder.BeginAnimation(OpacityProperty, fade);
        ShimmerTt.BeginAnimation(TranslateTransform.XProperty, null);
    }

    bool HasInternet() =>
        NetworkInterface.GetIsNetworkAvailable();

    void ResetAudioButtons()
    {
        PlayButton.Content = Audio.PlayingRecitation ? GlyphStop : GlyphPlay;
        TtsButton.Content = Audio.ReadingMeaning ? GlyphStop : GlyphVolume;
    }

    int _versesPlayed;

    void PlayRecitation_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.PlayingRecitation)
        {
            Audio.StopAll();
            return;
        }
        if (!HasInternet())
        {
            ShowStatus(L10n.T("No internet — recitation is unavailable", "لا يوجد إنترنت — التلاوة غير متاحة"));
            return;
        }
        _versesPlayed = 0;

        // resume a previous continuous-reading session if one was saved
        var s = SettingsService.Current;
        if (s.ContinueReading && s.ResumeS > 0 &&
            (s.ResumeS != _passage.Verses[0].SurahNumber || s.ResumeA != _passage.Verses[0].NumberInSurah))
        {
            _passage = Quran.GetPassage(s.ResumeS, s.ResumeA, s.ResumeA + 2);
            ApplyPassage(animate: false);
            var resumed = L10n.SurahRef(s.ResumeS, s.ResumeA);
            ShowStatus(L10n.T($"Resumed from {resumed}", $"استؤنف من {resumed}"));
        }

        // remember the position so playback can resume later
        // (set the glyph after PlayRecitation: its internal StopAll raises a status
        // that would otherwise reset the button to the play glyph)
        PlayButton.Content = GlyphStop;
        Audio.PlayRecitation(_passage, SettingsService.Current.ReciterId,
            next: NextReadingPassage, onVerse: HighlightVerse);
        PlayButton.Content = GlyphStop;
        UpdateResumeText();
    }

    // Called by Audio after each passage finishes; returns the next passage to
    // recite, or null to stop. Runs on the UI thread.
    Passage? NextReadingPassage()
    {
        var s = SettingsService.Current;
        if (!s.ContinueReading) return null;

        _versesPlayed += _passage.Verses.Count;
        int limit = s.StopAt switch { 1 => 10, 2 => 25, 3 => 50, _ => int.MaxValue };
        if (_versesPlayed >= limit) return null;

        var last = _passage.Verses[^1];
        int surahNo = last.SurahNumber;
        int start = last.NumberInSurah + 1;

        if (start > last.Surah.Ayahs.Count)
        {
            // reached the end of the surah
            if (s.StopAt == 0) return null;
            surahNo = surahNo % 114 + 1; // continue into the next surah
            start = 1;
        }

        var surah = Quran.Surah(surahNo);
        if (surah == null) return null;
        int len = Math.Min(3, limit - _versesPlayed);
        int end = Math.Min(start + Math.Max(len, 1) - 1, surah.Ayahs.Count);
        var next = Quran.GetPassage(surahNo, start, end);
        if (next.Verses.Count == 0) return null;

        _passage = next;
        ApplyPassage(animate: true);
        var reciting = L10n.SurahRef(next.Verses[0].SurahNumber, next.Verses[0].NumberInSurah,
                                     next.Verses[^1].NumberInSurah);
        ShowStatus(L10n.T($"Reciting… {reciting}", $"تلاوة… {reciting}"));

        // remember the position so playback can resume later
        SettingsService.Current.ResumeS = next.Verses[0].SurahNumber;
        SettingsService.Current.ResumeA = next.Verses[0].NumberInSurah;
        SettingsService.Save();
        UpdateResumeText();
        return next;
    }

    void SpeakMeaning_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.ReadingMeaning)
        {
            Audio.StopAll();
            return;
        }
        Audio.SpeakMeaning(Quran.EnglishOf(_passage), SettingsService.Current.VoiceName);
        TtsButton.Content = GlyphStop;
    }

    void NextVerse_Click(object sender, RoutedEventArgs e)
    {
        Audio.StopAll();
        _passage = Quran.RandomPassage(_passage);
        ApplyPassage(animate: true);
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var first = _passage.Verses[0];
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Quran.ArabicOf(_passage));
            sb.AppendLine();
            sb.AppendLine(Quran.EnglishOf(_passage));
            sb.Append($"— Quran, Surah {first.Surah.EnglishName} ({first.Surah.Meaning}) — {L10n.AyahsLabel(first.NumberInSurah, _passage.Verses[^1].NumberInSurah)}");
            System.Windows.Clipboard.SetText(sb.ToString());
            ShowStatus(L10n.T("Copied to clipboard", "تم النسخ"));
        }
        catch
        {
            ShowStatus(L10n.T("Could not copy", "تعذر النسخ"));
        }
    }

    void ShowStatus(string msg)
    {
        StatusText.Text = msg;
        var target = msg.Length == 0 ? 0.0 : 1.0;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(250));
        StatusText.BeginAnimation(OpacityProperty, anim);
    }

    // ---------- settings panel ----------

    void ApplySettings()
    {
        var s = SettingsService.Current;
        Opacity = s.Opacity;
        Topmost = s.AlwaysOnTop && !s.PinToDesktop;
        _reciterIndex = Array.FindIndex(Audio.Reciters, r => r.Id == s.ReciterId);
        if (_reciterIndex < 0) _reciterIndex = 0;
        _dlReciterIndex = _reciterIndex;
        UpdateReciterButton();
        ContinueCheck.IsChecked = s.ContinueReading;
        StopAtToggle.Content = StopAtLabel(s.StopAt);
        ThemeToggle.Content = ThemeService.NameOf(s.Theme);
        TranslationToggle.Content = TranslationName(s.TranslationId);
        SleepToggle.Content = SleepLabel(s.SleepMinutes);
        if (Audio.SleepEnd != null) SyncSleepCountdown();
        UpdateResumeText();
        PrayerCheck.IsChecked = s.PrayerEnabled;
        ShowTranslationCheck.IsChecked = s.ShowTranslation;
        FontSizeSlider.Value = Math.Clamp(s.ArabicFontSize, 18, 40);
        ApplyFontSize();
        ApplyLanguage();
        PlayButton.ToolTip = s.ContinueReading ? "Play recitation (continuous reading)" : "Play recitation";
        SyncFavoriteStar();
    }

    static string TranslationName(string id) =>
        Translations.DisplayName(Array.FindIndex(Translations.Available, t => t.Id == id));

    // ---------- downloads (offline audio) ----------

    int _dlReciterIndex;
    int _dlScope;          // 0 = current surah, 1 = a chosen surah, 2 = whole Quran
    int _dlSurah = 1;      // surah chosen for scope 1
    bool _pickingDl;

    string DlReciterName => Audio.ReciterName(_dlReciterIndex);

    string DlScopeLabel(int scope)
    {
        var cur = _passage.Verses.Count > 0 ? Quran.Surah(_passage.Verses[0].SurahNumber) : null;
        string current = cur != null
            ? L10n.T($"Current surah ({cur.EnglishName})", $"السورة الحالية ({cur.Name})")
            : L10n.T("Current surah", "السورة الحالية");
        return scope switch
        {
            0 => current,
            1 => L10n.T($"Surah {Quran.Surah(_dlSurah)?.Number}: {Quran.Surah(_dlSurah)?.EnglishName}",
                        $"سورة {Quran.Surah(_dlSurah)?.Name}"),
            _ => L10n.T("Whole Quran (114 surahs)", "المصحف كاملاً (114 سورة)"),
        };
    }

    void UpdateDownloadsUi()
    {
        DlReciterCaption.Text = L10n.T("Reciter", "القارئ");
        DlScopeCaption.Text = L10n.T("Content", "المحتوى");
        if (DlReciterToggle.IsChecked != true) DlReciterToggle.Content = DlReciterName;
        if (DlScopeToggle.IsChecked != true) DlScopeToggle.Content = DlScopeLabel(_dlScope);
        DownloadsHint.Text = L10n.T(
            "Save recitation audio to your device for the reciter and content chosen above, so it plays without internet. Translations are downloaded automatically when you pick them.",
            "احفظ صوت التلاوة للقارئ والمحتوى المختارين أعلاه ليعمل بدون إنترنت. تُنزَّل الترجمات تلقائياً عند اختيارها.");
        DownloadStartButton.Content = L10n.T("Download", "تنزيل");
        DownloadStartButton.IsEnabled = !Audio.Downloading;
    }

    void DlReciterToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (DlReciterToggle.IsChecked == true)
        {
            _pickingDl = true;
            DlReciterList.ItemsSource = Enumerable.Range(0, Audio.Reciters.Length).Select(Audio.ReciterName).ToList();
            DlReciterList.SelectedIndex = _dlReciterIndex;
            DlReciterList.ScrollIntoView(DlReciterList.SelectedItem);
            _pickingDl = false;
            DlReciterPopup.IsOpen = true;
        }
        else DlReciterPopup.IsOpen = false;
    }

    void DlReciterPopup_Closed(object? sender, EventArgs e) => DlReciterToggle.IsChecked = false;

    void DlReciterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingDl) return;
        if (DlReciterList.SelectedIndex >= 0) _dlReciterIndex = DlReciterList.SelectedIndex;
        DlReciterPopup.IsOpen = false;
        UpdateDownloadsUi();
    }

    void DlScopeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (DlScopeToggle.IsChecked == true)
        {
            _pickingDl = true;
            _dlScopePhase = false;
            DlScopeList.ItemsSource = new List<string> { DlScopeLabel(0), DlScopeLabel(2) };
            DlScopeList.SelectedIndex = -1;
            _pickingDl = false;
            DlScopePopup.IsOpen = true;
        }
        else DlScopePopup.IsOpen = false;
    }

    void DlScopePopup_Closed(object? sender, EventArgs e)
    {
        DlScopeToggle.IsChecked = false;
        _dlScopePhase = false;
    }

    bool _dlScopePhase; // false = choose kind, true = choose a surah

    void DlScopeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingDl) return;
        int i = DlScopeList.SelectedIndex;
        if (i < 0) return;

        if (!_dlScopePhase)
        {
            if (i == 1) { _dlScope = 2; DlScopePopup.IsOpen = false; UpdateDownloadsUi(); return; }
            // open the surah list
            _pickingDl = true;
            _dlScopePhase = true;
            DlScopeList.ItemsSource = Enumerable.Range(1, 114).Select(n =>
                L10n.T($"{n}. {Quran.Surah(n)?.EnglishName} — {Quran.Surah(n)?.Meaning}",
                       $"{n}. {Quran.Surah(n)?.Name}")).ToList();
            DlScopeList.SelectedIndex = _dlSurah - 1;
            DlScopeList.ScrollIntoView(DlScopeList.SelectedItem);
            _pickingDl = false;
            return;
        }

        _dlScope = 1;
        _dlSurah = i + 1;
        DlScopePopup.IsOpen = false;
        UpdateDownloadsUi();
    }

    async void DownloadStart_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.Downloading) return;
        if (!HasInternet())
        {
            ShowStatus(L10n.T("No internet — downloads are unavailable", "لا يوجد إنترنت — التنزيل غير متاح"));
            return;
        }

        IEnumerable<int> surahs;
        if (_dlScope == 2)
        {
            var r = System.Windows.MessageBox.Show(this,
                L10n.T($"Download the whole Quran recited by {DlReciterName} (~2.5 GB)?\nThis needs a while and a stable connection.",
                       $"تنزيل المصحف كاملاً بصوت {DlReciterName} (~2.5 جيجابايت)؟\nقد يستغرق وقتاً طويلاً ويتطلب اتصالاً مستقراً."),
                "Quran Widget", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            surahs = Enumerable.Range(1, 114);
        }
        else
        {
            if (_dlScope == 0)
            {
                if (_passage.Verses.Count == 0) return;
                _dlSurah = _passage.Verses[0].SurahNumber;
                UpdateDownloadsUi();
            }
            surahs = new[] { _dlSurah };
        }

        string reciterId = Audio.Reciters[_dlReciterIndex].Id;

        UpdateDownloadsUi();
        DownloadsProgressPanel.Visibility = Visibility.Visible;
        DownloadsStatus.Text = L10n.T("Downloading…", "جارٍ التنزيل…");
        DownloadCancelButton.ToolTip = L10n.T("Cancel download", "إلغاء التنزيل");

        void OnPercent(int pct)
        {
            if (pct >= 0)
            {
                DownloadsProgress.Value = pct;
                DownloadsStatus.Text = L10n.T($"Downloading… {pct}%", $"جارٍ التنزيل… {pct}٪");
            }
            else
            {
                DownloadsProgressPanel.Visibility = Visibility.Collapsed;
                DownloadsStatus.Text = pct switch
                {
                    -1 => L10n.T("Download complete — recitation now works offline", "اكتمل التنزيل — تعمل التلاوة بدون إنترنت"),
                    -2 => L10n.T("Download cancelled", "أُلغي التنزيل"),
                    _ => L10n.T("Download failed — check your internet", "فشل التنزيل — تحقق من الإنترنت"),
                };
                UpdateDownloadsUi();
            }
        }
        Audio.DownloadPercent += OnPercent;
        await Audio.DownloadAudioAsync(reciterId, surahs);
        Audio.DownloadPercent -= OnPercent;
    }

    void DownloadCancel_Click(object sender, RoutedEventArgs e) => Audio.CancelDownload();

    // ---------- language (English / Arabic UI) ----------

    void ApplyLanguage()
    {
        FlowDirection = L10n.ArabicUI ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        // header + body text depend on the passage; refresh them if one is loaded
        if (_passage.Verses.Count > 0)
            ApplyPassage(animate: false);

        // settings panel
        // refresh the selected-value toggle buttons so they follow the UI language too
        ReciterToggle.Content = Audio.ReciterName(_reciterIndex);
        ThemeToggle.Content = ThemeService.NameOf(SettingsService.Current.Theme);
        TranslationToggle.Content = TranslationName(SettingsService.Current.TranslationId);
        ReciterLabel.Text = L10n.T("RECITER", "القارئ");
        TranslationLabel.Text = L10n.T("TRANSLATION", "الترجمة");
        ThemeLabel.Text = L10n.T("THEME", "المظهر");
        OpacityLabel.Text = L10n.T("OPACITY", "الشفافية");
        FontSizeLabel.Text = L10n.T("ARABIC FONT SIZE", "حجم خط المصحف");
        ContinueLabel.Text = L10n.T("CONTINUOUS READING", "متابعة القراءة");
        SleepLabel2.Text = L10n.T("SLEEP TIMER", "مؤقت النوم");
        UpdatesLabel.Text = L10n.T("UPDATES", "التحديثات");
        LanguageLabel.Text = L10n.T("LANGUAGE", "اللغة");
        LangButton.Content = L10n.T("English / العربية", "العربية / English");
        CheckUpdatesButton.Content = L10n.T("Check for updates now", "تحقق من التحديثات الآن");
        ClearResumeButton.Content = L10n.T("Clear saved reading position", "مسح موضع القراءة المحفوظ");

        TopmostCheck.Content = L10n.T("Always on top", "دائماً في المقدمة");
        PinCheck.Content = L10n.T("Pin to the desktop background", "تثبيت في خلفية سطح المكتب");
        StartupCheck.Content = L10n.T("Start with Windows", "البدء مع Windows");
        PrayerCheck.Content = L10n.T("Show next prayer time (auto-locates once)", "إظهار وقت الصلاة القادمة (يحدد الموقع تلقائياً)");
        ShowTranslationCheck.Content = L10n.T("Show translation", "إظهار الترجمة");
        ContinueCheck.Content = L10n.T("Keep reading — continue to the next verses", "متابعة القراءة — انتقل إلى الآيات التالية");

        StopAtToggle.Content = StopAtLabel(SettingsService.Current.StopAt);
        SleepToggle.Content = SleepLabel(SettingsService.Current.SleepMinutes);
        SyncTranslationToggle();

        // tooltips
        ToolTipService.SetToolTip(SearchButton, L10n.T("Search the Quran (text or verse number like 2:255)", "ابحث في القرآن (نص أو رقم آية مثل 2:255)"));
        ToolTipService.SetToolTip(PlayButton, SettingsService.Current.ContinueReading
            ? L10n.T("Play recitation (continuous reading)", "تشغيل التلاوة (متابعة القراءة)")
            : L10n.T("Play recitation", "تشغيل التلاوة"));
        ToolTipService.SetToolTip(TtsButton, L10n.T("Read the meaning aloud", "قراءة المعنى صوتياً"));
        ToolTipService.SetToolTip(NextButton, L10n.T("Another verse", "آية أخرى"));
        ToolTipService.SetToolTip(HifzButton, L10n.T("Memorize (Hifz)", "الحفظ"));
        ToolTipService.SetToolTip(FavoriteButton, L10n.T("Favorites — tap to save this verse", "المفضلة — اضغط لحفظ هذه الآية"));
        ToolTipService.SetToolTip(CopyButton, L10n.T("Copy", "نسخ"));
        SearchBoxHint.Text = L10n.T("Type words to search, or 2:255 to jump to a verse",
                                    "اكتب كلمات للبحث، أو 2:255 للانتقال إلى آية");

        ApplyTranslationVisibility();
        UpdateResumeText();
        UpdateDownloadsUi();
        SettingsTitle.Text = L10n.T("Settings", "الإعدادات");
        DownloadsLabel.Text = L10n.T("DOWNLOADS", "التنزيلات");
        SettingsScrollHint.Text = L10n.T("Scroll for more ↓", "مرّر للمزيد ↓");
        CreditsText.Text = L10n.T(
            "Recitation: cdn.islamic.network  ·  Translations: api.alquran.cloud",
            "التلاوة: cdn.islamic.network  ·  الترجمات: api.alquran.cloud");
    }

    void Language_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.Language = L10n.ArabicUI ? "en" : "ar";
        SettingsService.Save();
        ApplyLanguage();
        App.Instance.ApplyTrayLanguage();
        ShowStatus(L10n.T("Language: English", "اللغة: العربية"));
    }

    void ShowTranslation_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = ShowTranslationCheck.IsChecked == true;
        if (on == SettingsService.Current.ShowTranslation) return;
        SettingsService.Current.ShowTranslation = on;
        SettingsService.Save();
        ApplyTranslationVisibility();
    }

    void ApplyTranslationVisibility()
    {
        bool on = SettingsService.Current.ShowTranslation;
        TranslationScroll.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        DividerGrid.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    static readonly int[] SleepOptions = { 0, 10, 20, 30, 60 };

    static string SleepLabel(int minutes) =>
        minutes <= 0 ? L10n.T("Off — play until I stop", "بدون توقيت — تشغيل حتى أوقفه")
                     : L10n.T($"Stop after {minutes} minutes", $"التوقف بعد {minutes} دقيقة");

    // Live countdown shown in the footer while a sleep timer is armed, so the
    // user always knows when recitation will stop.
    DispatcherTimer? _sleepUiTimer;

    void SyncSleepCountdown()
    {
        if (Audio.SleepEnd == null) { _sleepUiTimer?.Stop(); return; }
        _sleepUiTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sleepUiTimer.Tick -= SleepUi_Tick;
        _sleepUiTimer.Tick += SleepUi_Tick;
        _sleepUiTimer.Start();
        SleepUi_Tick(null, null!);
    }

    void SleepUi_Tick(object? sender, EventArgs e)
    {
        var end = Audio.SleepEnd;
        if (end == null)
        {
            _sleepUiTimer?.Stop();
            if (StatusText.Text.StartsWith("Sleeping in", StringComparison.Ordinal) ||
                StatusText.Text.StartsWith("سيتم التوقف بعد", StringComparison.Ordinal))
                ShowStatus("");
            return;
        }
        var rem = end.Value - DateTime.UtcNow;
        if (rem <= TimeSpan.Zero) return;
        ShowStatus(L10n.T($"Sleeping in {(int)rem.TotalMinutes}:{rem.Seconds:00}",
                          $"سيتم التوقف بعد {(int)rem.TotalMinutes}:{rem.Seconds:00}"));
    }

    void ApplyFontSize()
    {
        double size = Math.Clamp(SettingsService.Current.ArabicFontSize, 18, 40);
        ArabicText.FontSize = size;
        TranslationText.FontSize = Math.Clamp(size * 0.55, 11, 22);
        TranslationText.LineHeight = TranslationText.FontSize * 1.5;
    }

    static readonly (string En, string Ar)[] StopAtLabels =
    {
        ("Stop at the end of the surah", "التوقف في نهاية السورة"),
        ("Stop after 10 verses", "التوقف بعد 10 آيات"),
        ("Stop after 25 verses", "التوقف بعد 25 آية"),
        ("Stop after 50 verses", "التوقف بعد 50 آية"),
        ("Keep going until I stop", "الاستمرار حتى أوقفه"),
    };

    static string StopAtLabel(int i) =>
        L10n.T(StopAtLabels[Math.Clamp(i, 0, StopAtLabels.Length - 1)].En,
               StopAtLabels[Math.Clamp(i, 0, StopAtLabels.Length - 1)].Ar);

    void UpdateReciterButton()
    {
        ReciterToggle.Content = Audio.ReciterName(_reciterIndex);
    }

    void Hifz_Click(object sender, RoutedEventArgs e) => App.Instance.ShowHifz();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        SyncSettingsChecks();
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    void SettingsClose_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    // Hides the fade / "Scroll for more" hint once the user reaches the bottom.
    void SettingsScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;
        bool atEnd = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 4;
        SettingsFade.Opacity = atEnd ? 0 : 1;
        SettingsScrollHint.Opacity = atEnd ? 0 : 1;
    }

    void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        ShowStatus(L10n.T("Checking for updates…", "جارٍ التحقق من التحديثات…"));
        App.Instance.CheckForUpdates();
    }

    void SyncSettingsChecks()
    {
        TopmostCheck.IsChecked = SettingsService.Current.AlwaysOnTop;
        PinCheck.IsChecked = SettingsService.Current.PinToDesktop;
        StartupCheck.IsChecked = Autostart.IsEnabled();
    }

    bool _pickingReciter;

    void ReciterToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (ReciterToggle.IsChecked == true)
        {
            _pickingReciter = true;
            ReciterList.ItemsSource = Enumerable.Range(0, Audio.Reciters.Length).Select(Audio.ReciterName).ToList();
            ReciterList.SelectedIndex = _reciterIndex;
            ReciterList.ScrollIntoView(ReciterList.SelectedItem);
            _pickingReciter = false;
            ReciterPopup.IsOpen = true;
        }
        else
        {
            ReciterPopup.IsOpen = false;
        }
    }

    void ReciterPopup_Closed(object? sender, EventArgs e)
    {
        ReciterToggle.IsChecked = false;
    }

    void ReciterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingReciter) return;
        int i = ReciterList.SelectedIndex;
        if (i < 0 || i >= Audio.Reciters.Length) return;
        if (i != _reciterIndex)
        {
            if (Audio.PlayingRecitation) Audio.StopAll();
            _reciterIndex = i;
            SettingsService.Current.ReciterId = Audio.Reciters[i].Id;
            UpdateReciterButton();
            SettingsService.Save();
        }
        ReciterPopup.IsOpen = false;
    }

    bool _pickingStopAt;

    void Continue_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = ContinueCheck.IsChecked == true;
        if (on == SettingsService.Current.ContinueReading) return;
        SettingsService.Current.ContinueReading = on;
        SettingsService.Save();
        PlayButton.ToolTip = on ? L10n.T("Play recitation (continuous reading)","تشغيل التلاوة (متابعة القراءة)") : L10n.T("Play recitation","تشغيل التلاوة");
        ShowStatus(on ? "Continuous reading on — the recitation will continue to the next verses"
                      : "Continuous reading off");
    }

    void StopAtToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (StopAtToggle.IsChecked == true)
        {
            _pickingStopAt = true;
            StopAtList.ItemsSource = StopAtLabels.ToList();
            StopAtList.SelectedIndex = Math.Clamp(SettingsService.Current.StopAt, 0, StopAtLabels.Length - 1);
            StopAtList.ScrollIntoView(StopAtList.SelectedItem);
            _pickingStopAt = false;
            StopAtPopup.IsOpen = true;
        }
        else
        {
            StopAtPopup.IsOpen = false;
        }
    }

    void StopAtPopup_Closed(object? sender, EventArgs e)
    {
        StopAtToggle.IsChecked = false;
    }

    void StopAtList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingStopAt) return;
        int i = StopAtList.SelectedIndex;
        if (i < 0) return;
        if (i != SettingsService.Current.StopAt)
        {
            SettingsService.Current.StopAt = i;
            StopAtToggle.Content = StopAtLabel(i);
            SettingsService.Save();
        }
        StopAtPopup.IsOpen = false;
    }

    void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        SettingsService.Current.Opacity = OpacitySlider.Value;
        Opacity = OpacitySlider.Value;
        SettingsService.Save();
    }

    void FontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        SettingsService.Current.ArabicFontSize = FontSizeSlider.Value;
        ApplyFontSize();
        SettingsService.Save();
    }

    // ---------- theme picker ----------

    bool _pickingTheme;

    void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (ThemeToggle.IsChecked == true)
        {
            _pickingTheme = true;
            ThemeList.ItemsSource = Enumerable.Range(0, ThemeService.Themes.Length).Select(ThemeService.DisplayName).ToList();
            ThemeList.SelectedIndex = Array.FindIndex(ThemeService.Themes, t => t.Id == SettingsService.Current.Theme);
            if (ThemeList.SelectedIndex < 0) ThemeList.SelectedIndex = 0;
            ThemeList.ScrollIntoView(ThemeList.SelectedItem);
            _pickingTheme = false;
            ThemePopup.IsOpen = true;
        }
        else
        {
            ThemePopup.IsOpen = false;
        }
    }

    void ThemePopup_Closed(object? sender, EventArgs e) => ThemeToggle.IsChecked = false;

    void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingTheme) return;
        int i = ThemeList.SelectedIndex;
        if (i < 0 || i >= ThemeService.Themes.Length) return;
        var id = ThemeService.Themes[i].Id;
        if (id != SettingsService.Current.Theme)
        {
            SettingsService.Current.Theme = id;
            ThemeToggle.Content = ThemeService.NameOf(id);
            ThemeService.Apply(id);
            if (!SettingsService.Current.PinToDesktop) WindowEffects.EnableAcrylic(this);
            SyncFavoriteStar();
            SettingsService.Save();
        }
        ThemePopup.IsOpen = false;
    }

    // ---------- translation picker ----------

    bool _pickingTranslation;

    void TranslationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (TranslationToggle.IsChecked == true)
        {
            _pickingTranslation = true;
            TranslationList.ItemsSource = Translations.Available
                .Select(t => Translations.DisplayName(Array.FindIndex(Translations.Available, a => a.Id == t.Id))
                             + (Translations.IsDownloaded(t.Id) ? "" : L10n.T("  (download)", "  (تنزيل)"))).ToList();
            TranslationList.SelectedIndex = Array.FindIndex(Translations.Available, t => t.Id == SettingsService.Current.TranslationId);
            if (TranslationList.SelectedIndex < 0) TranslationList.SelectedIndex = 0;
            TranslationList.ScrollIntoView(TranslationList.SelectedItem);
            _pickingTranslation = false;
            TranslationPopup.IsOpen = true;
        }
        else
        {
            TranslationPopup.IsOpen = false;
        }
    }

    void TranslationPopup_Closed(object? sender, EventArgs e) => TranslationToggle.IsChecked = false;

    async void TranslationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingTranslation) return;
        int i = TranslationList.SelectedIndex;
        if (i < 0 || i >= Translations.Available.Length) return;
        var id = Translations.Available[i].Id;
        var name = Translations.DisplayName(i);
        TranslationPopup.IsOpen = false;
        if (id == SettingsService.Current.TranslationId) return;

        if (!Translations.IsDownloaded(id))
        {
            if (!HasInternet())
            {
                ShowStatus(L10n.T("Needs internet to download this translation", "يلزم إنترنت لتنزيل هذه الترجمة"));
                SyncTranslationToggle();
                return;
            }
            ShowStatus(L10n.T($"Downloading {name}…", $"جارٍ تنزيل {name}…"));
            bool ok = await Translations.DownloadAsync(id);
            if (!ok)
            {
                ShowStatus(L10n.T("Could not download the translation", "تعذر تنزيل الترجمة"));
                SyncTranslationToggle();
                return;
            }
        }
        SettingsService.Current.TranslationId = id;
        SettingsService.Save();
        Translations.Load(id);
        SyncTranslationToggle();
        ApplyPassage(animate: true);
        ShowStatus(L10n.T($"Translation: {name}", $"الترجمة: {name}"));
    }

    void SyncTranslationToggle() =>
        TranslationToggle.Content = TranslationName(SettingsService.Current.TranslationId);

    // ---------- search ----------

    DispatcherTimer? _searchDebounce;
    readonly List<Translations.SearchHit> _searchHits = new();

    void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchPopup.IsOpen = true;
        SearchBox.Focus();
    }

    void SearchPopup_Closed(object? sender, EventArgs e)
    {
        SearchBox.Clear();
        SearchList.Items.Clear();
        SearchList.Visibility = Visibility.Collapsed;
    }


    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _searchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Stop();
        _searchDebounce.Tick -= SearchDebounce_Tick;
        _searchDebounce.Tick += SearchDebounce_Tick;
        _searchDebounce.Start();
    }

    void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        RunSearch();
    }

    void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchList.Items.Count > 0)
        {
            SearchList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    void RunSearch()
    {
        var q = SearchBox.Text.Trim();
        SearchList.Items.Clear();
        _searchHits.Clear();
        if (q.Length == 0)
        {
            SearchList.Visibility = Visibility.Collapsed;
            return;
        }

        // "2:255" / "2 255" style reference jump
        var m = System.Text.RegularExpressions.Regex.Match(q, @"^(\d{1,3})\s*[:\s.]\s*(\d{1,3})$");
        if (m.Success)
        {
            int s = int.Parse(m.Groups[1].Value), a = int.Parse(m.Groups[2].Value);
            if (Quran.Surah(s) is { } surah && a >= 1 && a <= surah.Ayahs.Count)
            {
                _searchHits.Add(new Translations.SearchHit(s, a, "Jump to verse"));
                SearchList.Items.Add($"{s}:{a} — {surah.EnglishName} · jump to verse");
            }
        }

        foreach (var hit in Translations.Search(q, 10))
        {
            _searchHits.Add(hit);
            SearchList.Items.Add($"{hit.S}:{hit.A}  ·  {hit.Text}");
        }

        SearchList.Visibility = SearchList.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void SearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        int i = SearchList.SelectedIndex;
        SearchList.SelectedIndex = -1;
        if (i < 0 || i >= _searchHits.Count) return;
        var hit = _searchHits[i];
        _passage = Quran.GetPassage(hit.S, hit.A);
        ApplyPassage(animate: true);
        SearchBox.Clear();
        SearchList.Visibility = Visibility.Collapsed;
        SearchPopup.IsOpen = false;
        ShowStatus(L10n.T($"Jumped to {hit.S}:{hit.A}", $"انتقل إلى {hit.S}:{hit.A}"));
    }

    // ---------- favorites ----------

    bool IsFavorite(Passage p)
    {
        if (p.Verses.Count == 0) return false;
        var s = SettingsService.Current.Favorites;
        return s.Any(f => f.S == p.Verses[0].SurahNumber && f.A == p.Verses[0].NumberInSurah);
    }

    void SyncFavoriteStar()
    {
        bool fav = IsFavorite(_passage);
        FavoriteButton.Content = fav ? "\uE735" : "\uE734";
        FavoriteButton.ToolTip = fav ? L10n.T("Remove from favorites", "إزالة من المفضلة")
                                     : L10n.T("Favorites — tap to save this verse", "المفضلة — اضغط لحفظ هذه الآية");
    }

    void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_passage.Verses.Count == 0) return;
        var s = SettingsService.Current;
        var first = _passage.Verses[0];
        var existing = s.Favorites.FirstOrDefault(f => f.S == first.SurahNumber && f.A == first.NumberInSurah);
        if (existing != null)
        {
            s.Favorites.Remove(existing);
            ShowStatus(L10n.T("Removed from favorites", "أُزيلت من المفضلة"));
        }
        else
        {
            s.Favorites.Add(new Favorite
            {
                S = first.SurahNumber,
                A = first.NumberInSurah,
                B = _passage.Verses[^1].NumberInSurah,
            });
            ShowStatus(L10n.T("Saved to favorites", "حُفظت في المفضلة"));
        }
        SettingsService.Save();
        SyncFavoriteStar();
        RefreshFavorites();
    }

    void RefreshFavorites()
    {
        FavoritesList.Items.Clear();
        var favs = SettingsService.Current.Favorites;
        FavoritesEmpty.Visibility = favs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var f in favs)
        {
            var surah = Quran.Surah(f.S);
            if (surah == null) continue;
            string snippet = Translations.EnglishOf(Quran.GetPassage(f.S, f.A, f.A));
            if (snippet.Length > 60) snippet = snippet[..60] + "…";
            FavoritesList.Items.Add($"{surah.EnglishName} — {L10n.AyahsLabel(f.A, f.B)}  ·  {snippet}");
        }
    }

    void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        int i = FavoritesList.SelectedIndex;
        FavoritesList.SelectedIndex = -1;
        if (i < 0 || i >= SettingsService.Current.Favorites.Count) return;
        var f = SettingsService.Current.Favorites[i];
        _passage = Quran.GetPassage(f.S, f.A, f.B);
        ApplyPassage(animate: true);
        FavoritesPopup.IsOpen = false;
    }

    void FavoritesPopup_Closed(object? sender, EventArgs e) { }

    // ---------- prayer times ----------

    DispatcherTimer? _prayerTimer;

    void Prayer_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = PrayerCheck.IsChecked == true;
        if (on == SettingsService.Current.PrayerEnabled) return;
        SettingsService.Current.PrayerEnabled = on;
        SettingsService.Save();
        if (on)
        {
            _ = EnablePrayerTimesAsync();
        }
        else
        {
            _prayerTimer?.Stop();
            PrayerText.Visibility = Visibility.Collapsed;
        }
    }

    async Task EnablePrayerTimesAsync()
    {
        var s = SettingsService.Current;
        if (double.IsNaN(s.Latitude) || double.IsNaN(s.Longitude))
        {
            // visible feedback right away, so it is clear the option is working
            PrayerText.Text = "Finding your location…";
            PrayerText.Visibility = Visibility.Visible;
            ShowStatus(L10n.T("Finding your location for prayer times…", "جارٍ تحديد موقعك لأوقات الصلاة…"));
            var loc = await PrayerTimes.LocateAsync();
            if (loc == null)
            {
                // keep the option on and let the timer retry quietly; the location
                // service is occasionally unavailable
                ShowStatus(L10n.T("Could not find your location yet — will keep trying", "لم نحدد موقعك بعد — سنحاول مجدداً"));
            }
            else
            {
                s.Latitude = loc.Lat;
                s.Longitude = loc.Lon;
                s.CityName = loc.City;
                SettingsService.Save();
                ShowStatus(L10n.T($"Prayer times for {(string.IsNullOrWhiteSpace(loc.City) ? "your area" : loc.City)}", $"أوقات الصلاة لـ {(string.IsNullOrWhiteSpace(loc.City) ? "منطقتك" : loc.City)}"));
            }
        }
        _prayerTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _prayerTimer.Tick -= UpdatePrayerText;
        _prayerTimer.Tick += UpdatePrayerText;
        _prayerTimer.Start();
        UpdatePrayerText(null, null!);
    }

    void UpdatePrayerText(object? sender, EventArgs e)
    {
        var s = SettingsService.Current;
        if (!s.PrayerEnabled) return;
        if (double.IsNaN(s.Latitude))
        {
            // still no location — retry quietly in the background
            _ = RetryLocateAsync();
            return;
        }
        var next = PrayerTimes.Next(s.Latitude, s.Longitude);
        if (next == null) return;
        string city = string.IsNullOrWhiteSpace(s.CityName) ? "" : $"  ·  {s.CityName}";
        PrayerText.Text = L10n.T(
            $"Next prayer: {L10n.Prayer(next.Value.Name)} at {next.Value.Time}{city}",
            $"الصلاة القادمة: {L10n.Prayer(next.Value.Name)} {next.Value.Time}{city}");
        PrayerText.Visibility = Visibility.Visible;
    }

    async Task RetryLocateAsync()
    {
        var s = SettingsService.Current;
        var loc = await PrayerTimes.LocateAsync();
        if (loc != null && s.PrayerEnabled)
        {
            s.Latitude = loc.Lat;
            s.Longitude = loc.Lon;
            s.CityName = loc.City;
            SettingsService.Save();
        }
    }

    // ---------- sleep timer ----------

    bool _pickingSleep;

    void SleepToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (SleepToggle.IsChecked == true)
        {
            _pickingSleep = true;
            SleepList.ItemsSource = SleepOptions.Select(SleepLabel).ToList();
            SleepList.SelectedIndex = Array.IndexOf(SleepOptions, SettingsService.Current.SleepMinutes) is >= 0 and int ix ? ix : 0;
            SleepList.ScrollIntoView(SleepList.SelectedItem);
            _pickingSleep = false;
            SleepPopup.IsOpen = true;
        }
        else
        {
            SleepPopup.IsOpen = false;
        }
    }

    void SleepPopup_Closed(object? sender, EventArgs e) => SleepToggle.IsChecked = false;

    void SleepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _pickingSleep) return;
        int i = SleepList.SelectedIndex;
        if (i < 0 || i >= SleepOptions.Length) return;
        int minutes = SleepOptions[i];
        if (minutes != SettingsService.Current.SleepMinutes)
        {
            SettingsService.Current.SleepMinutes = minutes;
            SleepToggle.Content = SleepLabel(minutes);
            Audio.SetSleepTimer(minutes);
            SyncSleepCountdown();
            SettingsService.Save();
            ShowStatus(minutes > 0 ? L10n.T($"Sleep timer: recitation stops after {minutes} min", $"مؤقت النوم: تتوقف التلاوة بعد {minutes} دقيقة") : L10n.T("Sleep timer off", "مؤقت النوم متوقف"));
        }
        SleepPopup.IsOpen = false;
    }

    void ClearResume_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.ResumeS = -1;
        SettingsService.Current.ResumeA = -1;
        SettingsService.Save();
        UpdateResumeText();
        ShowStatus(L10n.T("Reading position cleared", "مُسح موضع القراءة"));
    }

    // Shows where continuous reading would resume, so the saved position is
    // never an invisible surprise.
    void UpdateResumeText()
    {
        var s = SettingsService.Current;
        if (ResumeText == null) return;
        if (s.ResumeS > 0 && s.ResumeA > 0)
        {
            string where = L10n.SurahRef(s.ResumeS, s.ResumeA);
            ResumeText.Text = s.ContinueReading
                ? L10n.T($"Recitation will resume from {where} the next time you press play.",
                         $"سيتم استئناف التلاوة من {where} عند التشغيل التالي.")
                : L10n.T($"Position {where} saved — turn on \u201CKeep reading\u201D above to resume from it.",
                         $"حُفظ الموضع {where} — فعّل \u201Cمتابعة القراءة\u201D أعلاه للاستئناف منه.");
        }
        else
        {
            ResumeText.Text = L10n.T(
                "Play recitation and your position is saved automatically.",
                "شغّل التلاوة ويُحفظ موضعك تلقائياً.");
        }
    }


    void Topmost_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = TopmostCheck.IsChecked == true;
        if (on == SettingsService.Current.AlwaysOnTop) return;
        SetAlwaysOnTop(on);
    }

    void Pin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = PinCheck.IsChecked == true;
        if (on == SettingsService.Current.PinToDesktop) return;
        SetPin(on);
    }

    void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool on = StartupCheck.IsChecked == true;
        if (on == Autostart.IsEnabled()) return;
        Autostart.Set(on);
        SettingsService.Current.LaunchAtStartup = on;
        SettingsService.Save();
        ShowStatus(on ? L10n.T("Quran Widget will start with Windows", "سيبدأ Quran Widget مع Windows") : L10n.T("Autostart disabled", "أُلغي البدء التلقائي"));
    }

    void SetAlwaysOnTop(bool on)
    {
        if (on && SettingsService.Current.PinToDesktop) SetPin(false);
        SettingsService.Current.AlwaysOnTop = on;
        Topmost = on;
        SettingsService.Save();
        SyncSettingsChecks();
        SyncMenuChecks();
    }

    void SetPin(bool on)
    {
        if (on)
        {
            if (SettingsService.Current.AlwaysOnTop)
            {
                SettingsService.Current.AlwaysOnTop = false;
                Topmost = false;
            }
            SettingsService.Current.PinToDesktop = true;
            ApplyPin();
        }
        else
        {
            StopBottomLoop();
            WindowEffects.UnpinFromDesktop(Handle);
            SettingsService.Current.PinToDesktop = false;
            WindowEffects.EnableAcrylic(this);
            ShowStatus(L10n.T("Unpinned from the desktop", "أُلغي التثبيت من سطح المكتب"));
        }
        SettingsService.Save();
        SyncSettingsChecks();
        SyncMenuChecks();
    }

    // ---------- header buttons ----------

    void Hide_Click(object sender, RoutedEventArgs e)
    {
        SaveWindowState();
        Hide();
    }

    // ---------- context menu ----------

    void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        MenuDaily.Header = L10n.T("Verse of the Day", "آية اليوم");
        MenuNew.Header = L10n.T("New Verse Widget", "أداة آية جديدة");
        MenuFavorites.Header = L10n.T("Favorites", "المفضلة");
        MenuSaveImage.Header = L10n.T("Save verse as image", "حفظ الآية كصورة");
        MenuTopmost.Header = L10n.T("Always on top", "دائماً في المقدمة");
        MenuPin.Header = L10n.T("Pin to desktop", "تثبيت على سطح المكتب");
        MenuStartup.Header = L10n.T("Start with Windows", "البدء مع Windows");
        MenuExit.Header = L10n.T("Exit", "خروج");
        SyncMenuChecks();
        SyncFavoriteStar();
    }

    void SyncMenuChecks()
    {
        MenuTopmost.IsChecked = SettingsService.Current.AlwaysOnTop;
        MenuPin.IsChecked = SettingsService.Current.PinToDesktop;
        MenuStartup.IsChecked = Autostart.IsEnabled();
    }

    void MenuDaily_Click(object sender, RoutedEventArgs e) => App.Instance.ShowDaily();
    void MenuNew_Click(object sender, RoutedEventArgs e) => App.Instance.CreateWidget();
    void MenuTopmost_Click(object sender, RoutedEventArgs e) => SetAlwaysOnTop(!SettingsService.Current.AlwaysOnTop);
    void MenuPin_Click(object sender, RoutedEventArgs e) => SetPin(!SettingsService.Current.PinToDesktop);

    void MenuFavorites_Click(object sender, RoutedEventArgs e)
    {
        RefreshFavorites();
        FavoritesPopup.IsOpen = true;
    }

    void MenuSaveImage_Click(object sender, RoutedEventArgs e) => SaveVerseImage();

    void SaveVerseImage()
    {
        try
        {
            var first = _passage.Verses[0];
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG image|*.png",
                FileName = $"Quran {first.Surah.EnglishName} {_passage.Reference.Replace(':', '-')}.png",
            };
            if (dlg.ShowDialog(this) != true) return;

            Card.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Card.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), Card.DesiredSize));
            double scale = 2.0;
            var rtb = new RenderTargetBitmap(
                (int)(Card.ActualWidth * scale), (int)(Card.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(Card);
            Card.InvalidateMeasure();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = File.Create(dlg.FileName);
            encoder.Save(stream);
            ShowStatus(L10n.T("Verse image saved", "حُفظت صورة الآية"));
        }
        catch
        {
            ShowStatus(L10n.T("Could not save the image", "تعذر حفظ الصورة"));
        }
    }

    void MenuStartup_Click(object sender, RoutedEventArgs e)
    {
        bool on = !Autostart.IsEnabled();
        Autostart.Set(on);
        SettingsService.Current.LaunchAtStartup = on;
        SettingsService.Save();
        SyncMenuChecks();
    }
    void MenuExit_Click(object sender, RoutedEventArgs e) => App.Instance.ExitApp();

    // ---------- drag / position ----------

    void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(src) != null || FindAncestor<Thumb>(src) != null
            || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(src) != null)
            return;
        if (e.ClickCount == 1)
        {
            try
            {
                DragMove();
                SaveWindowState();
            }
            catch
            {
                // DragMove throws if the mouse was released; harmless
            }
        }
    }

    static T? FindAncestor<T>(DependencyObject? d) where T : class
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : System.Windows.LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Pinning at startup is deferred to Loaded (the window must be shown before
        // it can safely be re-parented); acrylic is decided by the pin state.
        if (!SettingsService.Current.PinToDesktop)
            WindowEffects.EnableAcrylic(this);
    }

    bool _startupPinDone;

    void OnLoadedStartupPin()
    {
        if (_startupPinDone) return;
        _startupPinDone = true;
        if (SettingsService.Current.PinToDesktop)
            ApplyPin();
    }

    void ApplyPin()
    {
        if (!WindowEffects.PinToDesktop(Handle))
        {
            SettingsService.Current.PinToDesktop = false;
            SettingsService.Save();
            WindowEffects.EnableAcrylic(this);
            SyncSettingsChecks();
            SyncMenuChecks();
            ShowStatus(L10n.T("Could not pin — right-click and try again", "تعذر التثبيت — انقر بزر الفأرة الأيمن وحاول مجدداً"));
            return;
        }
        WindowEffects.DisableAcrylic(this);
        ShowStatus(L10n.T("Pinned — the widget sits behind all windows", "تم التثبيت — الأداة خلف جميع النوافذ"));
        StartBottomLoop();
    }

    // While pinned, keep the widget at the very bottom of the desktop (behind every
    // other window, above the wallpaper) whenever something covers it.
    DispatcherTimer? _bottomTimer;

    void StartBottomLoop()
    {
        _bottomTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _bottomTimer.Tick -= BottomLoop_Tick;
        _bottomTimer.Tick += BottomLoop_Tick;
        _bottomTimer.Start();
    }

    void StopBottomLoop()
    {
        _bottomTimer?.Stop();
    }

    void BottomLoop_Tick(object? sender, EventArgs e)
    {
        if (!SettingsService.Current.PinToDesktop) { StopBottomLoop(); return; }
        // don't sink while the user is interacting with the widget
        if (Native.GetForegroundWindow() == Handle) return;
        WindowEffects.SendToBottom(Handle);
    }

    void RestorePosition()
    {
        var st = GetState();
        var (wa, _) = WorkAreaForScreen(st.Screen);
        if (!double.IsNaN(st.X) && !double.IsNaN(st.Y))
        {
            Left = Math.Clamp(st.X, wa.Left - 60, wa.Right - 120);
            Top = Math.Clamp(st.Y, wa.Top, wa.Bottom - 80);
        }
        else
        {
            Left = wa.Right - Width - 46;
            Top = wa.Bottom - 500;
        }
    }

    public void ClampToScreen()
    {
        var st = GetState();
        var (wa, _) = WorkAreaForScreen(st.Screen);
        Left = Math.Clamp(Left, wa.Left - 60, wa.Right - 120);
        Top = Math.Clamp(Top, wa.Top, wa.Bottom - 80);
    }

    // Returns the WPF (DIP) work area of the screen the widget was last on.
    static (Rect Wa, string Device) WorkAreaForScreen(string? deviceName)
    {
        double scale = UiScale();
        foreach (var sc in WinForms.Screen.AllScreens)
        {
            if (sc.DeviceName == deviceName)
                return (DeviceToDips(sc.WorkingArea, scale), sc.DeviceName);
        }
        var primary = WinForms.Screen.PrimaryScreen!;
        return (DeviceToDips(primary.WorkingArea, scale), primary.DeviceName);
    }

    static double UiScale()
    {
        try
        {
            var waDip = SystemParameters.WorkArea;
            var waPx = WinForms.Screen.PrimaryScreen!.WorkingArea;
            return waPx.Height / waDip.Height;
        }
        catch
        {
            return 1.0;
        }
    }

    static Rect DeviceToDips(System.Drawing.Rectangle r, double scale) =>
        scale <= 0 ? new Rect(r.Left, r.Top, r.Width, r.Height)
                   : new Rect(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);

    static string CurrentScreenDevice(Window w)
    {
        try
        {
            double cx = w.Left + w.ActualWidth / 2;
            double cy = w.Top + w.ActualHeight / 2;
            double scale = UiScale();
            foreach (var sc in WinForms.Screen.AllScreens)
            {
                var r = DeviceToDips(sc.Bounds, scale);
                if (cx >= r.Left && cx <= r.Right && cy >= r.Top && cy <= r.Bottom)
                    return sc.DeviceName;
            }
        }
        catch
        {
            // best effort
        }
        return "";
    }

    void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        _posSave ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _posSave.Stop();
        _posSave.Tick -= PosSave_Tick;
        _posSave.Tick += PosSave_Tick;
        _posSave.Start();
    }

    void PosSave_Tick(object? sender, EventArgs e)
    {
        _posSave?.Stop();
        SaveWindowState();
    }

    WidgetState GetState()
    {
        var w = SettingsService.Current.Windows;
        if (!w.TryGetValue(Id, out var st))
        {
            st = new WidgetState();
            w[Id] = st;
        }
        return st;
    }

    void SaveWindowState()
    {
        try
        {
            var st = GetState();
            st.X = Left;
            st.Y = Top;
            if (_passage.Verses.Count > 0)
            {
                st.S = _passage.Verses[0].SurahNumber;
                st.A = _passage.Verses[0].NumberInSurah;
            }
            if (IsDaily) st.Day = DateTime.Today.ToString("yyyy-MM-dd");
            st.Screen = CurrentScreenDevice(this);
            SettingsService.Save();
        }
        catch
        {
            // best effort
        }
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        Audio.StopAll();
        Audio.Status -= OnAudioStatus;
        try
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkChanged;
            NetworkChange.NetworkAddressChanged -= NetworkChanged;
        }
        catch
        {
            // best effort
        }
        if (App.Exiting)
        {
            SaveWindowState();
            return;
        }
        if (IsDaily)
        {
            // keep the daily widget alive in the tray
            e.Cancel = true;
            SaveWindowState();
            Hide();
        }
        else
        {
            SaveWindowState();
            App.Instance.WidgetClosed(this);
        }
    }
}

