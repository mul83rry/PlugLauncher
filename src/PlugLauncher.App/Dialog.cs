using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PlugLauncher.App;

/// <summary>
/// یک جعبه‌ی پیام ساده. <c>MessageBox</c> چیزی است که ویندوز دارد و بقیه ندارند، پس همان‌قدر
/// که لازم بود اینجا از نو ساخته شده: یک عنوان، یک متن، و یکی دو دکمه.
///
/// عمداً هم‌زمان (blocking) است. کدی که می‌پرسد «این کار را انجام بدهم؟» بدون جواب نمی‌تواند
/// ادامه بدهد، و اگر این متد <c>async</c> بود، هر صداکننده‌ای هم باید می‌شد — از جمله
/// <see cref="Contracts.Ask"/> که پلاگین‌ها صدایش می‌زنند و امضایش قرارداد است.
/// </summary>
internal static class Dialog
{
    public static void Info(string title, string message) => Ask(title, message, "OK", null);

    public static bool Confirm(string title, string question) => Ask(title, question, "Yes", "No");

    private static bool Ask(string title, string message, string accept, string? reject)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Ask(title, message, accept, reject));

        var answer = false;

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brush("WindowBrush", Color.FromRgb(0x1B, 0x1B, 0x1B)),
            MaxWidth = 520,
            // وگرنه پنجره تا اندازه‌ی متن جمع می‌شود و عنوان خودش سه‌نقطه می‌خورد
            MinWidth = 340
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            FontSize = 13,
            Foreground = Brush("TextBrush", Color.FromRgb(0xF2, 0xF2, 0xF2))
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 18, 0, 0),
            Spacing = 6
        };

        if (reject is not null)
        {
            var no = new Button { Content = reject, MinWidth = 76 };
            no.Classes.Add("ghost");
            no.Click += (_, _) => window.Close();
            buttons.Children.Add(no);
        }

        var yes = new Button { Content = accept, MinWidth = 76, IsDefault = true };
        yes.Classes.Add("ghost");
        yes.Click += (_, _) =>
        {
            answer = true;
            window.Close();
        };
        buttons.Children.Add(yes);

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(22, 20, 22, 18),
            Children = { text, buttons }
        };

        // حلقه‌ی پیام تودرتو: همان کاری که ShowDialog در دنیای WPF می‌کرد. بدون این، اجرا از
        // اینجا رد می‌شود و جواب کاربر همیشه دیر می‌رسد.
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;

        window.Show();
        window.Activate();
        Dispatcher.UIThread.PushFrame(frame);

        return answer;
    }

    /// <summary>رنگ از تم برنامه، و اگر تم هنوز بالا نیامده باشد همان رنگ به‌صورت خام.</summary>
    private static IBrush Brush(string key, Color fallback)
        => Avalonia.Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
