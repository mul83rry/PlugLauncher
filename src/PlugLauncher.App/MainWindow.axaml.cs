using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PlugLauncher.Core;
using PlugLauncher.Platform;

namespace PlugLauncher.App;

public partial class MainWindow : Window
{
    private readonly PluginEngine _engine;
    private readonly FileLogger _log = new("window");
    private readonly IHotkeys _hotkey = Os.CreateHotkeys();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _refresh;

    /// <summary>تا این لحظه اجازه‌ی تازه‌سازی خودکار هست؛ با هر تغییرِ متن از نو شارژ می‌شود.</summary>
    private DateTime _refreshUntil = DateTime.MinValue;

    private CancellationTokenSource? _queryCts;
    private bool _hotkeyTried;

    private ClipboardBridge? _clipboard;
    private SettingsWindow? _settings;

    /// <summary>متن اصلاح‌شده‌ی چیدمان کیبورد برای کوئری فعلی، اگر نتایج از روی آن آمده باشند.</summary>
    private string? _layoutFix;

    /// <summary>به‌روزرسانی پیداشده در استارتاپ، تا تنظیمات مجبور نشود دوباره از گیت‌هاب بپرسد.</summary>
    private UpdateInfo? _knownUpdate;

    public MainWindow(PluginEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        // شیشه‌ای، و اگر سیستم بلد نبود شیشه‌ی مات‌تر، و اگر آن هم نه فقط شفاف. ترتیب مهم است:
        // اولین چیزی که سیستم بتواند برمی‌دارد.
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent
        ];

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

        Deactivated += (_, _) => HideLauncher();


        SearchBox.AddHandler(TextBox.PastingFromClipboardEvent, OnSearchPaste, RoutingStrategies.Bubble);

