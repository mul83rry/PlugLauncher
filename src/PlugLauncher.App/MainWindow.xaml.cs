using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PlugLauncher.App.Interop;
using PlugLauncher.Core;

namespace PlugLauncher.App;

public partial class MainWindow : Window
{
    private readonly PluginEngine _engine;
    private readonly FileLogger _log = new("window");
    private readonly GlobalHotkey _hotkey = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _refresh;

    /// <summary>تا این لحظه اجازه‌ی تازه‌سازی خودکار هست؛ با هر تغییرِ متن از نو شارژ می‌شود.</summary>
    private DateTime _refreshUntil = DateTime.MinValue;

    private CancellationTokenSource? _queryCts;
    private bool _suppressHideOnDeactivate;

    /// <summary>متن اصلاح‌شده‌ی چیدمان کیبورد برای کوئری فعلی، اگر نتایج از روی آن آمده باشند.</summary>
    private string? _layoutFix;

    /// <summary>به‌روزرسانی پیداشده در استارتاپ، تا تنظیمات مجبور نشود دوباره از گیت‌هاب بپرسد.</summary>
    private UpdateInfo? _knownUpdate;

    public MainWindow(PluginEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        _debounce = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RunQueryAsync();
        };

        _refresh = new DispatcherTimer(DispatcherPriority.Background);
        _refresh.Tick += async (_, _) =>
        {
            _refresh.Stop();
            if (!IsVisible) return;
            await RunQueryAsync();
        };

        Deactivated += (_, _) =>
        {
            if (!_suppressHideOnDeactivate) HideLauncher();
        };

