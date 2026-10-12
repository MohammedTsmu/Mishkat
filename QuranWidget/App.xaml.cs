using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace QuranWidget;

public partial class App : System.Windows.Application
{
    public static App Instance => (App)Current;
    public static bool Exiting { get; private set; }

    readonly List<MainWindow> _widgets = new();
    WinForms.NotifyIcon? _tray;
    Mutex? _mutex;
    CancellationTokenSource? _pipeCts;
    int _extraCount;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smoke = e.Args.Contains("--smoke");
        bool hidden = e.Args.Contains("--hidden");
        bool openNew = e.Args.Contains("--new");

        _mutex = new Mutex(true, @"Local\QuranWidget.SingleInstance", out bool first);
        if (!first)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "QuranWidget.Pipe", PipeDirection.Out);
                client.Connect(1500);
                using var w = new StreamWriter(client) { AutoFlush = true };
                w.WriteLine(openNew ? "new" : "show");
            }
            catch
            {
                // another instance is busy; nothing else to do
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(LogPath(), $"{DateTime.Now:u} {args.Exception}\n");
            }
            catch
            {
                // ignore logging failures
            }
            args.Handled = true;
        };
        SessionEnding += (_, _) => { if (!Exiting) ExitApp(); };

        SettingsService.Load();
        ThemeService.Apply(SettingsService.Current.Theme);
        try
        {
            Quran.Load();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to load Quran data:\n{ex.Message}", "Quran Widget",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Translations.Load(SettingsService.Current.TranslationId);
        try { HifzStore.Open(); } catch { /* hifz persistence is best-effort */ }
        Audio.SetSleepTimer(SettingsService.Current.SleepMinutes);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Gentle, pixel-precise scrolling everywhere: touchpads fire many small
        // wheel events and the default WPF response (3 lines per event) scrolls
        // far too fast to aim at anything. This scales the delta down and takes
        // over scrolling for every ScrollViewer in the app.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollPreviewMouseWheel), true);

        BuildTray();

        var daily = new MainWindow("daily", daily: true);
        _widgets.Add(daily);
        if (!hidden) daily.Show();

        if (openNew) CreateWidget();
        StartPipeServer();

        if (!smoke && !hidden)
        {
            _tray?.ShowBalloonTip(2500, "Quran Widget",
                L10n.T("Your verse of the day is on the desktop.\nRight-click it for more options.","آية اليوم على سطح مكتبك.\nانقر بزر الفأرة الأيمن للمزيد من الخيارات."),
                WinForms.ToolTipIcon.Info);
        }

        if (smoke) RunSmoke();
    }

    void RunSmoke()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4000) };
        t.Tick += (_, _) =>
        {
            try
            {
                var w = _widgets.FirstOrDefault();
                var info = $"SMOKE OK surahs={Quran.Data.Surahs.Count} verses={Quran.VerseCount} " +
                           $"pool={Quran.Pool.Count} ref={w?.CurrentReference ?? "none"}";
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "quranwidget_smoke.txt"), info);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "quranwidget_smoke.txt"), "SMOKE FAIL " + ex);
                }
                catch
                {
                    // nothing more we can do
                }
            }
            finally
            {
                Exiting = true;
                Shutdown(0);
            }
        };
        t.Start();
    }

    void BuildTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = IconFactory.CreateIcon(32),
            Text = "Quran Widget",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        var dailyItem = new WinForms.ToolStripMenuItem(L10n.T("Verse of the Day", "آية اليوم"), null, (_, _) => ShowDaily()) { Name = "Daily" };
        var newWidget = new WinForms.ToolStripMenuItem(L10n.T("New Verse Widget", "أداة آية جديدة"), null, (_, _) => CreateWidget()) { Name = "NewWidget" };
        var hifzItem = new WinForms.ToolStripMenuItem(L10n.T("Memorize (Hifz)", "الحفظ"), null, (_, _) => ShowHifz()) { Name = "Hifz" };
        var updates = new WinForms.ToolStripMenuItem(L10n.T("Check for Updates", "التحقق من التحديثات"), null, (_, _) => CheckForUpdates()) { Name = "Updates" };
        var log = new WinForms.ToolStripMenuItem(L10n.T("Open Log File", "فتح ملف السجل"), null, (_, _) => OpenLog()) { Name = "Log" };
        var unpin = new WinForms.ToolStripMenuItem(L10n.T("Unpin from desktop", "إلغاء التثبيت من سطح المكتب"), null, (_, _) => UnpinAll()) { Name = "Unpin" };
        var exit = new WinForms.ToolStripMenuItem(L10n.T("Exit", "خروج"), null, (_, _) => ExitApp()) { Name = "Exit" };
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            dailyItem, newWidget, hifzItem, new WinForms.ToolStripSeparator(), updates, log,
            new WinForms.ToolStripSeparator(), unpin, new WinForms.ToolStripSeparator(), exit,
        });

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowDaily();
    }

    static void OnScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        var sv = (ScrollViewer)sender;

        // only the innermost ScrollViewer under the pointer responds, so nested
        // lists inside scrollable pages keep their own gentle scrolling
        var d = e.OriginalSource as DependencyObject;
        while (d != null && !ReferenceEquals(d, sv))
        {
            if (d is ScrollViewer && !ReferenceEquals(d, sv)) return;
            d = d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }

        // touchpad events carry small deltas but arrive very often; scaling the
        // delta (instead of using line-based scrolling) keeps both devices sane
        double notches = e.Delta / 120.0;
        double amount = notches * System.Windows.SystemParameters.WheelScrollLines * 16 * 0.4;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - amount);
        }
        else if (sv.ScrollableHeight > 0)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - amount);
        }
        else if (sv.ScrollableWidth > 0)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - amount);
        }
        else
        {
            return; // nothing to scroll here; let the outer surface try
        }
        e.Handled = true;
    }

    public void ApplyTrayLanguage()    {
        if (_tray == null) return;
        _tray.Text = "Quran Widget";
        foreach (var item in _tray.ContextMenuStrip!.Items.OfType<WinForms.ToolStripMenuItem>())
        {
            item.Text = item.Name switch
            {
                "Daily" => L10n.T("Verse of the Day", "آية اليوم"),
                "NewWidget" => L10n.T("New Verse Widget", "أداة آية جديدة"),
                "Hifz" => L10n.T("Memorize (Hifz)", "الحفظ"),
                "Updates" => L10n.T("Check for Updates", "التحقق من التحديثات"),
                "Log" => L10n.T("Open Log File", "فتح ملف السجل"),
                "Unpin" => L10n.T("Unpin from desktop", "إلغاء التثبيت من سطح المكتب"),
                "Exit" => L10n.T("Exit", "خروج"),
                _ => item.Text,
            };
        }
    }

    public async void CheckForUpdates()
    {
        var repo = SettingsService.Current.UpdateRepo;
        var (update, detail) = await UpdateChecker.CheckAsync(repo);
        if (_tray != null)
            _tray.ShowBalloonTip(3000, "Quran Widget", detail, WinForms.ToolTipIcon.Info);
        if (update && _tray != null)
            _tray.Text = $"Quran Widget — {detail}";
    }

    void OpenLog()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogPath(),
                UseShellExecute = true,
            });
        }
        catch
        {
            // log may not exist yet
        }
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var w in _widgets.ToList())
            {
                try { w.ClampToScreen(); }
                catch { /* best effort */ }
            }
        });
    }

    // Recovery hatch: if a pinned widget ever ends up invisible on the desktop,
    // this restores every widget to a normal window.
    void UnpinAll()
    {
        SettingsService.Current.PinToDesktop = false;
        SettingsService.Save();
        foreach (var w in _widgets.ToList())
        {
            try
            {
                WindowEffects.UnpinFromDesktop(w.Handle);
                WindowEffects.EnableAcrylic(w);
                if (w.IsDaily) ShowDaily();
            }
            catch
            {
                // best effort
            }
        }
    }

    public void ShowDaily()
    {
        var daily = _widgets.FirstOrDefault(w => w.IsDaily);
        if (daily == null)
        {
            daily = new MainWindow("daily", daily: true);
            _widgets.Add(daily);
        }
        daily.ShowAndResetDaily();
    }

    public void CreateWidget()
    {
        if (_widgets.Count(w => !w.IsDaily) >= 5) return;
        _extraCount++;
        var w = new MainWindow("w" + _extraCount, daily: false);
        _widgets.Add(w);
        w.Show();
    }

    HifzWindow? _hifz;

    // Opens (or focuses) the memorization window; it outlives individual widgets.
    public void ShowHifz()
    {
        if (_hifz == null)
        {
            _hifz = new HifzWindow();
            _hifz.Closed += (_, _) => _hifz = null;
        }
        _hifz.Show();
        _hifz.Activate();
    }

    public void WidgetClosed(MainWindow w)
    {
        _widgets.Remove(w);
        SettingsService.Current.Windows.Remove(w.Id);
        SettingsService.Save();
    }

    public void ExitApp()
    {
        if (Exiting) return;
        Exiting = true;
        SettingsService.Save();
        HifzStore.Close();
        _pipeCts?.Cancel();
        foreach (var w in _widgets.ToList()) w.Close();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Shutdown(0);
    }

    void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream("QuranWidget.Pipe", PipeDirection.In);
                    await server.WaitForConnectionAsync(token);
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync(token);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (line == "show") ShowDaily();
                        else if (line == "new") CreateWidget();
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(1000, token); }
                    catch { break; }
                }
            }
        }, token);
    }

    static string LogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuranWidget");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "log.txt");
    }
}

