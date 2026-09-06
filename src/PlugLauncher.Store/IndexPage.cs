using System.Net;
using System.Text;

namespace PlugLauncher.Store;

/// <summary>
/// صفحه‌ی ساده‌ی مرور فروشگاه در مرورگر. عمداً بدون فایل استاتیک و بدون جاوااسکریپت نوشته شده
/// تا سرویس همچنان تک‌فایلی و بدون wwwroot بماند.
/// </summary>
public static class IndexPage
{
    public static string Render(PackageStore store, string pathBase)
    {
        // پیوندهای فروشگاه نسبی‌اند (api/v1/…). وقتی nginx خودش prefix را حذف می‌کند PathBase خالی
        // می‌ماند و مرورگر باید نسبت به آدرس صفحه حل کند — در آن حالت تگ base نباید نوشته شود،
        // وگرنه لینک‌ها به ریشه‌ی دامنه می‌روند.
        var basePath = string.IsNullOrWhiteSpace(pathBase) ? null : pathBase.TrimEnd('/') + "/";
        var page = store.Search(null, 1, 200);

        var html = new StringBuilder();
        html.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>PlugLauncher Plugin Store</title>
            """);

        if (basePath is not null)
            html.Append("<base href=\"").Append(Escape(basePath)).Append("\">");

        html.Append("""
            <style>
              :root { color-scheme: dark; }
              body { margin:0; padding:32px 20px; background:#1b1b1b; color:#e8e8e8;
                     font-family:"Segoe UI",Inter,system-ui,sans-serif; }
              .wrap { max-width:860px; margin:0 auto; }
              h1 { font-size:24px; margin:0 0 6px; }
              .sub { color:#9a9a9a; font-size:13px; margin:0 0 28px; }
              .card { background:#232323; border-radius:10px; padding:16px 18px; margin-bottom:12px;
                      display:flex; gap:16px; align-items:flex-start; }
              .card img { width:44px; height:44px; object-fit:contain; flex:none; }
              .body { flex:1; min-width:0; }
              .name { font-size:16px; }
              .ver { color:#9a9a9a; font-size:12px; margin-left:8px; }
              .desc { color:#b8b8b8; font-size:13px; margin-top:5px; }
              .meta { color:#7d7d7d; font-size:11.5px; margin-top:6px; }
              a.dl { color:#5cc8d7; text-decoration:none; font-size:13px; white-space:nowrap;
                     border:1px solid #3a4b4e; border-radius:6px; padding:6px 14px; }
              a.dl:hover { background:#2a3739; }
              .empty { color:#9a9a9a; }
              footer { color:#7d7d7d; font-size:12px; margin-top:28px; line-height:2; }
              footer code { background:#262626; padding:2px 6px; border-radius:4px; display:inline-block; }
            </style>
            </head>
            <body><div class="wrap">
            <h1>PlugLauncher Plugin Store</h1>
            """);

        html.Append("<p class=\"sub\">")
            .Append(page.Total)
            .Append(" package(s) published — to install, open PlugLauncher and go to Settings &rarr; Store.</p>");

        if (page.Items.Count == 0)
        {
            html.Append("<p class=\"empty\">Nothing has been published yet.</p>");
        }

        foreach (var p in page.Items)
        {
            html.Append("<div class=\"card\">");

            if (!string.IsNullOrWhiteSpace(p.IconUrl))
                html.Append("<img src=\"").Append(Escape(p.IconUrl)).Append("\" alt=\"\">");

            html.Append("<div class=\"body\">")
                .Append("<div><span class=\"name\">").Append(Escape(p.Name)).Append("</span>")
                .Append("<span class=\"ver\">v").Append(Escape(p.Version)).Append("</span></div>");

            if (!string.IsNullOrWhiteSpace(p.Description))
                html.Append("<div class=\"desc\">").Append(Escape(p.Description)).Append("</div>");

            html.Append("<div class=\"meta\">")
                .Append(Escape(p.Id))
                .Append("  ·  ").Append((p.Size / 1024.0).ToString("0.#")).Append(" KB")
                .Append("  ·  ").Append(p.Downloads).Append(" downloads");

            if (!string.IsNullOrWhiteSpace(p.Author))
                html.Append("  ·  ").Append(Escape(p.Author));

            html.Append("</div></div>");

            html.Append("<a class=\"dl\" href=\"").Append(Escape(p.DownloadUrl)).Append("\">Download</a>");
            html.Append("</div>");
        }

        html.Append("""
            <footer>
              API: <code>api/v1/plugins</code> · health: <code>health</code><br>
              Installing from inside the app verifies the package <code>sha256</code>.
            </footer>
            </div></body></html>
            """);

        return html.ToString();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
