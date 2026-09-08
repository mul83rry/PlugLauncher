using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>
/// کلیپ‌بورد سیستم، آن‌طور که یک پلاگین می‌تواند از آن استفاده کند.
///
/// دو ناهماهنگی اینجا حل می‌شود: کلیپ‌بوردِ کتابخانه‌ی پنجره‌ها فقط ناهم‌زمان جواب می‌دهد و فقط
/// از ترد UI قابل صدا زدن است، ولی <see cref="Contracts.Clipboard"/> هم‌زمان است و از هر تردی
/// صدا زده می‌شود — چون امضایی که پلاگین‌ها می‌بینند باید ساده بماند.
/// </summary>
internal sealed class ClipboardBridge(TopLevel host, FileLogger log)
{
    /// <summary>سقف انتظار برای یک خواندن. کلیپ‌بوردی که جواب نمی‌دهد نباید پلاگین را بخواباند.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    /// <summary>آخرین متنی که خوانده‌ایم؛ جوابِ صداکننده‌ای که خودش روی ترد UI است.</summary>
    private string _text = string.Empty;

    public void Install() => Contracts.Clipboard.Use(Copy, Read);

    /// <summary>
    /// هربار که پنجره باز می‌شود صدا زده می‌شود. ترتیب کار کاربر همیشه یکی است — چیزی را کپی
    /// می‌کند و بعد لانچر را باز می‌کند — پس همین یک نقطه کافی است تا نسخه‌ی نگه‌داشته تازه باشد.
    /// </summary>
    public void Refresh() => Dispatcher.UIThread.Post(() => _ = ReadAsync());

    private void Copy(string text)
    {
        var value = text ?? string.Empty;
        _text = value;

        Dispatcher.UIThread.Post(() => _ = WriteAsync(value));
    }

    private string Read()
    {
        // روی ترد UI نمی‌شود منتظر ماند — همان ترد است که باید جواب را بیاورد. نسخه‌ای که موقع
        // باز شدن پنجره خوانده شده همان چیزی است که کاربر یک لحظه پیش کپی کرده.
        if (Dispatcher.UIThread.CheckAccess()) return _text;

        var answer = new TaskCompletionSource<string>();
        Dispatcher.UIThread.Post(async () => answer.TrySetResult(await ReadAsync()));

        return answer.Task.Wait(Patience) ? answer.Task.Result : _text;
    }

    /// <summary>همیشه روی ترد UI: کلیپ‌بورد ویندوز فقط از تردی که پنجره‌ها را دارد جواب می‌دهد.</summary>
    private async Task<string> ReadAsync()
    {
        var clipboard = host.Clipboard;
        if (clipboard is null) return _text;

        try
        {
            _text = await clipboard.TryGetTextAsync() ?? string.Empty;
        }
        catch (Exception ex)
        {
            log.Warn($"could not read the clipboard: {ex.Message}");
        }

        return _text;
    }

    private async Task WriteAsync(string text)
    {
        var clipboard = host.Clipboard;
        if (clipboard is null)
        {
            log.Warn("could not put text on the clipboard: there is no clipboard");
            return;
        }

        // کلیپ‌بورد گاهی دست برنامه‌ی دیگری است و همان لحظه جواب نمی‌دهد؛ همان حلقه‌ی «سه بار
        // تلاش» که قبلاً در هفت اسکریپت کپی شده بود، حالا فقط اینجاست.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                return;
            }
            catch
            {
                await Task.Delay(40);
            }
        }

        log.Warn("could not put text on the clipboard");
    }
}
