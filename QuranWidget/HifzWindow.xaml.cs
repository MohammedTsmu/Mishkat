using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ListBox = System.Windows.Controls.ListBox;
using Button = System.Windows.Controls.Button;
using FlowDirection = System.Windows.FlowDirection;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using WinForms = System.Windows.Forms;

namespace QuranWidget;

public partial class HifzWindow : Window
{
    // ---------- pick state ----------
    int _surah = 1;
    int _from = 1, _to = 3;
    int _reps = 7;

    // ---------- session state ----------
    // The classic method, step by step: learn each verse (listen + recite hidden),
    // then link it to everything before it (الربط), then a full-group link, then
    // a test gate (الامتحان) before the group joins the review ladder.
    enum SessionMode { Idle, Learn, Link, LinkAll, Test }
    SessionMode _mode = SessionMode.Idle;
    List<Verse> _session = new();
    int _sessionIndex;
    int _loopsDone;
    bool _revealed;
    int _linkTo;                       // last verse index of the current link step
    List<int> _blanks = new();         // test blank mode: verse indices still hidden
    string? _sessionRecording;

    // ---------- review state ----------
    // Due work is organized as entries: a connected group (وِرد متصل) when several
    // adjacent due verses belong to one memorization group, otherwise a single
    // verse. New hifz (حديث) comes first, capped old hifz (قديم) after it.
    sealed class DueEntry
    {
        public string Label = "";
        public List<HifzItem> Items = new();
        public bool IsGroup;
    }
    List<DueEntry> _entries = new();
    DueEntry? _currentEntry;
    int _reviewIndex;
    bool _reviewRevealed;
    bool _groupStepping;               // stumbled in a group review → per-verse walk
    string? _reviewRecording;

    Popup? _picker;

