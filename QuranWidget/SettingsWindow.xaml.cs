using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace QuranWidget;

// Dedicated settings window: the widget no longer hosts the settings overlay,
// so changing preferences never covers or disturbs the verse. Preferences apply
// live to every widget and are saved as they change.
public partial class SettingsWindow : Window
{
    bool _loading = true;
    DispatcherTimer? _saveDebounce;

    public SettingsWindow()
    {
        InitializeComponent();
        ApplyTexts();
        LoadValues();
        _loading = false;
        Activated += (_, _) => UpdateResumeText();
        Closing += (_, _) => SettingsService.Save();

        // The widget survives Win+D ("show desktop"); settings must stay available
        // the same way, so restore immediately if something ever minimizes it.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        };
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    // CaptionHeight is 0; the header background drags the window manually.
    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { /* released mid-drag */ }
    }

    // ---------- navigation ----------

    void SelectPage(FrameworkElement page)
    {
        PageRecite.Visibility = page == PageRecite ? Visibility.Visible : Visibility.Collapsed;
        PageLook.Visibility = page == PageLook ? Visibility.Visible : Visibility.Collapsed;
        PageDownload.Visibility = page == PageDownload ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = page == PageGeneral ? Visibility.Visible : Visibility.Collapsed;

        // small slide-in so switching pages feels alive but stays quick
        PageTt.X = 12;
        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        PageTt.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    void NavRecite_Checked(object sender, RoutedEventArgs e) { if (!_loading) SelectPage(PageRecite); }
    void NavLook_Checked(object sender, RoutedEventArgs e) { if (!_loading) SelectPage(PageLook); }
    void NavDownload_Checked(object sender, RoutedEventArgs e) { if (!_loading) SelectPage(PageDownload); }
    void NavGeneral_Checked(object sender, RoutedEventArgs e) { if (!_loading) SelectPage(PageGeneral); }

    // ---------- texts ----------

    void ApplyTexts()
    {
        FlowDirection = L10n.ArabicUI ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        bool ar = L10n.ArabicUI;

        TitleText.Text = L10n.T("Settings", "الإعدادات");
        NavRecite.Content = L10n.T("Recitation", "التلاوة");
        NavLook.Content = L10n.T("Appearance", "المظهر");
        NavDownload.Content = L10n.T("Downloads", "التنزيلات");
        NavGeneral.Content = L10n.T("General", "عام");

        ReciterHeader.Text = L10n.T("RECITER", "القارئ");
        ReciterTitle.Text = L10n.T("Reciter", "القارئ");
        ReciterSub.Text = L10n.T("Who recites the verses", "من يُمَلّي الآيات");
        ReadingHeader.Text = L10n.T("CONTINUOUS READING", "متابعة القراءة");
        ContinueTitle.Text = L10n.T("Keep reading", "متابعة القراءة");
        ContinueSub.Text = L10n.T("Continue automatically to the next verses", "الانتقال تلقائياً إلى الآيات التالية");
        StopAtTitle.Text = L10n.T("Stop after", "التوقف بعد");
        SleepHeader.Text = L10n.T("SLEEP TIMER", "مؤقت النوم");
        SleepTitle.Text = L10n.T("Sleep timer", "مؤقت النوم");
        SleepSub.Text = L10n.T("Stop recitation automatically", "إيقاف التلاوة تلقائياً");

        LookHeader.Text = L10n.T("WIDGET", "الويدجت");
        ThemeTitle.Text = L10n.T("Theme", "المظهر");
        ThemeSub.Text = L10n.T("Colors of the widget and windows", "ألوان الويدجت والنوافذ");
        OpacityTitle.Text = L10n.T("Opacity", "الشفافية");
        FontSizeTitle.Text = L10n.T("Arabic font size", "حجم خط المصحف");
        TransHeader.Text = L10n.T("TRANSLATION & LANGUAGE", "الترجمة واللغة");
        ShowTransTitle.Text = L10n.T("Show translation", "إظهار الترجمة");
        ShowTransSub.Text = L10n.T("Show the meaning under the verse", "إظهار المعنى تحت الآية");
        TranslationTitle.Text = L10n.T("Translation", "الترجمة");
        TranslationSub.Text = L10n.T("Others download on demand", "تُنزَّل الترجمات الأخرى عند الطلب");
        LangTitle.Text = L10n.T("Language", "اللغة");
        LangSub.Text = L10n.T("Interface language", "لغة الواجهة");
        LangButton.Content = L10n.T("English / العربية", "العربية / English");

        DownloadsHint.Text = L10n.T(
            "Save recitation audio to your device for the reciter and content chosen below, so it plays without internet. Translations are downloaded automatically when you pick them.",
            "احفظ صوت التلاوة ليعمل بدون إنترنت للقارئ والمحتوى المختارين أدناه. تُنزَّل الترجمات تلقائياً عند اختيارها.");
        DlReciterCaption.Text = L10n.T("Reciter", "القارئ");
        DlScopeCaption.Text = L10n.T("Content", "المحتوى");
        DownloadStartButton.Content = L10n.T("Download", "تنزيل");
        DownloadStartButton.IsEnabled = !Audio.Downloading;
        DownloadStartButton.ToolTip = L10n.T("Save audio for offline listening", "حفظ الصوت للاستماع دون اتصال");

        WindowHeader.Text = L10n.T("WIDGET WINDOW", "نافذة الويدجت");
        TopmostTitle.Text = L10n.T("Always on top", "دائماً في المقدمة");
        TopmostSub.Text = L10n.T("Keep the widget above other windows", "إبقاء الويدجت فوق النوافذ الأخرى");
        PinTitle.Text = L10n.T("Pin to the desktop background", "تثبيت في خلفية سطح المكتب");
        PinSub.Text = L10n.T("The widget sits behind every window", "يبقى الويدجت خلف جميع النوافذ");
        StartupTitle.Text = L10n.T("Start with Windows", "البدء مع Windows");
        StartupSub.Text = L10n.T("Launch Mishkat when you sign in", "تشغيل مشكاة عند تسجيل الدخول");
        UpdatesHeader.Text = L10n.T("UPDATES", "التحديثات");
        UpdatesTitle.Text = L10n.T("Updates", "التحديثات");
        UpdatesSub.Text = L10n.T("Check for a newer version", "التحقق من وجود إصدار أحدث");
        CheckUpdatesButton.Content = L10n.T("Check for updates now", "تحقق من التحديثات الآن");
        ClearResumeButton.Content = L10n.T("Clear saved position", "مسح الموضع المحفوظ");

        CreditsText.Text = L10n.T(
            "Recitation: cdn.islamic.network\nTranslations: api.alquran.cloud",
            "التلاوة: cdn.islamic.network\nالترجمات: api.alquran.cloud");
        MadeWithLoveText.Text = L10n.T(
            "Built with ❤ by MohammedTsmu",
            "صُنع بـ ❤ بواسطة MohammedTsmu");

        // picker values follow the language too
        ReciterToggle.Content = Audio.ReciterName(_reciterIndex);
        StopAtToggle.Content = StopAtLabel(SettingsService.Current.StopAt);
        SleepToggle.Content = SleepLabel(SettingsService.Current.SleepMinutes);
        ThemeToggle.Content = ThemeService.NameOf(SettingsService.Current.Theme);
        TranslationToggle.Content = TranslationName(SettingsService.Current.TranslationId);
        if (DlReciterToggle.IsChecked != true) DlReciterToggle.Content = DlReciterName;
        if (DlScopeToggle.IsChecked != true) DlScopeToggle.Content = DlScopeLabel(_dlScope);
    }

    // ---------- load current values into the controls ----------

    int _reciterIndex;

    void LoadValues()
    {
        var s = SettingsService.Current;

        _reciterIndex = Array.FindIndex(Audio.Reciters, r => r.Id == s.ReciterId);
        if (_reciterIndex < 0) _reciterIndex = 0;

        ContinueSwitch.IsChecked = s.ContinueReading;
        ShowTranslationSwitch.IsChecked = s.ShowTranslation;
        TopmostSwitch.IsChecked = s.AlwaysOnTop;
        PinSwitch.IsChecked = s.PinToDesktop;
        StartupSwitch.IsChecked = Autostart.IsEnabled();

        OpacitySlider.Value = s.Opacity;
        FontSizeSlider.Value = Math.Clamp(s.ArabicFontSize, 18, 40);
        SyncOpacityValue();
        SyncFontSizeValue();

        UpdateResumeText();
        UpdateDownloadsUi();
    }

    // ---------- shared save helper ----------

    void SaveNow() => SettingsService.Save();

    // Sliders fire ValueChanged for every pixel; apply the change live but
    // coalesce the file writes into one save per burst.
    void DebouncedSave()
    {
        if (_loading) return;
        _saveDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Stop();
        _saveDebounce.Tick += SaveDebounce_Tick;
        _saveDebounce.Start();
    }

    void SaveDebounce_Tick(object? sender, EventArgs e)
    {
        _saveDebounce?.Stop();
        SaveNow();
    }

    // Push the current settings into every open widget so changes apply live.
    void RefreshWidgets() => App.Instance.RefreshWidgets();

    // ---------- reciter ----------

    bool _pickingReciter;

    void ReciterToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (ReciterToggle.IsChecked == true)
        {
            _pickingReciter = true;
            ReciterList.ItemsSource = Enumerable.Range(0, Audio.Reciters.Length).Select(Audio.ReciterName).ToList();
            ReciterList.SelectedIndex = _reciterIndex;
            ReciterList.ScrollIntoView(ReciterList.SelectedItem);
            _pickingReciter = false;
            ReciterPopup.IsOpen = true;
        }
        else ReciterPopup.IsOpen = false;
    }

