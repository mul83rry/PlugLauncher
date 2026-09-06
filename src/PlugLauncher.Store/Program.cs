using Microsoft.Extensions.Options;
using PlugLauncher.Store;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection("Store"));
builder.Services.AddSingleton<PackageStore>();

builder.WebHost.ConfigureKestrel((context, options) =>
{
    var max = context.Configuration.GetValue<long?>("Store:MaxPackageBytes") ?? 20L * 1024 * 1024;

    // کمی بیشتر از سقف بسته، تا خود سقف را PackageStore با پیام فارسی رد کند نه Kestrel با 413 خالی
    options.Limits.MaxRequestBodySize = max + 1024 * 1024;
});

var app = builder.Build();

// وقتی پشت nginx زیر یک مسیر (مثلاً /pluglauncher) سرو می‌شود
var basePath = app.Configuration["Store:BasePath"];
if (!string.IsNullOrWhiteSpace(basePath)) app.UsePathBase(basePath);

var store = app.Services.GetRequiredService<PackageStore>();
var options = app.Services.GetRequiredService<IOptions<StoreOptions>>().Value;

app.Lifetime.ApplicationStopping.Register(store.FlushStats);

// ===== صفحه‌ی مرور در مرورگر =====

app.MapGet("/", (HttpRequest request) =>
    Results.Content(IndexPage.Render(store, request.PathBase), "text/html; charset=utf-8"));

// ===== سلامت =====

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    plugins = store.PluginCount,
    versions = store.VersionCount,
    publishEnabled = !string.IsNullOrWhiteSpace(options.ApiKey)
}));

// ===== خواندن فهرست =====

app.MapGet("/api/v1/plugins", (string? q, int? page, int? pageSize) =>
    Results.Ok(store.Search(q, page ?? 1, pageSize ?? 50)));

app.MapGet("/api/v1/plugins/{id}", (string id) =>
{
    var detail = store.GetDetail(id);
    return detail is null ? NotFound(id) : Results.Ok(detail);
});

app.MapGet("/api/v1/plugins/{id}/download", (string id, string? version) =>
{
    var package = store.OpenPackage(id, version);
    if (package is null) return NotFound(id, version);

    return Results.File(package.Value.Stream, "application/zip", package.Value.FileName);
});

app.MapGet("/api/v1/plugins/{id}/icon", (string id, string? version) =>
{
    var icon = store.OpenIcon(id, version);
    return icon is null
        ? Results.NotFound()
        : Results.File(icon.Value.Stream, icon.Value.ContentType);
});

// ===== انتشار (نیازمند کلید) =====

app.MapPost("/api/v1/plugins", async (HttpRequest request, bool? overwrite, CancellationToken cancellationToken) =>
{
    if (!IsAuthorized(request, options, out var authError)) return authError!;

    Stream body;
    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.FirstOrDefault();
        if (file is null) return Results.BadRequest(new { error = "هیچ فایلی در فرم ارسال نشده است." });

        body = file.OpenReadStream();
    }
    else
    {
        body = request.Body;
    }

    var result = await store.PublishAsync(body, overwrite ?? false, cancellationToken);

    return result.Ok
        ? Results.Ok(result.Plugin)
        : Results.BadRequest(new { error = result.Error });
});

app.MapDelete("/api/v1/plugins/{id}", (HttpRequest request, string id, string? version) =>
{
    if (!IsAuthorized(request, options, out var authError)) return authError!;

    return store.Delete(id, version)
        ? Results.Ok(new { deleted = id, version })
        : NotFound(id, version);
});

app.MapPost("/api/v1/rescan", (HttpRequest request) =>
{
    if (!IsAuthorized(request, options, out var authError)) return authError!;

    store.Rescan();
    return Results.Ok(new { plugins = store.PluginCount, versions = store.VersionCount });
});

app.Run();
return;

// ===== کمکی‌ها =====

static IResult NotFound(string id, string? version = null) => Results.NotFound(new
{
    error = version is null
        ? $"بسته‌ای با شناسه‌ی «{id}» پیدا نشد."
        : $"نسخه‌ی «{version}» از بسته‌ی «{id}» پیدا نشد."
});

static bool IsAuthorized(HttpRequest request, StoreOptions options, out IResult? error)
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        error = Results.Json(
            new { error = "انتشار روی این سرور فعال نیست (کلید تنظیم نشده است)." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
        return false;
    }

    var provided = request.Headers["X-Api-Key"].ToString();
    if (!CryptographicEquals(provided, options.ApiKey))
    {
        error = Results.Json(new { error = "کلید نامعتبر است." }, statusCode: StatusCodes.Status401Unauthorized);
        return false;
    }

    error = null;
    return true;
}

/// <summary>مقایسه‌ی زمان‌ثابت تا کلید از روی تفاوت زمان پاسخ حدس زده نشود.</summary>
static bool CryptographicEquals(string a, string b)
{
    var left = System.Text.Encoding.UTF8.GetBytes(a);
    var right = System.Text.Encoding.UTF8.GetBytes(b);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
}