    public HifzWindow()
    {
        InitializeComponent();
        HifzStore.Open();
        HifzStore.RefreshClock();
        Recorder.CleanupOld();
        FlowDirection = L10n.ArabicUI ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _surah = Math.Clamp(SettingsService.Current.HifzSurah, 1, Quran.Data.Surahs.Count);
        _from = Math.Max(1, SettingsService.Current.HifzFrom);
        _to = Math.Max(_from, SettingsService.Current.HifzTo);
        _reps = Math.Clamp(SettingsService.Current.HifzReps, 1, 15);
        ApplyTexts();
        SelectTab(TabMemorize);
        Loaded += (_, _) => UpdateStats();
        Closing += (_, _) => { Audio.StopAll(); Recorder.StopAll(); };
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // CaptionHeight is 0 (so header buttons get their clicks); the header
    // background drags the window manually instead.
    void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    void ApplyTexts()
    {
        TitleText.Text = L10n.T("MEMORIZATION — حفظ", "الحفظ — MEMORIZATION");
        TabMemorize.Content = L10n.T("Memorize", "الحفظ");
        TabReview.Content = L10n.T("Review", "المراجعة");
        TabProgress.Content = L10n.T("Progress", "التقدم");
        PickGroupLabel.Text = L10n.T("PICK WHAT TO MEMORIZE", "اختر ما تحفظه");
        PickHint.Text = L10n.T(
            "The classic method: listen to each verse several times, recite it with the text hidden, then link it to the verses before it. Finish with a full-group test; when you pass, the group joins the daily review ladder (1, 3, 7, 14, 30, 60, 120 days).",
            "الطريقة المتعارف عليها: استمع إلى الآية عدة مرات وأعدها والنص مخفي، ثم اربطها بما قبلها، واختم باختبار المجموعة كاملة. عند النجاح تدخل المجموعة سلّم المراجعة اليومي (1، 3، 7، 14، 30، 60، 120 يوماً).");
        SurahPicker.Content = SurahLabel(_surah);
        RangePicker.Content = L10n.AyahsLabel(_from, _to);
        RepsPicker.Content = L10n.T($"Listen {_reps}× per verse", $"استماع {_reps}× لكل آية");
        StartButton.Content = L10n.T("Start memorizing", "ابدأ الحفظ");

        DueLabel.Text = L10n.T("DUE FOR REVIEW TODAY", "المستحق للمراجعة اليوم");
        ReviewEmptyText.Text = L10n.T(
            "Nothing is due right now.\nMemorize a new group, and reviewed verses will come back here on their schedule.",
            "لا يوجد شيء مستحق الآن.\nاحفظ مجموعة جديدة، وستعود الآيات المحفوظة إلى هنا حسب جدولها.");

        BackupButton.Content = L10n.T("Backup", "نسخ احتياطي");
        RestoreButton.Content = L10n.T("Restore", "استعادة");
        ExportCsvButton.Content = L10n.T("Export CSV", "تصدير CSV");
        UpdateReminderLabel();

        ApplySessionTexts();
        ApplyReviewTexts();
        UpdateStats();
        UpdatePickHint();
    }

    // Labels that depend on the current session mode.
    void ApplySessionTexts()
    {
        switch (_mode)
        {
            case SessionMode.Link:
                MarkKnownButton.Content = L10n.T("Linked — next verse", "تم الربط — الآية التالية");
                FinishButton.Content = L10n.T("Test the group", "اختبار المجموعة");
                break;
            case SessionMode.LinkAll:
                MarkKnownButton.Content = L10n.T("Linked — start the test", "تم الربط — ابدأ الاختبار");
                FinishButton.Content = L10n.T("Test the group", "اختبار المجموعة");
                break;
            case SessionMode.Test:
                MarkKnownButton.Content = L10n.T("Back to rehearsing", "عودة للتدريب");
                FinishButton.Content = L10n.T("Restart the test", "إعادة الاختبار");
                break;
            default:
                MarkKnownButton.Content = L10n.T("I know it — next verse", "حفظتها — الآية التالية");
                FinishButton.Content = L10n.T("Mark group as memorized", "علّم المجموعة محفوظة");
                break;
        }
        TestHint.Text = SettingsService.Current.HifzTestBlank
            ? L10n.T(
                "Fill-in-the-blank test: two verses are hidden. Say each one from memory, then tap the verse area to uncover the next blank. When everything is uncovered, pass the test.",
                "اختبار أكمل الفراغ: آيتان مخفيتان. قل كل آية من حفظك ثم اضغط منطقة النص لإظهار الفراغ التالي، وعند اكتمال النص اعلن النجاح.")
            : L10n.T(
                "Final test: recite the whole group from memory with the text hidden, then reveal it to check yourself.",
                "الاختبار النهائي: أعد تلاوة المجموعة كاملة من حفظك والنص مخفي، ثم أظهر النص للتأكد.");
        TestPassButton.Content = L10n.T("I passed — save the group", "نجحت — احفظ المجموعة");
        TestRepeatButton.Content = L10n.T("Repeat the test", "أعد الاختبار");
        ToolTipService.SetToolTip(RecordButton, L10n.T("Record your recitation", "سجّل تلاوتك"));
        ToolTipService.SetToolTip(PlayMineButton, L10n.T("Play your recording", "شغّل تسجيلك"));
    }

    void ApplyReviewTexts()
    {
        GroupPassButton.Content = L10n.T("Group passed", "نجحت المجموعة");
        GroupStumbledButton.Content = L10n.T("I stumbled on a part", "تعثرت في موضع");
        RememberedButton.Content = L10n.T("I remembered it", "تذكرتها");
        ForgotButton.Content = L10n.T("I forgot it", "نسيته");
        ToolTipService.SetToolTip(ReviewRecordButton, L10n.T("Record your recitation", "سجّل تلاوتك"));
        ToolTipService.SetToolTip(ReviewPlayMineButton, L10n.T("Play your recording", "شغّل تسجيلك"));
    }

    string SurahLabel(int n)
    {
        var s = Quran.Surah(n);
        if (s == null) return "";
        return L10n.T($"{n}. {s.EnglishName}", $"{n}. {s.Name}");
    }

    void UpdateStats()
    {
        int total = HifzStore.TotalCount();
        int due = HifzStore.DueCount();
        StatsText.Text = L10n.T(
            $"{total} verses memorized · {due} due for review",
            $"{total} آية محفوظة · {due} مستحقة للمراجعة");
    }

    void UpdatePickHint()
    {
        var s = Quran.Surah(_surah);
        if (s == null) return;
        int count = _to - _from + 1;
        PickHint.Text = L10n.T(
            $"You picked {count} verse(s) from Surah {s.EnglishName}. Listen {(_reps)}× to each verse, recite it hidden, link it to the group, then test the whole group.",
            $"اخترت {count} آية من سورة {s.Name}. استمع {_reps} مرات لكل آية وأعدها والنص مخفي، ثم اربطها بالمجموعة، واختبر المجموعة كاملة.");
    }

    // ---------- generic picker popup ----------

    void ShowPicker(FrameworkElement target, IEnumerable<string> items, int selected, Action<int> pick)
    {
        ClosePicker();
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemContainerStyle = (Style)FindResource("DarkListBoxItem"),
            MaxHeight = 320,
        };
        list.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var entries = items.ToList();
        list.ItemsSource = entries;
        if (selected >= 0 && selected < entries.Count)
            list.SelectedIndex = selected;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            int i = list.SelectedIndex;
            ClosePicker();
            pick(i);
        };
        _picker = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        var border = new Border
        {
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("SurfaceBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Margin = new Thickness(10),
            MaxWidth = 420,
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (Color)FindResource("ShadowColor"),
            BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45,
        };
        border.Child = list;
        _picker.Child = border;
        _picker.Closed += (_, _) => _picker = null;
        _picker.IsOpen = true;
    }

    void ClosePicker()
    {
        if (_picker != null) { _picker.IsOpen = false; _picker = null; }
    }

    void SurahPicker_Click(object sender, RoutedEventArgs e) =>
        ShowPicker(SurahPicker,
            Enumerable.Range(1, Quran.Data.Surahs.Count).Select(SurahLabel),
            _surah - 1,
            i =>
            {
                _surah = i + 1;
                SettingsService.Current.HifzSurah = _surah;
                SettingsService.Save();
                _from = 1;
                _to = Math.Min(3, Quran.Surah(_surah)?.Ayahs.Count ?? 3);
                SettingsService.Current.HifzFrom = _from;
                SettingsService.Current.HifzTo = _to;
                SettingsService.Save();
                SurahPicker.Content = SurahLabel(_surah);
                RangePicker.Content = L10n.AyahsLabel(_from, _to);
                UpdatePickHint();
            });