        DataObject.AddPastingHandler(SearchBox, OnSearchPaste);
    }

    /// <summary>
    /// یک جعبه‌ی تک‌خطی از چند خطِ پیست‌شده فقط خط اول را نگه می‌دارد و بقیه را بی‌صدا دور می‌ریزد.
    /// دستور curlِ چندخطی، مسیرِ کپی‌شده از یک لاگ، کوئریِ SQL: چیزی که آدم پیست می‌کند اغلب
    /// بیش از یک خط است، پس خطوط را به هم می‌چسبانیم تا هیچ‌چیز گم نشود.
    /// </summary>
    private void OnSearchPaste(object sender, DataObjectPastingEventArgs e)
    {
        var format = e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true) ? DataFormats.UnicodeText
                   : e.SourceDataObject.GetDataPresent(DataFormats.Text, true) ? DataFormats.Text
                   : null;
        if (format is null) return;

        if (e.SourceDataObject.GetData(format, true) is not string text) return;
        if (!text.Contains('\n') && !text.Contains('\r')) return;

        var flat = text.Replace('\r', '\n').Replace('\n', ' ').Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");

        var single = new DataObject();
        single.SetData(DataFormats.UnicodeText, flat);
        e.DataObject = single;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        WindowEffects.TryApplyAcrylic(this);

        _hotkey.Pressed += (_, _) =>
        {
            _log.Info("hotkey pressed");
            ToggleLauncher();
        };

        if (_hotkey.Register(this, _engine.Settings.Hotkey))
        {
            _log.Info($"hotkey \"{_engine.Settings.Hotkey}\" registered");
        }
        else
        {
            _log.Error($"could not register hotkey \"{_engine.Settings.Hotkey}\"");
            MessageBox.Show(
                $"Could not register the hotkey \"{_engine.Settings.Hotkey}\" — another application probably owns it.",
                "PlugLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ===== نمایش / مخفی‌سازی =====

    public void ToggleLauncher()
    {
        if (IsVisible && IsActive) HideLauncher();
        else ShowLauncher();
    }

    public void ShowLauncher()
    {
        SearchBox.Text = string.Empty;
        ShowDefaultRows();
        PositionOnActiveScreen();

        Show();
        Activate();
        Topmost = true;
        ForegroundHelper.ForceForeground(this);

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        _log.Info($"showing window: visible={IsVisible} active={IsActive} left={Left:F0} top={Top:F0} " +
                  $"width={ActualWidth:F0} height={ActualHeight:F0}");
    }

    public void HideLauncher()
    {
        _debounce.Stop();
        _refresh.Stop();
        _refreshUntil = DateTime.MinValue;
        _queryCts?.Cancel();
        Hide();
        SearchBox.Text = string.Empty;
        ResultsList.ItemsSource = null;
        _layoutFix = null;
    }

    /// <summary>وسط‌چین افقی روی همان نمایشگری که موس در آن است، کمی بالاتر از وسط عمودی.</summary>
    private void PositionOnActiveScreen()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = screen.WorkingArea;

        Left = (area.Left + (area.Width - Width * dpi.DpiScaleX) / 2) / dpi.DpiScaleX;
        Top = (area.Top + area.Height * 0.2) / dpi.DpiScaleY;
    }

    // ===== جستجو =====

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SettingsButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        _debounce.Stop();

        // هر تغییرِ متن یک کوئری تازه است، پس بودجه‌ی تازه‌سازی هم از نو شروع می‌شود
        _refresh.Stop();
        _refreshUntil = DateTime.UtcNow + RefreshBudget;

        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _queryCts?.Cancel();
            _layoutFix = null;
            ShowDefaultRows();
            return;
        }

        _debounce.Start();
    }

    private async Task RunQueryAsync()
    {
        var query = SearchBox.Text;

        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var token = _queryCts.Token;

        QueryOutcome outcome;
        try
        {
            outcome = await _engine.QueryAsync(query, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || !string.Equals(query, SearchBox.Text, StringComparison.Ordinal)) return;

        _layoutFix = outcome.CorrectedQuery;
        SetRows(outcome.Items.Select(LauncherRow.FromResult).ToList());
    }

    /// <summary>حالت پیش‌فرض: پلاگین‌ها به ترتیب آخرین استفاده.</summary>
    private void ShowDefaultRows()
        => SetRows(_engine.PluginsByRecency().Select(LauncherRow.FromPlugin).ToList());

    private void SetRows(IReadOnlyList<LauncherRow> rows)
    {
        ResultsList.ItemsSource = rows;
        ResultsList.SelectedIndex = rows.Count > 0 ? 0 : -1;
        ResultsList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateHint();
        ScheduleRefresh(rows);
    }

    /// <summary>کف فاصله‌ی دو تازه‌سازی خودکار؛ پلاگین نمی‌تواند از این تندتر بخواهد.</summary>
    private const int MinRefreshMs = 250;

    /// <summary>سقف زمانی تازه‌سازی خودکار برای یک متنِ ثابت.</summary>
    private static readonly TimeSpan RefreshBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// اگر ردیفی گفته باشد کارش هنوز تمام نشده (<c>RefreshAfterMs</c>)، همین کوئری خودش دوباره
    /// اجرا می‌شود تا نتیجه‌ی نهایی بدون Enter زدن کاربر بیاید.
    ///
    /// سه سقف جلوی حلقه‌ی بی‌پایان را می‌گیرد: فاصله کف دارد، هر متن دو دقیقه بودجه دارد، و با
    /// بسته شدن پنجره همه‌چیز می‌ایستد. سومی مهم‌ترین است — پنجره فقط وقتی باز است که کاربر
    /// جلویش نشسته، پس هیچ پلاگینی نمی‌تواند در پس‌زمینه بچرخد.
    /// </summary>
    private void ScheduleRefresh(IReadOnlyList<LauncherRow> rows)
    {
        _refresh.Stop();
        if (DateTime.UtcNow > _refreshUntil) return;

        var soonest = rows
            .Select(r => r.Item?.Result.RefreshAfterMs)
            .Where(ms => ms is not null)
            .Select(ms => Math.Max(ms!.Value, MinRefreshMs))
            .DefaultIfEmpty(0)
            .Min();

        if (soonest <= 0) return;

        _refresh.Interval = TimeSpan.FromMilliseconds(soonest);
        _refresh.Start();
    }

    // ===== تکمیل خودکار درون‌خطی =====

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not null) ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        UpdateHint();
    }

    private void UpdateHint()
    {
        var typed = SearchBox.Text;
        HintTyped.Text = typed;
        HintRest.Text = string.Empty;

        // چیدمان کیبورد اشتباه بوده: به‌جای تکمیل، خوانشِ درست همان کلیدها نشان داده می‌شود
        if (!string.IsNullOrEmpty(_layoutFix))
        {
            HintRest.Text = "   →  " + _layoutFix;
            return;
        }

        if (ResultsList.SelectedItem is not LauncherRow row || string.IsNullOrEmpty(typed)) return;

        // ردیف راهنما خودش می‌گوید باکس باید چه بشود، پس حدس زدن لازم نیست. اگر ادامه‌ی چیزی که
        // تایپ شده نباشد (مثلاً کاربر «c:/» نوشته و پیشنهاد «C:\» است) هیچ سایه‌ای نشان نمی‌دهیم؛
        // Tab باز هم کار می‌کند و کل متن را جایگزین می‌کند.
        var suggestion = row.Item?.Result.ReplaceQuery;
        if (!string.IsNullOrEmpty(suggestion))
        {
            HintRest.Text = suggestion.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase)
                ? suggestion[typed.Length..]
                : string.Empty;
            return;
        }

        // آخرین کلمه‌ی تایپ‌شده مبنای تکمیل است
        var lastSpace = typed.LastIndexOf(' ');
        var fragment = lastSpace < 0 ? typed : typed[(lastSpace + 1)..];

        if (fragment.Length > 0 && row.Title.StartsWith(fragment, StringComparison.CurrentCultureIgnoreCase))
            HintRest.Text = row.Title[fragment.Length..];
        else
            HintRest.Text = typed.EndsWith(' ') ? row.Title : " " + row.Title;
    }

    private void CompleteFromHint()
    {
        // با چیدمان اشتباه، Tab خودِ متن باکس را درست می‌کند نه اینکه ادامه‌اش را بچسباند
        if (!string.IsNullOrEmpty(_layoutFix))
        {
            SearchBox.Text = _layoutFix;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            return;
        }

        // روی یک ردیف راهنما، Tab همان کاری را می‌کند که Enter — متن پیشنهادی را می‌گذارد و
        // پنجره باز می‌ماند. این‌طور حتی وقتی سایه نشان داده نشده هم پذیرفتنِ پیشنهاد کار می‌کند.
        if (ResultsList.SelectedItem is LauncherRow row && row.Item?.Result.ReplaceQuery is { Length: > 0 } suggestion)
        {
            SearchBox.Text = suggestion;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            return;
        }

        if (string.IsNullOrEmpty(HintRest.Text)) return;

        SearchBox.Text = HintTyped.Text + HintRest.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    // ===== کیبورد =====

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Tab:
                CompleteFromHint();
                e.Handled = true;
                break;

            case Key.Enter:
                e.Handled = true;
                await ActivateSelectedAsync();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ResultsList.Items.Count == 0) return;

        var next = ResultsList.SelectedIndex + delta;
        if (next < 0) next = ResultsList.Items.Count - 1;
        if (next >= ResultsList.Items.Count) next = 0;

        ResultsList.SelectedIndex = next;
    }

    private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e) => await ActivateSelectedAsync();

    private async Task ActivateSelectedAsync()
    {
        if (ResultsList.SelectedItem is not LauncherRow row) return;

        // ردیف پلاگین در حالت پیش‌فرض: کلیدواژه‌اش را در باکس می‌گذاریم
        if (row.Plugin is not null)
        {
            var keyword = row.Plugin.Manifest.Keywords.FirstOrDefault();
            SearchBox.Text = string.IsNullOrEmpty(keyword) ? string.Empty : keyword + " ";
            SearchBox.CaretIndex = SearchBox.Text.Length;
            return;
        }

        if (row.Item is null) return;

        // ردیف راهنما: به‌جای اجرا، نحوِ پیشنهادی را در باکس می‌گذارد و پنجره باز می‌ماند
        if (!string.IsNullOrEmpty(row.Item.Result.ReplaceQuery))
        {
            SearchBox.Text = row.Item.Result.ReplaceQuery;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            SearchBox.Focus();
            return;
        }

        var shouldHide = await _engine.ExecuteAsync(row.Item);
        if (shouldHide) HideLauncher();
    }

    // ===== تنظیمات =====

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// نقطه‌ی کنار «Settings» تنها نشانه‌ی ماندگار به‌روزرسانی است — بالن سینی چند ثانیه بعد
    /// می‌رود و ممکن است اصلاً دیده نشود. جزئیاتش داخل خود تنظیمات است، چون پنجره‌ی لانچر باید
    /// ساکت بماند.
    /// </summary>
    public void MarkUpdateAvailable(UpdateInfo update)
    {
        _knownUpdate = update;
        SettingsButton.Content = "Settings ●";
        SettingsButton.ToolTip = $"PlugLauncher {update.Version.ToString(3)} is available";
    }

    public void OpenSettings()
    {
        _suppressHideOnDeactivate = true;
        try
        {
            var window = new SettingsWindow(_engine, _knownUpdate) { Owner = IsVisible ? this : null };
            window.ShowDialog();
        }
        finally
        {
            _suppressHideOnDeactivate = false;
        }

        ShowDefaultRows();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey.Dispose();
        _engine.Usage.Flush();
        base.OnClosed(e);
    }
}
