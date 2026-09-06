# فروشگاه پلاگین PlugLauncher

## آدرس‌ها

| مورد | مقدار |
|---|---|
| آدرس عمومی | `https://thehokm.cloud/pluglauncher/` |
| سلامت | `https://thehokm.cloud/pluglauncher/health` |
| سرور | `72.61.156.201` (ssh alias `hokm`) |
| پورت داخلی | `127.0.0.1:5100` (فقط لوکال، از بیرون بسته) |

> ساب‌دامین اختصاصی (`store.thehokm.cloud`) عمداً استفاده نشده چون رکورد DNS ندارد و wildcard هم روی دامنه فعال نیست.
> اگر بعداً یک رکورد A به `72.61.156.201` اضافه شود، جابه‌جایی فقط یک `server` بلاک جدید در nginx + `certbot` است.

## فرمت بسته (`.plz`)

یک zip معمولی با این ساختار (پسوند فقط برای تشخیص است):

```
plugin.json      # اجباری، در ریشه‌ی بسته
main.csx         # فایل ورودی؛ نامش از فیلد entry مانیفست می‌آید
assets/          # اختیاری — آیکن‌ها و فایل‌های استاتیک
theme/           # اختیاری — استایل مخصوص پلاگین
```

`plugin.json` باید این‌ها را داشته باشد: `id` (۳ تا ۱۰۰ کاراکتر، فقط حروف/عدد/نقطه/خط‌تیره)، `name`،
`version` به شکل `1.0.0` یا `1.0.0-beta` و `entry` که فایلش واقعاً داخل بسته باشد. اگر `icon` مقدار داشته
باشد، آن فایل هم باید در بسته موجود باشد.

سرور موقع پذیرش این‌ها را رد می‌کند: مسیر مطلق یا `..` داخل zip، بیش از ۲۰۰۰ فایل، حجم بسته بیش از ۲۰MB،
حجم بازشده بیش از ۱۰۰MB (zip bomb)، zip خراب، مانیفست ناقص، و نسخه‌ی تکراری (مگر با `?overwrite=true`).

## API

| متد | مسیر | توضیح |
|---|---|---|
| GET | `/` | صفحه‌ی مرور در مرورگر (فهرست بسته‌ها + لینک دانلود) |
| GET | `/health` | وضعیت + تعداد بسته و نسخه |
| GET | `/api/v1/plugins?q=&page=&pageSize=` | فهرست/جستجو (آخرین نسخه‌ی هر بسته) |
| GET | `/api/v1/plugins/{id}` | جزئیات + همه‌ی نسخه‌ها |
| GET | `/api/v1/plugins/{id}/download?version=` | دانلود بسته (بدون `version` یعنی آخرین) |
| GET | `/api/v1/plugins/{id}/icon` | آیکن بسته |
| POST | `/api/v1/plugins?overwrite=` | انتشار — نیازمند هدر `X-Api-Key` |
| DELETE | `/api/v1/plugins/{id}?version=` | حذف نسخه یا کل بسته — نیازمند کلید |
| POST | `/api/v1/rescan` | ساخت دوباره‌ی فهرست از روی دیسک — نیازمند کلید |

آدرس‌های `downloadUrl`/`iconUrl` در پاسخ‌ها **نسبی** هستند تا پشت هر prefix ای درست کار کنند.

> `proxy_pass` در nginx با اسلش انتهایی نوشته شده، یعنی nginx خودش `/pluglauncher` را حذف می‌کند و
> برنامه `PathBase` خالی می‌بیند. به همین دلیل صفحه‌ی HTML عمداً تگ `<base>` نمی‌گذارد (اگر بگذارد،
> `<base href="/">` می‌شود و لینک‌ها به ریشه‌ی دامنه می‌روند و ۴۰۴ می‌گیرند). لینک‌ها نسبی می‌مانند و
> مرورگر آن‌ها را نسبت به `/pluglauncher/` حل می‌کند. `/pluglauncher` بدون اسلش را هم nginx با ۳۰۱ به
> نسخه‌ی اسلش‌دار می‌فرستد، پس این فرض همیشه برقرار است.

## ساخت و انتشار یک بسته

```powershell
# ساخت فایل .plz از پوشه‌ی پلاگین
./tools/pack-plugin.ps1 -Path ./plugins/steam-games -Output ./dist

# انتشار (کلید را یک‌بار در متغیر محیطی بگذارید)
$env:PLUGSTORE_KEY = "<کلید>"
./tools/publish-plugin.ps1 -File ./dist/com.pluglauncher.steam-games-1.0.0.plz
```

کلید انتشار روی سرور در `/etc/pluglauncher-store.env` است (فقط root، `chmod 600`):

```bash
ssh hokm 'grep Store__ApiKey /etc/pluglauncher-store.env'
```

## چیدمان روی سرور

| مسیر | محتوا |
|---|---|
| `/opt/pluglauncher-store/` | فایل‌های برنامه (framework-dependent) |
| `/var/lib/pluglauncher-store/` | داده‌ها: `packages/{id}/{version}/…` و `stats.json` |
| `/etc/pluglauncher-store.env` | تنظیمات + کلید انتشار |
| `/etc/systemd/system/pluglauncher-store.service` | سرویس (کاربر `pluglauncher`، بدون شل) |
| `/usr/local/dotnet/` | رانتایم ASP.NET Core 10.0.11 (نصب‌شده برای همین سرویس) |
| `/etc/nginx/sites-enabled/hokm-server` | بلاک `location /pluglauncher/` |

> **دقت**: روی این سرور `sites-enabled/hokm-server` یک فایل مستقل است، نه symlink به `sites-available`،
> و آن دو با هم فرق دارند. فایل زنده همان `sites-enabled` است. نسخه‌ی پشتیبان قبل از تغییر در
> `/root/hokm-server.enabled.bak.*` گذاشته شده است.