    void RangePicker_Click(object sender, RoutedEventArgs e)
    {
        var s = Quran.Surah(_surah);
        if (s == null) return;
        ClosePicker();

        // two-column picker: start ayah, then group length
        var fromList = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemContainerStyle = (Style)FindResource("DarkListBoxItem"),
            MaxHeight = 260, MinWidth = 120,
        };
        fromList.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var lenList = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemContainerStyle = (Style)FindResource("DarkListBoxItem"),
            MaxHeight = 260, MinWidth = 120,
        };
        lenList.SetValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        fromList.ItemsSource = Enumerable.Range(1, s.Ayahs.Count)
            .Select(a => L10n.T($"From {a}", $"من {a}")).ToList();
        lenList.ItemsSource = Enumerable.Range(1, 10)
            .Select(n => L10n.T($"{n} verse(s)", $"{n} آية")).ToList();
        fromList.SelectedIndex = _from - 1;
        lenList.SelectedIndex = Math.Clamp(_to - _from, 0, 9);

        int from = _from;
        fromList.SelectionChanged += (_, _) =>
        {
            if (fromList.SelectedIndex < 0) return;
            from = fromList.SelectedIndex + 1;
            // a length that would run past the end of the surah is not offered;
            // if the current length no longer fits, fall back to the longest fit
            for (int i = 0; i < 10; i++)
            {
                if (lenList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                    item.IsEnabled = from + i <= s.Ayahs.Count;
            }
            if (lenList.SelectedIndex >= 0)
            {
                int len = lenList.SelectedIndex + 1;
                if (from + len - 1 > s.Ayahs.Count)
                    lenList.SelectedIndex = Math.Max(0, s.Ayahs.Count - from);
            }
        };
        lenList.SelectionChanged += (_, _) =>
        {
            if (lenList.SelectedIndex < 0 || fromList.SelectedIndex < 0) return;
            int len = lenList.SelectedIndex + 1;
            int start = fromList.SelectedIndex + 1;
            len = Math.Min(len, s.Ayahs.Count - start + 1);
            ClosePicker();
            _from = start;
            _to = start + len - 1;
            SettingsService.Current.HifzFrom = _from;
            SettingsService.Current.HifzTo = _to;
            SettingsService.Save();
            RangePicker.Content = L10n.AyahsLabel(_from, _to);
            UpdatePickHint();
        };

        var header = new TextBlock
        {
            Text = L10n.T("START", "البداية"),
            FontSize = 12, Foreground = (Brush)FindResource("TextFaint"), Margin = new Thickness(8, 4, 8, 4),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(fromList, 0);
        Grid.SetColumn(lenList, 1);
        grid.Children.Add(fromList);
        grid.Children.Add(lenList);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(grid);

        var border = new Border
        {
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("SurfaceBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Margin = new Thickness(10),
            MaxWidth = 300,
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = (Color)FindResource("ShadowColor"),
            BlurRadius = 16, ShadowDepth = 3, Opacity = 0.45,
        };
        border.Child = stack;
        _picker = new Popup
        {
            PlacementTarget = RangePicker,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = border,
        };
        _picker.Closed += (_, _) => _picker = null;
        _picker.IsOpen = true;
    }

    void RepsPicker_Click(object sender, RoutedEventArgs e) =>
        ShowPicker(RepsPicker,
            Enumerable.Range(1, 15).Select(n => L10n.T($"Listen {n}× per verse", $"استماع {n}× لكل آية")),
            _reps - 1,
            i =>
            {
                _reps = i + 1;
                SettingsService.Current.HifzReps = _reps;
                SettingsService.Save();
                RepsPicker.Content = L10n.T($"Listen {_reps}× per verse", $"استماع {_reps}× لكل آية");
                UpdatePickHint();
            });

    // ---------- tabs ----------

    void SelectTab(System.Windows.Controls.Button tab)
    {
        foreach (var b in new[] { TabMemorize, TabReview, TabProgress })
            b.Tag = b == tab ? "active" : null;
        MemorizePanel.Visibility = tab == TabMemorize ? Visibility.Visible : Visibility.Collapsed;
        ReviewPanel.Visibility = tab == TabReview ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = tab == TabProgress ? Visibility.Visible : Visibility.Collapsed;
        if (tab == TabReview) RefreshDueList();
        if (tab == TabProgress) RefreshProgress();
        if (tab == TabMemorize) UpdateStats();
    }

    void TabMemorize_Click(object sender, RoutedEventArgs e) => SelectTab(TabMemorize);
    void TabReview_Click(object sender, RoutedEventArgs e) => SelectTab(TabReview);
    void TabProgress_Click(object sender, RoutedEventArgs e) => SelectTab(TabProgress);

    // ---------- memorize session ----------

    void StartSession_Click(object sender, RoutedEventArgs e)
    {
        Audio.StopAll();
        Recorder.StopAll();
        var passage = Quran.GetPassage(_surah, _from, _to);
        if (passage.Verses.Count == 0) return;
        _session = passage.Verses;
        _sessionIndex = 0;
        _loopsDone = 0;
        _sessionRecording = null;
        PlayMineButton.IsEnabled = false;
        SessionCard.Visibility = Visibility.Visible;
        SetMode(SessionMode.Learn);
        StartButton.Content = L10n.T("Restart session", "إعادة بدء الجلسة");
    }

    void SetMode(SessionMode mode)
    {
        _mode = mode;
        _revealed = mode == SessionMode.Learn;
        TestPanel.Visibility = mode == SessionMode.Test ? Visibility.Visible : Visibility.Collapsed;
        SessionStatus.Text = "";
        if (mode == SessionMode.Learn) ShowCurrentVerse();
        else if (mode == SessionMode.Link) ShowLinkVerse();
        else if (mode == SessionMode.LinkAll) ShowLinkAll();
        else if (mode == SessionMode.Test) ShowTest();
        ApplySessionTexts();
    }

    Verse? CurrentVerse =>
        _session.Count > 0 && _sessionIndex >= 0 && _sessionIndex < _session.Count
            ? _session[_sessionIndex] : null;

    Passage PassageRange(int fromIdx, int toIdx)
    {
        var p = new Passage();
        for (int i = Math.Max(0, fromIdx); i <= Math.Min(toIdx, _session.Count - 1); i++)
            p.Verses.Add(_session[i]);
        return p;
    }

    Passage CurrentPassage()
    {
        var v = CurrentVerse;
        var p = new Passage();
        if (v != null) p.Verses.Add(v);
        return p;
    }

    void ShowCurrentVerse()
    {
        var v = CurrentVerse;
        if (v == null) { SessionCard.Visibility = Visibility.Collapsed; return; }
        SessionStep.Text = L10n.T(
            $"Verse {_sessionIndex + 1} of {_session.Count}",
            $"الآية {_sessionIndex + 1} من {_session.Count}");
        SessionVerse.Text = Quran.ArabicOf(CurrentPassage());
        SessionRef.Text = L10n.SurahRef(v.SurahNumber, v.NumberInSurah);
        SessionTranslation.Text = string.IsNullOrWhiteSpace(v.English) ? "" : v.English;
        SetVerseHidden(!_revealed);
        UpdateLoopCounter();
        PrevVerseButton.IsEnabled = _sessionIndex > 0;
        NextVerseButton.IsEnabled = _sessionIndex < _session.Count - 1;
        MarkKnownButton.IsEnabled = true;
        FinishButton.IsEnabled = true;
        UpdateLoopButtonGlyph();
    }

    // الربط: recite everything from the start of the group through the verse just
    // learned, with the text hidden — this is what welds the verses together.
    void ShowLinkVerse()
    {
        var v = CurrentVerse;
        if (v == null) { SessionCard.Visibility = Visibility.Collapsed; return; }
        _linkTo = _sessionIndex;
        SessionStep.Text = L10n.T(
            $"Linking (الربط) — verse {_sessionIndex + 1} of {_session.Count}",
            $"الربط — الآية {_sessionIndex + 1} من {_session.Count}");
        SessionVerse.Text = Quran.ArabicOf(PassageRange(0, _linkTo));
        SessionRef.Text = L10n.SurahRef(_session[0].SurahNumber, _session[0].NumberInSurah, _session[_linkTo].NumberInSurah);
        SessionTranslation.Text = "";
        SetVerseHidden(true);
        SessionStatus.Text = L10n.T(
            "Recite from the first verse of the group through this one, with the text hidden.",
            "أعد التلاوة من أول المجموعة إلى هذه الآية والنص مخفي.");
        MarkKnownButton.IsEnabled = true;
        FinishButton.IsEnabled = true;
        UpdateLoopButtonGlyph();
    }

    // The final link: the whole group, start to end, before the test.
    void ShowLinkAll()
    {
        _linkTo = _session.Count - 1;
        SessionStep.Text = L10n.T(
            "Full-group link (الربط) — done learning",
            "ربط المجموعة كاملة — انتهى الحفظ");
        SessionVerse.Text = Quran.ArabicOf(PassageRange(0, _linkTo));
        SessionRef.Text = L10n.SurahRef(_session[0].SurahNumber, _session[0].NumberInSurah, _session[_linkTo].NumberInSurah);
        SessionTranslation.Text = "";
        SetVerseHidden(true);
        SessionStatus.Text = L10n.T(
            "Recite the whole group from the beginning, then start the test.",
            "أعد تلاوة المجموعة كاملة من البداية، ثم ابدأ الاختبار.");
        MarkKnownButton.IsEnabled = true;
        FinishButton.IsEnabled = true;
        UpdateLoopButtonGlyph();
    }

    void SetVerseHidden(bool hidden)
    {
        SessionVerse.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        SessionTranslation.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        SessionRef.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        VerseCover.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
        HideButton.Content = hidden ? "\uE8B3" : "\uE7B3";
        ToolTipService.SetToolTip(HideButton, hidden
            ? L10n.T("Show the text", "إظهار النص")
            : L10n.T("Hide the text — recite from memory", "إخفاء النص — أعد من حفظك"));
        ToolTipService.SetToolTip(VerseCover, L10n.T("Tap to reveal", "اضغط للإظهار"));
    }

    void HideText_Click(object sender, RoutedEventArgs e)
    {
        _revealed = SessionVerse.Visibility != Visibility.Visible;
        SetVerseHidden(!_revealed);
    }

    void VerseCover_Click(object sender, MouseButtonEventArgs e)
    {
        // In the blank test the cover reveals blanks one at a time instead of all.
        if (_mode == SessionMode.Test && SettingsService.Current.HifzTestBlank)
        {
            RevealNextBlank();
            return;
        }
        _revealed = true;
        SetVerseHidden(false);
    }

    void UpdateLoopCounter()
    {
        var v = CurrentVerse;
        LoopCounter.Text = _mode == SessionMode.Learn && v != null
            ? L10n.T($"Listening {_loopsDone} of {_reps}", $"الاستماع {L10n.Ar(_loopsDone)} من {L10n.Ar(_reps)}")
            : "";
    }

    void UpdateLoopButtonGlyph()
    {
        LoopButton.Content = Audio.PlayingRecitation ? "\uE71A" : "\uE768";
    }

    // In Learn mode this loops the single verse; in Link / LinkAll modes it plays
    // the connected passage from the group start, as a model for the link step.
    void LoopPlay_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.PlayingRecitation) { Audio.StopAll(); UpdateLoopButtonGlyph(); return; }
        var reciter = SettingsService.Current.ReciterId;
        Passage passage;
        if (_mode == SessionMode.Link || _mode == SessionMode.LinkAll)
        {
            passage = PassageRange(0, _linkTo);
            _loopsDone = 0;
            Audio.PlayRecitation(passage, reciter, onVerse: _ => Dispatcher.BeginInvoke(UpdateLoopButtonGlyph));
        }
        else
        {
            var v = CurrentVerse;
            if (v == null) return;
            passage = CurrentPassage();
            _loopsDone = 0;
            Audio.PlayRecitation(passage, reciter,
                next: () =>
                {
                    _loopsDone++;
                    Dispatcher.BeginInvoke(UpdateLoopCounter);
                    return _loopsDone < _reps ? passage : null;
                },
                onVerse: _ => Dispatcher.BeginInvoke(UpdateLoopButtonGlyph));
            UpdateLoopCounter();
        }
        UpdateLoopButtonGlyph();
    }

    void MarkKnown_Click(object sender, RoutedEventArgs e)
    {
        Audio.StopAll();
        switch (_mode)
        {
            case SessionMode.Learn:
                if (_sessionIndex < _session.Count - 1)
                {
                    _sessionIndex++;
                    _loopsDone = 0;
                    SetMode(SessionMode.Link);
                }
                else
                {
                    SetMode(SessionMode.LinkAll);
                }
                break;
            case SessionMode.Link:
                _sessionIndex++;
                _loopsDone = 0;
                if (_sessionIndex >= _session.Count - 1) SetMode(SessionMode.LinkAll);
                else SetMode(SessionMode.Link);
                break;
            case SessionMode.LinkAll:
                SetMode(SessionMode.Test);
                break;
            case SessionMode.Test:
                SetMode(SessionMode.LinkAll);
                break;
        }
    }

    void NextVerse_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != SessionMode.Learn) return;
        if (_sessionIndex < _session.Count - 1)
        {
            _sessionIndex++;
            _loopsDone = 0;
            ShowCurrentVerse();
        }
    }

    void PrevVerse_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != SessionMode.Learn) return;
        if (_sessionIndex > 0)
        {
            _sessionIndex--;
            _loopsDone = 0;
            ShowCurrentVerse();
        }
    }

    // ---------- test gate (الامتحان) ----------

    void ShowTest()
    {
        SessionStep.Text = L10n.T("Final test", "الاختبار النهائي");
        SessionRef.Text = L10n.SurahRef(_session[0].SurahNumber, _session[0].NumberInSurah, _session[^1].NumberInSurah);
        SessionTranslation.Text = "";
        LoopCounter.Text = "";
        PrevVerseButton.IsEnabled = false;
        NextVerseButton.IsEnabled = false;

        if (SettingsService.Current.HifzTestBlank && _session.Count >= 3)
        {
            // hide two random verses inside the visible text
            var rng = new Random();
            _blanks = Enumerable.Range(0, _session.Count).OrderBy(_ => rng.Next()).Take(2).ToList();
            _blanks.Sort();
            ShowBlankText();
            TestPassButton.IsEnabled = false;
        }
        else
        {
            _blanks.Clear();
            SessionVerse.Text = Quran.ArabicOf(PassageRange(0, _session.Count - 1));
            SetVerseHidden(true);
            TestPassButton.IsEnabled = true;
        }
    }

    void ShowBlankText()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _session.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(_blanks.Contains(i)
                ? L10n.T("• • •", "• • •")
                : Quran.ArabicOf(new Passage { Verses = { _session[i] } }));
        }
        SessionVerse.Text = sb.ToString();
        SetVerseHidden(false);
        TestPassButton.IsEnabled = _blanks.Count == 0;
    }

    void RevealNextBlank()
    {
        if (_blanks.Count == 0) return;
        _blanks.RemoveAt(0);
        ShowBlankText();
    }

    void TestPass_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Count == 0) return;
        int added = HifzStore.AddRange(_session.Select(v => (v.SurahNumber, v.NumberInSurah)));
        Audio.StopAll();
        TestPanel.Visibility = Visibility.Collapsed;
        SetMode(SessionMode.Learn);
        SessionStatus.Text = added > 0
            ? L10n.T(
                $"Passed — {added} verse(s) saved. Due for review tomorrow, then at 3, 7, 14, 30, 60 and 120 days.",
                $"نجحت — تم حفظ {L10n.Ar(added)} آية. المراجعة غداً، ثم بعد 3 و7 و14 و30 و60 و120 يوماً.")
            : L10n.T(
                "Passed — these verses were already on the review schedule, so their progress is kept.",
                "نجحت — هذه الآيات موجودة في جدول المراجعة أصلاً، وحُفظ تقدمها.");
        UpdateStats();
    }

    void TestRepeat_Click(object sender, RoutedEventArgs e) => SetMode(SessionMode.Test);

    // "Mark group as memorized" now leads through the test gate instead of
    // saving immediately.
    void FinishSession_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Count == 0) return;
        Audio.StopAll();
        SetMode(SessionMode.Test);
    }

    // ---------- record & compare ----------

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (Recorder.Recording)
        {
            var file = Recorder.Stop();
            if (file != null) { _sessionRecording = file; PlayMineButton.IsEnabled = true; }
            RecordButton.Content = "\uE720";
            return;
        }
        Audio.StopAll();
        var started = Recorder.Start();
        if (started == null)
        {
            SessionStatus.Text = L10n.T("No microphone is available for recording.", "لا يوجد ميكروفون متاح للتسجيل.");
            return;
        }
        RecordButton.Content = "\uE71A";
    }

    void PlayMine_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionRecording != null) Recorder.PlayMine(_sessionRecording);
    }

    void ReviewRecord_Click(object sender, RoutedEventArgs e)
    {
        if (Recorder.Recording)
        {
            var file = Recorder.Stop();
            if (file != null) { _reviewRecording = file; ReviewPlayMineButton.IsEnabled = true; }
            ReviewRecordButton.Content = "\uE720";
            return;
        }
        Audio.StopAll();
        var started = Recorder.Start();
        if (started == null)
        {
            SessionStatus.Text = L10n.T("No microphone is available for recording.", "لا يوجد ميكروفون متاح للتسجيل.");
            return;
        }
        ReviewRecordButton.Content = "\uE71A";
    }

    void ReviewPlayMine_Click(object sender, RoutedEventArgs e)
    {
        if (_reviewRecording != null) Recorder.PlayMine(_reviewRecording);
    }

    // ---------- review ----------

    void RefreshDueList()
    {
        HifzStore.RefreshClock();
        BuildEntries();
        var rows = new List<object>();
        var newEntries = _entries.Where(en => !en.Items[0].IsOld).ToList();
        var oldEntries = _entries.Where(en => en.Items[0].IsOld).ToList();
        if (newEntries.Count > 0)
            rows.Add(L10n.T("TODAY'S NEW REVIEW — حديث", "مراجعة الحديث اليوم"));
        foreach (var en in newEntries) rows.Add(en);
        if (oldEntries.Count > 0)
            rows.Add(L10n.T("TODAY'S OLD REVIEW — قديم", "مراجعة القديم اليوم"));
        foreach (var en in oldEntries) rows.Add(en);
        DueList.ItemsSource = rows;
        if (_entries.Count == 0) ShowReviewEmpty();
        UpdateStats();
    }

    // Collapse adjacent due verses that share a memorization group into one
    // connected-review entry; the daily cap applies to old hifz verses.
    void BuildEntries()
    {
        _entries.Clear();
        var due = HifzStore.DueItems();
        int maxOld = Math.Clamp(SettingsService.Current.HifzMaxOldReview, 1, 500);
        int oldKept = 0;
        foreach (var item in due)
        {
            if (item.IsOld)
            {
                if (oldKept >= maxOld) continue;
                oldKept++;
            }
            AddToEntries(item);
        }
    }

    void AddToEntries(HifzItem item)
    {
        // merge into an existing group entry of the same group
        if (item.GroupId > 0)
        {
            var existing = _entries.FirstOrDefault(en =>
                en.IsGroup && en.Items[0].GroupId == item.GroupId);
            if (existing != null)
            {
                existing.Items.Add(item);
                existing.Items.Sort((a, b) => a.Ayah.CompareTo(b.Ayah));
                existing.Label = EntryLabel(existing);
                return;
            }
        }
        _entries.Add(new DueEntry
        {
            Items = { item },
            IsGroup = false,
            Label = L10n.SurahRef(item.Surah, item.Ayah),
        });
    }

    string EntryLabel(DueEntry en)
    {
        int s = en.Items[0].Surah;
        int from = en.Items.Min(i => i.Ayah);
        int to = en.Items.Max(i => i.Ayah);
        return L10n.SurahRef(s, from, to) + L10n.T("  ·  connected", "  ·  متصلة");
    }

    void ShowReviewEmpty()
    {
        ReviewEmpty.Visibility = Visibility.Visible;
        ReviewCard.Visibility = Visibility.Collapsed;
    }

    void DueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DueList.SelectedItem is not DueEntry entry) return;
        _currentEntry = entry;
        _reviewRecording = null;
        ReviewPlayMineButton.IsEnabled = false;
        _reviewRevealed = false;
        _groupStepping = false;
        _reviewIndex = 0;
        ShowReviewVerse();
    }

    Passage EntryPassage(DueEntry en)
    {
        var p = new Passage();
        foreach (var it in en.Items)
        {
            var v = Quran.Surah(it.Surah)?.Ayahs.ElementAtOrDefault(it.Ayah - 1);
            if (v != null) p.Verses.Add(v);
        }
        return p;
    }

    void ShowReviewVerse()
    {
        var entry = _currentEntry;
        if (entry == null) return;
        var passage = EntryPassage(entry);
        if (passage.Verses.Count == 0) { ShowReviewEmpty(); return; }

        ReviewEmpty.Visibility = Visibility.Collapsed;
        ReviewCard.Visibility = Visibility.Visible;

        bool groupMode = entry.IsGroup && !_groupStepping;
        GroupPassButton.Visibility = groupMode ? Visibility.Visible : Visibility.Collapsed;
        GroupStumbledButton.Visibility = groupMode ? Visibility.Visible : Visibility.Collapsed;
        RememberedButton.Visibility = groupMode ? Visibility.Collapsed : Visibility.Visible;
        ForgotButton.Visibility = groupMode ? Visibility.Collapsed : Visibility.Visible;

        Verse verse;
        if (groupMode)
        {
            verse = passage.Verses[0];
            ReviewVerse.Text = Quran.ArabicOf(passage);
            ReviewRef.Text = L10n.SurahRef(verse.SurahNumber, verse.NumberInSurah, passage.Verses[^1].NumberInSurah);
            ReviewStage.Text = L10n.T(
                $"Connected review of {passage.Verses.Count} verse(s) · recite the whole passage from memory",
                $"مراجعة متصلة لعدد {L10n.Ar(passage.Verses.Count)} آية · أعد تلاوة المقطع كاملاً من حفظك");
        }
        else
        {
            var item = entry.Items[Math.Clamp(_reviewIndex, 0, entry.Items.Count - 1)];
            var v = Quran.Surah(item.Surah)?.Ayahs.ElementAtOrDefault(item.Ayah - 1);
            if (v == null) { ShowReviewEmpty(); return; }
            verse = v;
            var single = new Passage(); single.Verses.Add(v);
            ReviewVerse.Text = Quran.ArabicOf(single);
            ReviewRef.Text = L10n.SurahRef(v.SurahNumber, v.NumberInSurah);
            int stage = Math.Min(item.Stage + 1, HifzStore.Intervals.Length);
            ReviewStage.Text = L10n.T(
                $"Review level {stage} of {HifzStore.Intervals.Length} · forgotten {item.Lapses}×",
                $"مستوى المراجعة {L10n.Ar(stage)} من {L10n.Ar(HifzStore.Intervals.Length)} · نُسي {L10n.Ar(item.Lapses)} مرة");
        }

        ReviewCover.Visibility = _reviewRevealed ? Visibility.Collapsed : Visibility.Visible;
        ReviewVerse.Visibility = _reviewRevealed ? Visibility.Visible : Visibility.Collapsed;
        ReviewTranslation.Visibility = _reviewRevealed ? Visibility.Visible : Visibility.Collapsed;
        ReviewTranslation.Text = string.IsNullOrWhiteSpace(verse.English) ? "" : verse.English;
        ReviewRevealButton.Content = _reviewRevealed ? "\uE7B3" : "\uE8B3";
        ToolTipService.SetToolTip(ReviewRevealButton, _reviewRevealed
            ? L10n.T("Hide the text", "إخفاء النص")
            : L10n.T("Reveal the text", "إظهار النص"));
        ToolTipService.SetToolTip(ReviewCover, L10n.T("Tap to reveal after reciting from memory", "اضغط للإظهار بعد التلاوة من الحفظ"));
    }

    void ReviewReveal_Click(object sender, RoutedEventArgs e)
    {
        _reviewRevealed = !_reviewRevealed;
        ShowReviewVerse();
    }

    void ReviewCover_Click(object sender, MouseButtonEventArgs e)
    {
        _reviewRevealed = true;
        ShowReviewVerse();
    }

    void ReviewPlay_Click(object sender, RoutedEventArgs e)
    {
        var entry = _currentEntry;
        if (entry == null) return;
        var p = EntryPassage(entry);
        if (p.Verses.Count == 0) return;
        Audio.PlayRecitation(p, SettingsService.Current.ReciterId);
    }

    // Group passed as a whole: every verse in it advances one ladder step.
    void GroupPass_Click(object sender, RoutedEventArgs e)
    {
        var entry = _currentEntry;
        if (entry == null) return;
        Audio.StopAll();
        HifzStore.RememberedRange(entry.Items.Select(i => (i.Surah, i.Ayah)));
        AdvanceReview(entry);
    }

    // Stumbled: reveal the passage and walk it verse by verse, so the exact
    // forgotten verses get marked forgot and the rest remembered.
    void GroupStumbled_Click(object sender, RoutedEventArgs e)
    {
        _groupStepping = true;
        _reviewIndex = 0;
        _reviewRevealed = false;
        ShowReviewVerse();
    }

    void Remembered_Click(object sender, RoutedEventArgs e)
    {
        var entry = _currentEntry;
        if (entry == null) return;
        var item = entry.Items[Math.Clamp(_reviewIndex, 0, entry.Items.Count - 1)];
        Audio.StopAll();
        HifzStore.Remembered(item.Surah, item.Ayah);
        StepOrFinish(entry);
    }

    void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var entry = _currentEntry;
        if (entry == null) return;
        var item = entry.Items[Math.Clamp(_reviewIndex, 0, entry.Items.Count - 1)];
        Audio.StopAll();
        HifzStore.Forgot(item.Surah, item.Ayah);
        StepOrFinish(entry);
    }

    void StepOrFinish(DueEntry entry)
    {
        if (_groupStepping && _reviewIndex < entry.Items.Count - 1)
        {
            _reviewIndex++;
            _reviewRevealed = false;
            ShowReviewVerse();
            return;
        }
        AdvanceReview(entry);
    }

    void AdvanceReview(DueEntry entry)
    {
        _entries.Remove(entry);
        _currentEntry = null;
        _groupStepping = false;
        _reviewRevealed = false;
        RefreshDueList();
    }

    // ---------- progress ----------

    void RefreshProgress()
    {
        HifzStore.RefreshClock();
        int total = HifzStore.TotalCount();
        int due = HifzStore.DueCount();
        var bySurah = HifzStore.ProgressBySurah();
        ProgressTotal.Text = L10n.T($"{total} verses memorized", $"{total} آية محفوظة");
        ProgressDue.Text = L10n.T($"{due} due for review", $"{due} مستحقة للمراجعة");
        ProgressSurahs.Text = L10n.T($"{bySurah.Count} surah(s) started", $"{bySurah.Count} سورة بدأت");

        int daily = Math.Max(0, SettingsService.Current.HifzDailyNew);
        int added = HifzStore.AddedToday();
        ProgressPlan.Text = daily == 0
            ? L10n.T($"Today's portion: {added} new verse(s) (free-form)",
                     $"جزء اليوم: {L10n.Ar(added)} آية جديدة (حر)")
            : L10n.T($"Today's portion: {added} of {daily} new verse(s)",
                     $"جزء اليوم: {L10n.Ar(added)} من {L10n.Ar(daily)} آية جديدة");

        int streak = HifzStore.Streak();
        ProgressStreak.Text = L10n.T($"Streak: {streak} day(s)", $"أيام متواصلة: {L10n.Ar(streak)}");

        // last 14 days: ● reviewed, ○ missed (oldest → today)
        var hist = HifzStore.RecentHistory(14);
        ProgressStrip.Text = L10n.T("Last 14 days:  ", "آخر ١٤ يوماً:  ") + string.Join(' ',
            hist.Select(h => h.Ok + h.Fail > 0 ? "●" : "○")) + " ◀ " + L10n.T("today", "اليوم");

        var rows = new List<string>();
        foreach (var (s, count) in bySurah)
        {
            var surah = Quran.Surah(s);
            string name = surah == null ? "" : L10n.T(surah.EnglishName, surah.Name);
            rows.Add(L10n.T($"{s}. {name} — {count} of {surah?.Ayahs.Count ?? 0} verses",
                            $"{s}. {name} — {count} من {surah?.Ayahs.Count ?? 0} آية"));
        }
        ProgressList.ItemsSource = rows;
    }

    // ---------- backup / restore / export ----------

    // Cycles the daily review reminder: off, then a set of sensible hours.
    static readonly int[] ReminderHours = { 8, 12, 15, 18, 20, 21 };

    void UpdateReminderLabel()
    {
        var s = SettingsService.Current;
        ReminderButton.Content = s.HifzReminder
            ? L10n.T($"Daily reminder: {s.HifzReminderHour}:00", $"تذكير يومي: {L10n.Ar(s.HifzReminderHour)}:00")
            : L10n.T("Daily reminder: off", "تذكير يومي: متوقف");
    }

    void Reminder_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Current;
        if (!s.HifzReminder)
        {
            s.HifzReminder = true;
            if (!ReminderHours.Contains(s.HifzReminderHour)) s.HifzReminderHour = 20;
        }
        else
        {
            int i = Array.IndexOf(ReminderHours, s.HifzReminderHour);
            if (i >= 0 && i < ReminderHours.Length - 1) s.HifzReminderHour = ReminderHours[i + 1];
            else s.HifzReminder = false;
        }
        SettingsService.Save();
        UpdateReminderLabel();
    }


    void Backup_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.SaveFileDialog
        {
            Filter = "Hifz backup (*.db)|*.db",
            FileName = $"hifz-backup-{DateTime.Now:yyyy-MM-dd}.db",
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            HifzStore.Checkpoint();
            File.Copy(HifzStore.DatabasePath, dlg.FileName, overwrite: true);
            ProgressPlan.Text = L10n.T("Backup saved.", "تم حفظ النسخة الاحتياطية.");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, L10n.T("Mishkat", "مشكاة"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.OpenFileDialog { Filter = "Hifz backup (*.db)|*.db" };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        var answer = System.Windows.MessageBox.Show(
            L10n.T("Restoring will replace your current memorization progress with the backup. Continue?",
                   "ستؤدي الاستعادة إلى استبدال تقدم الحفظ الحالي بالنسخة الاحتياطية. هل تريد المتابعة؟"),
            L10n.T("Mishkat", "مشكاة"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            HifzStore.Close();
            File.Copy(dlg.FileName, HifzStore.DatabasePath, overwrite: true);
            HifzStore.Open();
            HifzStore.RefreshClock();
            RefreshProgress();
            UpdateStats();
            ProgressPlan.Text = L10n.T("Backup restored.", "تمت استعادة النسخة الاحتياطية.");
        }
        catch (Exception ex)
        {
            try { HifzStore.Open(); } catch { /* already failing */ }
            System.Windows.MessageBox.Show(ex.Message, L10n.T("Mishkat", "مشكاة"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"hifz-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        try
        {
            var lines = new List<string> { "surah,ayah,stage,due_in_days,lapses" };
            foreach (var it in HifzStore.AllItems())
                lines.Add($"{it.Surah},{it.Ayah},{it.Stage},{Math.Max(0, it.DueDay - HifzStore.Today)},{it.Lapses}");
            File.WriteAllLines(dlg.FileName, lines);
            ProgressPlan.Text = L10n.T("CSV exported.", "تم تصدير ملف CSV.");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, L10n.T("Mishkat", "مشكاة"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
