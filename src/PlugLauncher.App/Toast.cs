using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace PlugLauncher.App;

/// <summary>
/// یک پیام کوچک گوشه‌ی صفحه که خودش می‌رود.
///
/// جای بالنِ آیکن سینی را گرفته. بالن چیزی است که فقط ویندوز دارد و کتابخانه‌ی پنجره‌ها هم
/// راهی برای نشان دادنش نمی‌دهد؛ یک پنجره‌ی کوچک از خودمان روی هر سه سیستم یکسان کار می‌کند و
/// برخلاف بالن، تا وقتی لازم است می‌ماند و می‌شود رویش کلیک کرد.
///
/// فوکوس نمی‌گیرد: خبر دادن نباید کارِ دستِ کاربر را قطع کند.
/// </summary>
internal sealed class Toast : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private readonly Action? _clicked;
    private readonly DispatcherTimer _timer;

    private Toast(string title, string message, Action? clicked)
    {
        _clicked = clicked;

        Width = 360;
        SizeToContent = SizeToContent.Height;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Content = new Border
        {
            Background = Resource("PanelBrush", Color.FromRgb(0x23, 0x23, 0x23)),
            BorderBrush = Resource("SeparatorBrush", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 13),
            Cursor = clicked is null ? Cursor.Default : new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 13.5,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Resource("TextBrush", Color.FromRgb(0xF2, 0xF2, 0xF2))
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Resource("SubtleTextBrush", Color.FromRgb(0x9A, 0x9A, 0x9A))
                    }
                }
            }
        };

        _timer = new DispatcherTimer { Interval = Lifetime };
        _timer.Tick += (_, _) => Close();

        PointerPressed += OnPressed;

        // ارتفاع از روی متن درمی‌آید و متن هنوز چیده نشده؛ هربار که اندازه عوض شد دوباره
        // سرِ جایش می‌نشیند، وگرنه بارِ اول از پایینِ صفحه می‌زند بیرون.
        SizeChanged += (_, _) => Place();
    }

    public static void Show(string title, string message, Action? clicked = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, message, clicked));
            return;
        }

        new Toast(title, message, clicked).Show();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Place();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var clicked = _clicked;
        Close();
        clicked?.Invoke();
    }

    /// <summary>گوشه‌ی پایین-راست نمایشگر اصلی، همان‌جایی که آدم عادت دارد خبرها بیایند.</summary>
    private void Place()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Width * scale);
        var height = (int)(ClientSize.Height * scale);

        var margin = (int)(16 * scale);
        Position = new PixelPoint(
            area.X + area.Width - width - margin,
            area.Y + area.Height - height - margin);
    }

    private static IBrush Resource(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
