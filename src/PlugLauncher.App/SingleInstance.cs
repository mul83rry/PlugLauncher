using System.IO;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>
/// فقط یک نسخه از برنامه در آنِ واحد.
///
/// یک فایل قفل، نه <c>Mutex</c> نام‌دار: mutex نام‌دار چیزی است که فقط ویندوز دارد. باز نگه
/// داشتن یک فایل به‌صورت انحصاری روی هر سه سیستم کار می‌کند و مزیت دیگری هم دارد — اگر برنامه
/// خراب شود، سیستم‌عامل خودش فایل را رها می‌کند و نسخه‌ی بعدی گیر نمی‌افتد.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private FileStream? _held;

    public bool TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(PluginPaths.UserRoot);

            _held = new FileStream(
                Path.Combine(PluginPaths.UserRoot, "running.lock"),
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None);

            return true;
        }
        catch (IOException)
        {
            // نسخه‌ی دیگری همین الان بازش نگه داشته است
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _held?.Dispose();
        _held = null;
    }
}