سرویس با `ProtectSystem=strict` اجرا می‌شود و تنها مسیر قابل نوشتنش `/var/lib/pluglauncher-store` است.
`Type=simple` عمدی است — برنامه `UseSystemd()` ندارد، پس `Type=notify` باعث تایم‌اوت و ری‌استارت مداوم می‌شد.

## به‌روزرسانی سرویس

```powershell
dotnet publish src/PlugLauncher.Store/PlugLauncher.Store.csproj -c Release -o publish/store --no-self-contained
tar czf publish/store.tar.gz -C publish/store .
scp publish/store.tar.gz hokm:/tmp/
```

```bash
ssh hokm 'systemctl stop pluglauncher-store &&
  rm -rf /opt/pluglauncher-store/* &&
  tar xzf /tmp/store.tar.gz -C /opt/pluglauncher-store &&
  systemctl start pluglauncher-store &&
  curl -s http://127.0.0.1:5100/health'
```

بیلد `--no-self-contained` عمدی است: بسته‌ی self-contained حدود ۱۰۵MB می‌شود و آپلودش روی این لینک
بیش از ۲۵ دقیقه طول کشید؛ نسخه‌ی framework-dependent حدود ۱۳۰KB است.

## فروشگاه استاتیک (بدون سرور)

کلاینت به سرور نیاز ندارد. `StoreClient` اول `index.json` را از ریشه‌ی آدرس می‌گیرد و اگر نبود
(۴۰۴) به API فعلی برمی‌گردد؛ نتیجه‌ی این تشخیص تا پایان عمر همان کلاینت نگه داشته می‌شود. یعنی
یک پوشه‌ی ساده روی هر هاست استاتیکی — GitHub Pages، jsDelivr، یا حتی یک `location` روی همین
nginx — همان کاری را می‌کند که سرویس ASP.NET می‌کند.

**برای کاربر هیچ چیزی لازم نیست**: نه git، نه اکانت، نه توکن. فقط دو `GET` روی HTTPS.

```powershell
# همه‌ی .plz های موجود را به یک فروشگاه استاتیک تبدیل می‌کند
./tools/build-static-store.ps1 -Packages ./dist -Output ./dist/store-static -Clean
```

خروجی:

```
index.json                                   # فهرست، آخرین نسخه‌ی هر بسته
packages/{id}/{version}/{id}-{version}.plz   # همه‌ی نسخه‌ها (لینک قدیمی‌ها نمی‌شکند)
packages/{id}/{version}/icon.png             # آیکن، از داخل بسته بیرون کشیده شده
```

`index.json` همان شکل پاسخ API را دارد (`total` + `items`) و `downloadUrl`/`iconUrl` در آن
**نسبی** هستند، پس زیر هر prefix ای کار می‌کند. `sha256` و `size` از روی خود فایل حساب می‌شوند،
یعنی بررسی صحت سمت کلاینت دقیقاً مثل حالت API باقی می‌ماند.

### تفاوت‌ها با فروشگاه API

| چیز | API (VPS) | استاتیک |
|---|---|---|
| انتشار | `POST` با `X-Api-Key` | کپی فایل + push (یا Contents API گیت‌هاب) |
| جستجو | سمت سرور (`?q=`) | سمت کلاینت (فهرست کامل حداکثر چند ده بسته است) |
| شمارنده‌ی دانلود | دارد | ندارد — نوشتن لازم دارد |
| اعتبارسنجی بسته موقع انتشار | سرور (zip bomb، مانیفست، مسیر مطلق) | باید در اسکریپت pack یا یک GitHub Action انجام شود |
| دفاع‌های کلاینت (sha256، zip slip) | دارد | همان، بدون تغییر |

### نکته‌ی Content-Type

`StoreClient` عمداً از `GetFromJsonAsync` استفاده نمی‌کند: بدنه را رشته‌ای می‌خواند و خودش
دیسریالایز می‌کند. دلیلش این است که هاست‌های استاتیک همیشه `application/json` نمی‌فرستند —
`raw.githubusercontent.com` برای فایل JSON هم `text/plain` می‌دهد و آن متد پاسخ را رد می‌کند.
با این تغییر، هر هاستی که فایل را برگرداند کار می‌کند.

## امنیت کلاینت

کلاینت (`StoreClient`) بعد از دانلود، `sha256` فایل را با مقداری که فروشگاه اعلام کرده مقایسه می‌کند و
در صورت عدم تطابق نصب را متوقف می‌کند. باز کردن zip هم در برابر zip slip محافظت شده و نصب روی پوشه‌ی
`.installing` انجام می‌شود تا نصب نیمه‌کاره روی نسخه‌ی سالم قبلی ننشیند.

آدرس فروشگاه در UI قابل ویرایش نیست: مقدار پیش‌فرض در `AppSettings.StoreUrl` هاردکد است و تغییرش
فقط با ویرایش دستی `%APPDATA%\PlugLauncher\settings.json` ممکن است. دلیلش همان نکته‌ی زیر است —
وقتی کد بسته بدون sandbox اجرا می‌شود، عوض کردن منبع بسته‌ها نباید یک تکست‌باکس فاصله داشته باشد.

**نکته‌ی امنیتی باقی‌مانده**: کد پلاگین با دسترسی کامل کاربر اجرا می‌شود (بدون sandbox). `sha256` فقط
تضمین می‌کند فایل همان چیزی است که فروشگاه دارد؛ تضمین نمی‌کند محتوایش بی‌خطر است. تا وقتی انتشار
فقط با کلید شماست این ریسک کنترل‌شده است.
