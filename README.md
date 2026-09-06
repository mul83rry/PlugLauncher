# PlugLauncher

لانچر سبک ویندوزی که همه‌ی قابلیت‌هایش از طریق **پلاگین** اضافه می‌شود. پلاگین‌ها اسکریپت
`.csx` هستند: بدون build، بدون DLL، فقط یک پوشه که داخل `plugins/` گذاشته می‌شود.

```
Alt+Space  →  پنجره باز می‌شود
(خالی)     →  لیست پلاگین‌ها به ترتیب آخرین استفاده + دکمه‌ی تنظیمات
تایپ کنید  →  نتایج پلاگین‌ها
Enter      →  اجرا | Tab: تکمیل خودکار | ↑↓: انتخاب | Esc: بستن
```

## ساختار پروژه

| پروژه | نقش |
|---|---|
| `src/PlugLauncher.Contracts` | قرارداد پلاگین (`IPlugin`, `PluginResult`, `PluginQuery`, `IPluginContext`) — تنها چیزی که نویسنده‌ی پلاگین می‌بیند |
| `src/PlugLauncher.Core` | کشف پلاگین، کامپایل CSX با Roslyn + کش، اجرای کوئری، آمار استفاده، تنظیمات |
| `src/PlugLauncher.App` | رابط کاربری WPF: پنجره‌ی شیشه‌ای، هات‌کی سراسری، صفحه‌ی تنظیمات، آیکن سینی |

## مسیرها

| چیست | کجاست |
|---|---|
| پلاگین‌های کاربر | `%APPDATA%\PlugLauncher\plugins\` |
| پلاگین‌های همراه برنامه | `plugins\` کنار فایل اجرایی |
| تنظیمات | `%APPDATA%\PlugLauncher\settings.json` |
| آمار استفاده | `%APPDATA%\PlugLauncher\usage.json` |
| کش کامپایل اسکریپت‌ها | `%APPDATA%\PlugLauncher\cache\scripts\` |
| لاگ | `%APPDATA%\PlugLauncher\logs\plugLauncher.log` |
| تم اختیاری کاربر | `%APPDATA%\PlugLauncher\theme\theme.xaml` |

## نوشتن یک پلاگین

```
my-plugin/
  plugin.json     # مانیفست
  main.csx        # کد
  assets/         # آیکن‌ها
```

**plugin.json**

```json
{
  "id": "com.you.my-plugin",
  "name": "پلاگین من",
  "description": "توضیح کوتاه",
  "version": "1.0.0",
  "entry": "main.csx",
  "keywords": [ "mp" ],
  "icon": "assets/icon.png"
}
```

- `keywords` پر باشد → پلاگین فقط وقتی صدا زده می‌شود که کوئری با آن کلیدواژه شروع شود (`mp چیزی`).
- `keywords` خالی باشد → پلاگین **سراسری** است و برای هر کوئری صدا زده می‌شود (مثل `calculator`).

**main.csx** — آخرین خط باید یک `IPlugin` برگرداند:

```csharp
return Plugin.Create(query =>
{
    var term = query.Search.Trim();   // متن بعد از کلیدواژه

    return new[]
    {
        new PluginResult
        {
            Id       = "unique-row-id",         // برای ثبت آمار استفاده
            Title    = $"سلام {term}",
            Subtitle = "خط دوم ردیف",
            IconPath = "assets/icon.png",       // نسبی به پوشه‌ی پلاگین یا مسیر مطلق
            Score    = 100,                     // بیشتر = بالاتر در لیست
            Action   = () => Process.Start(new ProcessStartInfo("https://example.com")
                             { UseShellExecute = true })
        }
    };
});
```

نسخه‌ی ناهمگام با مقداردهی اولیه هم هست:

```csharp
return Plugin.Create(
    query: async (q, ct) => { /* ... */ },
    initialize: async (ctx, ct) => { ctx.Log.Info(ctx.PluginDirectory); });
```

### نکته‌های عملی

- **کش کامپایل**: خروجی کامپایل هر پلاگین در `cache\scripts` نگه داشته می‌شود و کلیدش هش
  فایل‌های `.csx` + `plugin.json` است. با ویرایش کد، هش عوض می‌شود و خودکار دوباره کامپایل می‌شود.
- **ایزوله‌سازی**: خطا یا کندی یک پلاگین فقط خودش را از نتایج حذف می‌کند (`queryTimeoutMs`
  پیش‌فرض ۳ ثانیه) و در لاگ ثبت می‌شود.
- **مرجع‌ها**: کل framework و `PlugLauncher.Contracts` به اسکریپت داده می‌شود؛ DLL اضافه را
  در `references` مانیفست بگذارید.
- **خطای کامپایل** با شماره‌ی خط در صفحه‌ی تنظیمات، جلوی همان پلاگین، نمایش داده می‌شود.

## اجرا

```bash
dotnet build PlugLauncher.slnx
dotnet run --project src/PlugLauncher.App
```

فهرست کارهای انجام‌شده و باقی‌مانده در [TASKS.md](TASKS.md).
