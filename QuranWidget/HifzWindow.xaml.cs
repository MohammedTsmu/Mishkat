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

namespace QuranWidget;

public partial class HifzWindow : Window
{
    // ---------- pick state ----------
    int _surah = 1;
    int _from = 1, _to = 3;
    int _reps = 7;

    // ---------- session state ----------
    List<Verse> _session = new();
    int _sessionIndex;
    int _loopsDone;

    // ---------- review state ----------
    List<HifzItem> _due = new();
    HifzItem? _currentReview;
    bool _reviewRevealed;

    Popup? _picker;

    public HifzWindow()
    {
        InitializeComponent();
        HifzStore.Open();
        HifzStore.RefreshClock();
        FlowDirection = L10n.ArabicUI ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _surah = Math.Clamp(SettingsService.Current.HifzSurah, 1, Quran.Data.Surahs.Count);
        _from = Math.Max(1, SettingsService.Current.HifzFrom);
        _to = Math.Max(_from, SettingsService.Current.HifzTo);
        _reps = Math.Clamp(SettingsService.Current.HifzReps, 1, 15);
        ApplyTexts();
        SelectTab(TabMemorize);
        Loaded += (_, _) => UpdateStats();
        Closing += (_, _) => Audio.StopAll();
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
            "The classic method: listen to each verse several times, then recite it from memory with the text hidden. When you finish the group, mark it memorized and it joins the daily review ladder (1, 3, 7, 14, 30, 60, 120 days).",
            "الطريقة المتعارف عليها: استمع إلى الآية عدة مرات، ثم أعد تلاوتها من حفظك والنص مخفي. عند إنهاء المجموعة علّمها محفوظة فتدخل سلّم المراجعة اليومي (1، 3، 7، 14، 30، 60، 120 يوماً).");
        SurahPicker.Content = SurahLabel(_surah);
        RangePicker.Content = L10n.AyahsLabel(_from, _to);
        RepsPicker.Content = L10n.T($"Listen {_reps}× per verse", $"استماع {_reps}× لكل آية");
        StartButton.Content = L10n.T("Start memorizing", "ابدأ الحفظ");

        MarkKnownButton.Content = L10n.T("I know it — next verse", "حفظتها — الآية التالية");
        FinishButton.Content = L10n.T("Mark group as memorized", "علّم المجموعة محفوظة");

        DueLabel.Text = L10n.T("DUE FOR REVIEW TODAY", "المستحق للمراجعة اليوم");
        ReviewEmptyText.Text = L10n.T(
            "Nothing is due right now.\nMemorize a new group, and reviewed verses will come back here on their schedule.",
            "لا يوجد شيء مستحق الآن.\nاحفظ مجموعة جديدة، وستعود الآيات المحفوظة إلى هنا حسب جدولها.");
        RememberedButton.Content = L10n.T("I remembered it", "تذكرتها");
        ForgotButton.Content = L10n.T("I forgot it", "نسيته");