        // Tunnel، وگرنه Tab را سیستمِ جابه‌جایی فوکوس قبل از ما برمی‌دارد
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);

        // یک کلیک داخل متنِ نما، فوکوس را از باکس جستجو می‌گیرد و بعدش Esc و تایپ به همان باکس
        // نمی‌رسد. هر کلیدی که اینجا فرود بیاید، فوکوس را پس می‌گیرد؛ خود کلید خورده می‌شود ولی
        // بعدی درست جایی می‌نشیند که کاربر انتظار دارد.
        DetailPanel.AddHandler(KeyDownEvent, OnDetailKeyDown, RoutingStrategies.Tunnel);

        ResultsList.DoubleTapped += async (_, _) => await ActivateSelectedAsync();
    }

    internal void UseClipboard(ClipboardBridge clipboard) => _clipboard = clipboard;

    /// <summary>
    /// یک جعبه‌ی تک‌خطی از چند خطِ پیست‌شده فقط خط اول را نگه می‌دارد و بقیه را بی‌صدا دور می‌ریزد.
    /// دستور curlِ چندخطی، مسیرِ کپی‌شده از یک لاگ، کوئریِ SQL: چیزی که آدم پیست می‌کند اغلب
    /// بیش از یک خط است، پس خطوط را به هم می‌چسبانیم تا هیچ‌چیز گم نشود.
    /// </summary>
    private async void OnSearchPaste(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        if (clipboard is null) return;

        // قبل از هر await، وگرنه پیستِ پیش‌فرض زودتر انجام شده است
        e.Handled = true;

        string text;
        try
        {
            text = await clipboard.TryGetTextAsync() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _log.Warn($"could not read the clipboard: {ex.Message}");
            return;
        }

        if (text.Length == 0) return;

        var flat = text.Replace('\r', '\n').Replace('\n', ' ').Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");

        var current = SearchBox.Text ?? string.Empty;
        var from = Math.Clamp(Math.Min(SearchBox.SelectionStart, SearchBox.SelectionEnd), 0, current.Length);
        var to = Math.Clamp(Math.Max(SearchBox.SelectionStart, SearchBox.SelectionEnd), 0, current.Length);

        SearchBox.Text = current[..from] + flat + current[to..];
        SearchBox.CaretIndex = from + flat.Length;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // اگر سیستم شفافیت را برنداشت، پس‌زمینه‌ی تخت. بدون این، پنجره‌ی نیمه‌شفاف روی چیزی که
        // پشتش نیست کشیده می‌شود و سیاه درمی‌آید.
        if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        if (_hotkeyTried) return;
        _hotkeyTried = true;

        var typed = _engine.Settings.Hotkey;

        if (!Hotkey.TryParse(typed, out var hotkey))
        {
            Complain(typed, $"\"{typed}\" is not a key combination the launcher understands");
            return;
        }

        var registered = _hotkey.TryRegister(hotkey, () =>
        {
            _log.Info("hotkey pressed");
            ToggleLauncher();
        }, out var problem);

        if (registered) _log.Info($"hotkey \"{hotkey}\" registered");
        else Complain(typed, problem);
    }

    /// <summary>
    /// هات‌کی که ثبت نشود یعنی برنامه‌ای که هیچ راهی برای باز شدن ندارد، پس این یکی از معدود
    /// جاهایی است که ارزش دارد جلوی کاربر بایستد.
    /// </summary>
    private void Complain(string typed, string problem)
    {
        _log.Error($"could not register hotkey \"{typed}\": {problem}");
        Dialog.Info("PlugLauncher", $"Could not register the hotkey \"{typed}\" — {problem}.");
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
        _clipboard?.Refresh();
        ShowDefaultRows();
        PositionOnActiveScreen();

        Show();
        Activate();
        Topmost = true;

        // فوکوس گرفتنِ برنامه‌ای که فورگراند نیست روی هر سیستم قانون خودش را دارد؛ اگر لایه‌ی
        // پلتفرم بلد نباشد، همان Activate بالا تنها چیزی است که هست.
        Os.Windowing.TryFocus(TryGetPlatformHandle()?.Handle ?? 0);

        SearchBox.Focus();

        _log.Info($"showing window: visible={IsVisible} active={IsActive} " +
                  $"left={Position.X} top={Position.Y} width={Width:F0} height={Height:F0}");
    }

    public void HideLauncher()
    {
        CloseDetail();
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
        var screen = Os.Windowing.TryCursor(out var x, out var y)
            ? Screens.ScreenFromPoint(new PixelPoint(x, y))
            : null;

        screen ??= Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;

        // جای پنجره به پیکسل شمرده می‌شود ولی عرضش به واحد مستقل از تراکم صفحه، پس یکی از دو
        // طرف باید تبدیل شود
        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);

        Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + (int)(area.Height * 0.2));
    }

    // ===== جستجو =====

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var typed = SearchBox.Text ?? string.Empty;

        SettingsButton.IsVisible = typed.Length == 0;

        _debounce.Stop();

        // هر تغییرِ متن یک کوئری تازه است، پس بودجه‌ی تازه‌سازی هم از نو شروع می‌شود
        _refresh.Stop();
        _refreshUntil = DateTime.UtcNow + RefreshBudget;

        if (string.IsNullOrWhiteSpace(typed))
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
        var query = SearchBox.Text ?? string.Empty;

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
        // کوئری جدید یعنی ردیف‌های جدید؛ نمای ردیف قبلی دیگر به چیزی اشاره نمی‌کند
        CloseDetail();
        ResultsList.ItemsSource = rows;
        ResultsList.SelectedIndex = rows.Count > 0 ? 0 : -1;
        ResultsList.IsVisible = rows.Count > 0;
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

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not null) ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        UpdateHint();
    }

    private void UpdateHint()
    {
        var typed = SearchBox.Text ?? string.Empty;
        HintTyped.Text = typed;
        HintRest.Text = string.Empty;

        // چیدمان کیبورد اشتباه بوده: به‌جای تکمیل، خوانشِ درست همان کلیدها نشان داده می‌شود
        if (!string.IsNullOrEmpty(_layoutFix))
        {
            HintRest.Text = "   →  " + _layoutFix;
            return;
        }

        if (ResultsList.SelectedItem is not LauncherRow row || typed.Length == 0) return;

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
            Replace(_layoutFix);
            return;
        }

        // روی یک ردیف راهنما، Tab همان کاری را می‌کند که Enter — متن پیشنهادی را می‌گذارد و
        // پنجره باز می‌ماند. این‌طور حتی وقتی سایه نشان داده نشده هم پذیرفتنِ پیشنهاد کار می‌کند.
        if (ResultsList.SelectedItem is LauncherRow row && row.Item?.Result.ReplaceQuery is { Length: > 0 } suggestion)
        {
            Replace(suggestion);
            return;
        }

        if (string.IsNullOrEmpty(HintRest.Text)) return;

        Replace(HintTyped.Text + HintRest.Text);
    }

    private void Replace(string text)
    {
        SearchBox.Text = text;
        SearchBox.CaretIndex = text.Length;
    }

    // ===== کیبورد =====

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (DetailPanel.IsVisible) CloseDetail();
                else HideLauncher();
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
        var count = ResultsList.ItemCount;
        if (count == 0) return;

        var next = ResultsList.SelectedIndex + delta;
        if (next < 0) next = count - 1;
        if (next >= count) next = 0;

        ResultsList.SelectedIndex = next;
    }

    private async Task ActivateSelectedAsync()
    {
        if (ResultsList.SelectedItem is not LauncherRow row) return;

        // ردیف پلاگین در حالت پیش‌فرض: کلیدواژه‌اش را در باکس می‌گذاریم
        if (row.Plugin is not null)
        {
            var keyword = row.Plugin.Manifest.Keywords.FirstOrDefault();
            Replace(string.IsNullOrEmpty(keyword) ? string.Empty : keyword + " ");
            return;
        }

        if (row.Item is null) return;

        // ردیف راهنما: به‌جای اجرا، نحوِ پیشنهادی را در باکس می‌گذارد و پنجره باز می‌ماند
        if (!string.IsNullOrEmpty(row.Item.Result.ReplaceQuery))
        {
            Replace(row.Item.Result.ReplaceQuery);
            SearchBox.Focus();
            return;
        }

        if (row.HasDetail)
        {
            ShowDetail(row);
            return;
        }

        var shouldHide = await _engine.ExecuteAsync(row.Item);
        if (shouldHide) HideLauncher();
    }

    /// <summary>متن نمای فعلی، قبل از رنگ‌آمیزی — دکمه‌ی کپی این را می‌دهد بیرون، هر نمای که باز است.</summary>
    private string _detailText = string.Empty;

    /// <summary>
    /// متنِ یک ردیف را در نما نشان می‌دهد، با کپی در گوشه‌ی راست. متنِ بدون زبان در تکست‌باکس
    /// می‌نشیند؛ متنِ json به Runهای رنگی می‌شکند تا مثل ادیتور خوانده شود. فوکوس در باکس
    /// جستجو می‌ماند: Esc نما را می‌بندد و پنجره باز می‌ماند، و تایپِ بیشتر همان کوئری را ادامه
    /// می‌دهد و نما را خودکار می‌بندد.
    /// </summary>
    private void ShowDetail(LauncherRow row)
    {
        DetailCaption.Text = string.IsNullOrWhiteSpace(row.DetailTitle) ? row.Title : row.DetailTitle!;
        _detailText = row.DetailText!;

        var colored = string.Equals(row.DetailSyntax, "json", StringComparison.OrdinalIgnoreCase)
                      && row.DetailText!.Length <= ColoredDetailLimit;

        if (colored) FillColoredDetail(row.DetailText!);
        else DetailTextBox.Text = row.DetailText;

        DetailScroll.IsVisible = colored;
        DetailTextBox.IsVisible = !colored;

        ResultsList.IsVisible = false;
        DetailPanel.IsVisible = true;

        // اسکرول از بالا؛ رندرِ قبلی ممکن است تهِ متن بلند مانده باشد
        DetailTextBox.CaretIndex = 0;
        DetailTextBox.SelectionStart = DetailTextBox.SelectionEnd = 0;
        DetailScroll.Offset = Vector.Zero;
    }

    private void OnDetailKeyDown(object? sender, KeyEventArgs e)
    {
        if (!DetailPanel.IsVisible) return;

        e.Handled = true;
        SearchBox.Focus();

        if (e.Key == Key.Escape) CloseDetail();
    }

    private void CloseDetail()
    {
        if (!DetailPanel.IsVisible) return;

        DetailPanel.IsVisible = false;
        DetailTextBox.Clear();
        DetailRich.Inlines?.Clear();
        _detailText = string.Empty;
        ResultsList.IsVisible = ResultsList.ItemCount > 0;
    }

    private async void OnDetailCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_detailText.Length == 0) return;

        // مستقیم از کلیپ‌بوردِ همین پنجره، نه پلِ پلاگین‌ها — اینجا خودِ میزبان در حال کپی است.
        // متنِ خام را می‌دهد بیرون نه رنگ‌ها را؛ رنگ مالِ خواندن است.
        try
        {
            if (Clipboard is not null) await Clipboard.SetTextAsync(_detailText);

            var old = DetailCopyButton.Content;
            DetailCopyButton.Content = "Copied";
            await Task.Delay(900);
            DetailCopyButton.Content = old;
        }
        catch (Exception ex)
        {
            _log.Warn($"could not copy the detail text: {ex.Message}");
        }

        // فوکوس همین‌جا به باکس برمی‌گردد. کلیک روی دکمه فوکوس را می‌گیرد و همه‌ی کلیدهای لانچر —
        // Esc، تایپِ بیشتر، همه — به باکس گره خورده‌اند؛ روی دکمه هیچ‌کدام معنایی ندارد.
        SearchBox.Focus();
    }

    /// <summary>خروج از رشته قبل از نوشتنش همیشه یک علامت است، حتی وقتی ماندهٔ متن تمام شده باشد.</summary>
    private const char Backslash = '\\';

    // ===== رنگ‌آمیزی JSON =====

    /// <summary>
    /// بالای این طول، رنگ‌آمیزی خاموش می‌شود و متن در همان تکست‌باکس ساده می‌نشیند: هر تکه‌ی رنگ
    /// یک Run است و بدنه‌ی بزرگ هزاران Run یعنی رندری که دیر می‌آید، برای چیزی که کاربر یک خطش
    /// را می‌خواهد.
    /// </summary>
    private const int ColoredDetailLimit = 20_000;

    /// <summary>رنگ‌ها همان پالت Dark+ ‌اند — چیزی که VS Code برای JSON می‌کشد.</summary>
    private static readonly IReadOnlyDictionary<JsonTokenKind, IBrush> JsonBrushes =
        new Dictionary<JsonTokenKind, IBrush>
        {
            [JsonTokenKind.Key] = new SolidColorBrush(Color.Parse("#9CDCFE")),
            [JsonTokenKind.String] = new SolidColorBrush(Color.Parse("#CE9178")),
            [JsonTokenKind.Number] = new SolidColorBrush(Color.Parse("#B5CEA8")),
            [JsonTokenKind.Literal] = new SolidColorBrush(Color.Parse("#569CD6"))
        };

    private enum JsonTokenKind { Plain, Key, String, Number, Literal }

    private void FillColoredDetail(string text)
    {
        DetailRich.Inlines!.Clear();

        foreach (var (chunk, kind) in SplitJson(text))
        {
            var run = new Avalonia.Controls.Documents.Run(chunk);
            if (JsonBrushes.TryGetValue(kind, out var brush)) run.Foreground = brush;
            DetailRich.Inlines.Add(run);
        }
    }

    /// <summary>
    /// متن را به تکه‌های رنگ‌پذیر می‌شکند. چیزی که رنگ نمی‌گیرد (آکولاد، دونقطه، ویرگول، فاصله)
    /// در تکه‌ی Plain می‌ماند و رنگ خودِ تم را می‌گیرد — در ادیتور هم علامت‌گذاری رنگ نیست، متنِ
    /// معمولی است. کلید را از رشته‌ی مقدار، فقطِ یک نگاهِ به جلو جدا می‌کند: رشته‌ای که بعدش
    /// دونقطه است کلید است.
    /// </summary>
    private static IEnumerable<(string Text, JsonTokenKind Kind)> SplitJson(string text)
    {
        var chunks = new List<(string, JsonTokenKind)>();
        var plain = new StringBuilder();

        void Flush()
        {
            if (plain.Length > 0)
            {
                chunks.Add((plain.ToString(), JsonTokenKind.Plain));
                plain.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                var end = i + 1;
                while (end < text.Length)
                {
                    if (text[end] == Backslash) { end += 2; continue; }
                    if (text[end] == '"') { end++; break; }
                    end++;
                }

                var after = end;
                while (after < text.Length && char.IsWhiteSpace(text[after])) after++;

                Flush();
                chunks.Add((text[i..Math.Min(end, text.Length)],
                            after < text.Length && text[after] == ':' ? JsonTokenKind.Key : JsonTokenKind.String));
                i = Math.Min(end, text.Length) - 1;
            }
            else if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var end = i + 1;
                while (end < text.Length && (char.IsDigit(text[end]) || "+-.eE".IndexOf(text[end]) >= 0)) end++;

                Flush();
                chunks.Add((text[i..end], JsonTokenKind.Number));
                i = end - 1;
            }
            else if (char.IsLetter(c))
            {
                var end = i + 1;
                while (end < text.Length && char.IsLetter(text[end])) end++;

                var word = text[i..end];
                Flush();
                chunks.Add((word, word is "true" or "false" or "null" ? JsonTokenKind.Literal : JsonTokenKind.Plain));
                i = end - 1;
            }
            else
            {
                plain.Append(c);
            }
        }

        Flush();
        return chunks;
    }
    // ===== تنظیمات =====

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// نقطه‌ی کنار «Settings» تنها نشانه‌ی ماندگار به‌روزرسانی است — پیام گوشه‌ی صفحه چند ثانیه
    /// بعد می‌رود و ممکن است اصلاً دیده نشود. جزئیاتش داخل خود تنظیمات است، چون پنجره‌ی لانچر
    /// باید ساکت بماند.
    /// </summary>
    public void MarkUpdateAvailable(UpdateInfo update)
    {
        _knownUpdate = update;
        SettingsButton.Content = "Settings ●";
        ToolTip.SetTip(SettingsButton, $"PlugLauncher {update.Version.ToString(3)} is available");
    }

    /// <summary>
    /// تنظیمات مودال نیست و لانچر هم موقع باز شدنش کنار می‌رود.
    ///
    /// پنجره‌ی مودال به یک صاحبِ دیده‌شده نیاز دارد و لانچر اغلب پنهان است — مثلاً وقتی از منوی
    /// سینی باز می‌شود. و لانچر همیشه روی همه‌چیز است، پس اگر بماند دقیقاً روی تنظیمات می‌نشیند.
    /// </summary>
    public void OpenSettings()
    {
        HideLauncher();

        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_engine, _knownUpdate);
        _settings.Closed += (_, _) => _settings = null;

        _settings.Show();
        _settings.Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey.Dispose();
        _engine.Usage.Flush();
        base.OnClosed(e);
    }
}