    void ReciterPopup_Closed(object? sender, EventArgs e) => ReciterToggle.IsChecked = false;

    void ReciterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingReciter) return;
        int i = ReciterList.SelectedIndex;
        if (i < 0 || i >= Audio.Reciters.Length) return;
        if (i != _reciterIndex)
        {
            if (Audio.PlayingRecitation) Audio.StopAll();
            _reciterIndex = i;
            SettingsService.Current.ReciterId = Audio.Reciters[i].Id;
            ReciterToggle.Content = Audio.ReciterName(i);
            SaveNow();
        }
        ReciterPopup.IsOpen = false;
    }

    // ---------- continuous reading ----------

    void Continue_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ContinueSwitch.IsChecked == true;
        if (on == SettingsService.Current.ContinueReading) return;
        SettingsService.Current.ContinueReading = on;
        SaveNow();
        RefreshWidgets();
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

    bool _pickingStopAt;

    void StopAtToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (StopAtToggle.IsChecked == true)
        {
            _pickingStopAt = true;
            StopAtList.ItemsSource = StopAtLabels.ToList();
            StopAtList.SelectedIndex = Math.Clamp(SettingsService.Current.StopAt, 0, StopAtLabels.Length - 1);
            StopAtList.ScrollIntoView(StopAtList.SelectedItem);
            _pickingStopAt = false;
            StopAtPopup.IsOpen = true;
        }
        else StopAtPopup.IsOpen = false;
    }

    void StopAtPopup_Closed(object? sender, EventArgs e) => StopAtToggle.IsChecked = false;

    void StopAtList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingStopAt) return;
        int i = StopAtList.SelectedIndex;
        if (i < 0) return;
        if (i != SettingsService.Current.StopAt)
        {
            SettingsService.Current.StopAt = i;
            StopAtToggle.Content = StopAtLabel(i);
            SaveNow();
            RefreshWidgets();
        }
        StopAtPopup.IsOpen = false;
    }

    void UpdateResumeText()
    {
        var s = SettingsService.Current;
        if (s.ResumeS > 0 && s.ResumeA > 0)
        {
            string where = L10n.SurahRef(s.ResumeS, s.ResumeA);
            ResumeText.Text = s.ContinueReading
                ? L10n.T($"Resumes from {where}", $"يُستأنف من {where}")
                : L10n.T($"Position {where} saved", $"حُفظ الموضع {where}");
        }
        else
        {
            ResumeText.Text = L10n.T("No saved position yet", "لا يوجد موضع محفوظ بعد");
        }
    }

    void ClearResume_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.ResumeS = -1;
        SettingsService.Current.ResumeA = -1;
        SaveNow();
        UpdateResumeText();
    }

    // ---------- sleep timer ----------

    static readonly int[] SleepOptions = { 0, 10, 20, 30, 60 };

    static string SleepLabel(int minutes) =>
        minutes <= 0 ? L10n.T("Off — play until I stop", "بدون توقيت — حتى أوقفه")
                     : L10n.T($"Stop after {minutes} minutes", $"بعد {minutes} دقيقة");

    bool _pickingSleep;

    void SleepToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (SleepToggle.IsChecked == true)
        {
            _pickingSleep = true;
            SleepList.ItemsSource = SleepOptions.Select(SleepLabel).ToList();
            SleepList.SelectedIndex = Array.IndexOf(SleepOptions, SettingsService.Current.SleepMinutes) is >= 0 and int ix ? ix : 0;
            SleepList.ScrollIntoView(SleepList.SelectedItem);
            _pickingSleep = false;
            SleepPopup.IsOpen = true;
        }
        else SleepPopup.IsOpen = false;
    }

    void SleepPopup_Closed(object? sender, EventArgs e) => SleepToggle.IsChecked = false;

    void SleepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingSleep) return;
        int i = SleepList.SelectedIndex;
        if (i < 0 || i >= SleepOptions.Length) return;
        int minutes = SleepOptions[i];
        if (minutes != SettingsService.Current.SleepMinutes)
        {
            SettingsService.Current.SleepMinutes = minutes;
            SleepToggle.Content = SleepLabel(minutes);
            Audio.SetSleepTimer(minutes);
            SaveNow();
            RefreshWidgets();
        }
        SleepPopup.IsOpen = false;
    }

    // ---------- appearance ----------

    void SyncOpacityValue() =>
        OpacityValue.Text = L10n.T($"{Math.Round(OpacitySlider.Value * 100)}% opaque", $"شفافية {Math.Round(OpacitySlider.Value * 100)}٪");

    void SyncFontSizeValue() =>
        FontSizeValue.Text = L10n.T($"{(int)FontSizeSlider.Value} pt", $"{(int)FontSizeSlider.Value} نقطة");

    void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        SyncOpacityValue();
        SettingsService.Current.Opacity = OpacitySlider.Value;
        RefreshWidgets();
        DebouncedSave();
    }

    void FontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        SyncFontSizeValue();
        SettingsService.Current.ArabicFontSize = FontSizeSlider.Value;
        RefreshWidgets();
        DebouncedSave();
    }

    // ---------- theme ----------

    bool _pickingTheme;

    void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
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
        else ThemePopup.IsOpen = false;
    }

    void ThemePopup_Closed(object? sender, EventArgs e) => ThemeToggle.IsChecked = false;

    void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingTheme) return;
        int i = ThemeList.SelectedIndex;
        if (i < 0 || i >= ThemeService.Themes.Length) return;
        var id = ThemeService.Themes[i].Id;
        if (id != SettingsService.Current.Theme)
        {
            SettingsService.Current.Theme = id;
            ThemeToggle.Content = ThemeService.NameOf(id);
            ThemeService.Apply(id);
            foreach (var w in App.Instance.Widgets.ToList())
            {
                try
                {
                    if (!w.IsPinned) WindowEffects.EnableAcrylic(w);
                }
                catch { /* best effort */ }
            }
            SaveNow();
        }
        ThemePopup.IsOpen = false;
    }

    // ---------- translation ----------

    static string TranslationName(string id) =>
        Translations.DisplayName(Array.FindIndex(Translations.Available, t => t.Id == id));

    bool _pickingTranslation;

    void ShowTranslation_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ShowTranslationSwitch.IsChecked == true;
        if (on == SettingsService.Current.ShowTranslation) return;
        SettingsService.Current.ShowTranslation = on;
        SaveNow();
        RefreshWidgets();
    }

    void TranslationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
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
        else TranslationPopup.IsOpen = false;
    }

    void TranslationPopup_Closed(object? sender, EventArgs e) => TranslationToggle.IsChecked = false;

    async void TranslationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingTranslation) return;
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
                DownloadsStatus.Text = L10n.T("Needs internet to download this translation", "يلزم إنترنت لتنزيل هذه الترجمة");
                return;
            }
            DownloadsStatus.Text = L10n.T($"Downloading {name}…", $"جارٍ تنزيل {name}…");
            SelectPage(PageDownload);
            NavDownload.IsChecked = true;
            bool ok = await Translations.DownloadAsync(id);
            if (!ok)
            {
                DownloadsStatus.Text = L10n.T("Could not download the translation", "تعذر تنزيل الترجمة");
                return;
            }
        }
        SettingsService.Current.TranslationId = id;
        TranslationToggle.Content = name;
        SaveNow();
        Translations.Load(id);
        DownloadsStatus.Text = "";
        RefreshWidgets();
    }

    // ---------- language ----------

    void Language_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Current.Language = L10n.ArabicUI ? "en" : "ar";
        SaveNow();
        ApplyTexts();
        App.Instance.ApplyTrayLanguage();
        RefreshWidgets();
    }

    // ---------- downloads (offline audio) ----------

    bool HasInternet() => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

    int _dlScope;          // 0 = current surah, 1 = a chosen surah, 2 = whole Quran
    int _dlSurah = 1;      // surah chosen for scope 1
    bool _pickingDl;
    bool _dlScopePhase;    // false = choose kind, true = choose a surah

    string DlReciterName => Audio.ReciterName(_reciterIndex);

    string DlScopeLabel(int scope)
    {
        var cur = App.Instance.CurrentVerseSurah();
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
        if (DlReciterToggle.IsChecked != true) DlReciterToggle.Content = DlReciterName;
        if (DlScopeToggle.IsChecked != true) DlScopeToggle.Content = DlScopeLabel(_dlScope);
        DownloadStartButton.IsEnabled = !Audio.Downloading;
    }

    bool _pickingDlReciter;

    void DlReciterToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (DlReciterToggle.IsChecked == true)
        {
            _pickingDlReciter = true;
            DlReciterList.ItemsSource = Enumerable.Range(0, Audio.Reciters.Length).Select(Audio.ReciterName).ToList();
            DlReciterList.SelectedIndex = _reciterIndex;
            DlReciterList.ScrollIntoView(DlReciterList.SelectedItem);
            _pickingDlReciter = false;
            DlReciterPopup.IsOpen = true;
        }
        else DlReciterPopup.IsOpen = false;
    }

    void DlReciterPopup_Closed(object? sender, EventArgs e) => DlReciterToggle.IsChecked = false;

    void DlReciterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingDlReciter) return;
        if (DlReciterList.SelectedIndex >= 0) _reciterIndex = DlReciterList.SelectedIndex;
        DlReciterPopup.IsOpen = false;
        UpdateDownloadsUi();
    }

    void DlScopeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
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

    void DlScopeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingDl) return;
        int i = DlScopeList.SelectedIndex;
        if (i < 0) return;

        if (!_dlScopePhase)
        {
            if (i == 1) { _dlScope = 2; DlScopePopup.IsOpen = false; UpdateDownloadsUi(); return; }
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
            DownloadsStatus.Text = L10n.T("No internet — downloads are unavailable", "لا يوجد إنترنت — التنزيل غير متاح");
            return;
        }

        IEnumerable<int> surahs;
        if (_dlScope == 2)
        {
            var r = System.Windows.MessageBox.Show(this,
                L10n.T($"Download the whole Quran recited by {DlReciterName} (~2.5 GB)?\nThis needs a while and a stable connection.",
                       $"تنزيل المصحف كاملاً بصوت {DlReciterName} (~2.5 جيجابايت)؟\nقد يستغرق وقتاً طويلاً ويتطلب اتصالاً مستقراً."),
                L10n.T("Mishkat", "مشكاة"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            surahs = Enumerable.Range(1, 114);
        }
        else
        {
            if (_dlScope == 0)
            {
                var cur = App.Instance.CurrentVerseSurah();
                if (cur == null) return;
                _dlSurah = cur.Number;
                UpdateDownloadsUi();
            }
            surahs = new[] { _dlSurah };
        }

        string reciterId = Audio.Reciters[_reciterIndex].Id;

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

    // ---------- general: window switches ----------

    void Topmost_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = TopmostSwitch.IsChecked == true;
        if (on == SettingsService.Current.AlwaysOnTop) return;
        SettingsService.Current.AlwaysOnTop = on;
        if (on && SettingsService.Current.PinToDesktop)
        {
            // pinning and always-on-top are mutually exclusive; pin wins nothing here
            SettingsService.Current.PinToDesktop = false;
            PinSwitch.IsChecked = false;
        }
        SaveNow();
        RefreshWidgets();
        foreach (var w in App.Instance.Widgets.ToList())
        {
            try
            {
                if (on) { w.UnpinIfPinned(); }
                w.SyncMenuChecks();
            }
            catch { /* best effort */ }
        }
    }

    void Pin_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = PinSwitch.IsChecked == true;
        if (on == SettingsService.Current.PinToDesktop) return;
        SettingsService.Current.PinToDesktop = on;
        if (on)
        {
            SettingsService.Current.AlwaysOnTop = false;
            TopmostSwitch.IsChecked = false;
        }
        SaveNow();
        foreach (var w in App.Instance.Widgets.ToList())
        {
            try
            {
                if (on) w.ApplyPinFromSettings();
                else w.UnpinIfPinned();
                w.SyncMenuChecks();
            }
            catch { /* best effort */ }
        }
        RefreshWidgets();
    }

    void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = StartupSwitch.IsChecked == true;
        if (on == Autostart.IsEnabled()) return;
        Autostart.Set(on);
        SettingsService.Current.LaunchAtStartup = on;
        SaveNow();
    }

    void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        DownloadsStatus.Text = "";
        App.Instance.CheckForUpdates();
    }
}