        UpdateStats();
        UpdatePickHint();
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
        if (DueList.ItemsSource is List<string> && _due.Count == 0 && due == 0)
            ShowReviewEmpty();
    }

    void UpdatePickHint()
    {
        var s = Quran.Surah(_surah);
        if (s == null) return;
        int count = _to - _from + 1;
        PickHint.Text = L10n.T(
            $"You picked {count} verse(s) from Surah {s.EnglishName}. Listen {(_reps)}× to each verse, recite it hidden, then continue. Mark the group memorized at the end.",
            $"اخترت {count} آية من سورة {s.Name}. استمع {_reps} مرات لكل آية وأعدها والنص مخفي، ثم انتقل للتالية. وعلّم المجموعة محفوظة في النهاية.");
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
            FontSize = 10, Foreground = (Brush)FindResource("TextFaint"), Margin = new Thickness(8, 4, 8, 4),
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
        var passage = Quran.GetPassage(_surah, _from, _to);
        if (passage.Verses.Count == 0) return;
        _session = passage.Verses;
        _sessionIndex = 0;
        _loopsDone = 0;
        SessionCard.Visibility = Visibility.Visible;
        ShowCurrentVerse();
        SessionStatus.Text = "";
        StartButton.Content = L10n.T("Restart session", "إعادة بدء الجلسة");
    }

    Verse? CurrentVerse =>
        _session.Count > 0 && _sessionIndex >= 0 && _sessionIndex < _session.Count
            ? _session[_sessionIndex] : null;

    Passage CurrentPassage()
    {
        var p = new Passage();
        var v = CurrentVerse;
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
        SetVerseHidden(false);
        UpdateLoopCounter();
        PrevVerseButton.IsEnabled = _sessionIndex > 0;
        NextVerseButton.IsEnabled = _sessionIndex < _session.Count - 1;
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
        if (SessionVerse.Visibility == Visibility.Visible) SetVerseHidden(true);
        else SetVerseHidden(false);
    }

    void VerseCover_Click(object sender, MouseButtonEventArgs e) => SetVerseHidden(false);

    void UpdateLoopCounter()
    {
        var v = CurrentVerse;
        LoopCounter.Text = v == null ? ""
            : L10n.T($"Listening {_loopsDone} of {_reps}", $"الاستماع {L10n.Ar(_loopsDone)} من {L10n.Ar(_reps)}");
    }

    void UpdateLoopButtonGlyph()
    {
        LoopButton.Content = Audio.PlayingRecitation ? "\uE71A" : "\uE768";
    }

    void LoopPlay_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.PlayingRecitation) { Audio.StopAll(); UpdateLoopButtonGlyph(); return; }
        var v = CurrentVerse;
        if (v == null) return;
        var reciter = SettingsService.Current.ReciterId;
        var passage = CurrentPassage();
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
        UpdateLoopButtonGlyph();
    }

    void MarkKnown_Click(object sender, RoutedEventArgs e)
    {
        Audio.StopAll();
        SetVerseHidden(false);
        if (_sessionIndex < _session.Count - 1)
        {
            _sessionIndex++;
            _loopsDone = 0;
            ShowCurrentVerse();
            SessionStatus.Text = L10n.T(
                "Recite this verse from memory with the text hidden, then continue.",
                "أعد هذه الآية من حفظك والنص مخفي، ثم انتقل.");
        }
        else
        {
            SessionStatus.Text = L10n.T(
                "Group finished — tap “Mark group as memorized” to add it to the review ladder, or rehearse it again.",
                "انتهت المجموعة — اضغط «علّم المجموعة محفوظة» لإضافتها إلى سلّم المراجعة، أو كررها مرة أخرى.");
        }
    }

    void NextVerse_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionIndex < _session.Count - 1)
        {
            _sessionIndex++;
            _loopsDone = 0;
            ShowCurrentVerse();
        }
    }

    void PrevVerse_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionIndex > 0)
        {
            _sessionIndex--;
            _loopsDone = 0;
            ShowCurrentVerse();
        }
    }

    void FinishSession_Click(object sender, RoutedEventArgs e)
    {
        if (_session.Count == 0) return;
        HifzStore.AddRange(_session.Select(v => (v.SurahNumber, v.NumberInSurah)));
        Audio.StopAll();
        SessionStatus.Text = L10n.T(
            $"Saved: {_session.Count} verse(s) will be due for review tomorrow, then at 3, 7, 14, 30, 60 and 120 days.",
            $"تم الحفظ: {_session.Count} آية ستحتاج مراجعة غداً، ثم بعد 3 و7 و14 و30 و60 و120 يوماً.");
        UpdateStats();
    }

    // ---------- review ----------

    void RefreshDueList()
    {
        HifzStore.RefreshClock();
        _due = HifzStore.DueItems();
        var labels = _due.Select(i => L10n.SurahRef(i.Surah, i.Ayah)).ToList();
        DueList.ItemsSource = labels;
        if (_due.Count == 0) ShowReviewEmpty();
        UpdateStats();
    }

    void ShowReviewEmpty()
    {
        ReviewEmpty.Visibility = Visibility.Visible;
        ReviewCard.Visibility = Visibility.Collapsed;
    }

    void DueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = DueList.SelectedIndex;
        if (i < 0 || i >= _due.Count) return;
        _currentReview = _due[i];
        _reviewRevealed = false;
        ShowReviewVerse();
    }

    void ShowReviewVerse()
    {
        var item = _currentReview;
        if (item == null) return;
        var v = Quran.Surah(item.Surah)?.Ayahs.ElementAtOrDefault(item.Ayah - 1);
        if (v == null) { ShowReviewEmpty(); return; }
        var p = new Passage(); p.Verses.Add(v);
        ReviewEmpty.Visibility = Visibility.Collapsed;
        ReviewCard.Visibility = Visibility.Visible;
        ReviewCover.Visibility = _reviewRevealed ? Visibility.Collapsed : Visibility.Visible;
        ReviewVerse.Visibility = _reviewRevealed ? Visibility.Visible : Visibility.Collapsed;
        ReviewTranslation.Visibility = _reviewRevealed ? Visibility.Visible : Visibility.Collapsed;
        ReviewVerse.Text = Quran.ArabicOf(p);
        ReviewRef.Text = L10n.SurahRef(v.SurahNumber, v.NumberInSurah);
        ReviewTranslation.Text = string.IsNullOrWhiteSpace(v.English) ? "" : v.English;
        ReviewRevealButton.Content = _reviewRevealed ? "\uE7B3" : "\uE8B3";
        int stage = Math.Min(item.Stage + 1, HifzStore.Intervals.Length);
        ReviewStage.Text = L10n.T(
            $"Review level {stage} of {HifzStore.Intervals.Length} · forgotten {item.Lapses}×",
            $"مستوى المراجعة {L10n.Ar(stage)} من {L10n.Ar(HifzStore.Intervals.Length)} · نُسي {L10n.Ar(item.Lapses)} مرة");
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
        var item = _currentReview;
        if (item == null) return;
        var v = Quran.Surah(item.Surah)?.Ayahs.ElementAtOrDefault(item.Ayah - 1);
        if (v == null) return;
        var p = new Passage(); p.Verses.Add(v);
        Audio.PlayRecitation(p, SettingsService.Current.ReciterId);
    }

    void Remembered_Click(object sender, RoutedEventArgs e)
    {
        var item = _currentReview;
        if (item == null) return;
        Audio.StopAll();
        HifzStore.Remembered(item.Surah, item.Ayah);
        AdvanceReview(item);
    }

    void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var item = _currentReview;
        if (item == null) return;
        Audio.StopAll();
        HifzStore.Forgot(item.Surah, item.Ayah);
        AdvanceReview(item);
    }

    void AdvanceReview(HifzItem item)
    {
        _due.Remove(item);
        _currentReview = null;
        var labels = DueList.ItemsSource as List<string> ?? new List<string>();
        int i = DueList.SelectedIndex;
        if (i >= 0 && i < labels.Count) labels.RemoveAt(i);
        _reviewRevealed = false;
        DueList.ItemsSource = null;
        DueList.ItemsSource = labels;
        if (labels.Count == 0)
        {
            ShowReviewEmpty();
        }
        else
        {
            DueList.SelectedIndex = Math.Min(Math.Max(i, 0), labels.Count - 1);
        }
        UpdateStats();
    }

    // ---------- progress ----------

    void RefreshProgress()
    {
        int total = HifzStore.TotalCount();
        int due = HifzStore.DueCount();
        var bySurah = HifzStore.ProgressBySurah();
        ProgressTotal.Text = L10n.T($"{total} verses memorized", $"{total} آية محفوظة");
        ProgressDue.Text = L10n.T($"{due} due for review", $"{due} مستحقة للمراجعة");
        ProgressSurahs.Text = L10n.T($"{bySurah.Count} surah(s) started", $"{bySurah.Count} سورة بدأت");
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
}




