// QR codes, both ways. Keyword: qr
//
//   qr https://example.com   -> a QR code of that text, drawn on screen and scannable
//   qr any text at all       -> same, for anything: wifi passwords, notes, whatever
//   qr from clipboard        -> an image on the clipboard: the QR in it is read back to text
//                               text on the clipboard: offered up for encoding
//
// The code is a real PNG, drawn by QRCoder at eight pixels per module with a quiet zone, and
// shown centered in the launcher's image view — crisp at any zoom, and a phone reads it off the
// screen without effort. Copy in the corner gives out the *text* that went in, because the
// drawing is for machines. The PNG lands in the plugin's own data folder, one file per code.
//
// Reading one back: Windows keeps clipboard images as DIB, the launcher hands the pixels here,
// and zxing does the reading — a screenshot of a QR anywhere works, as long as it is reasonably
// straight-on.
//
// QRCoder (MIT) and zxing (Apache-2.0) travel with the plugin in libs/.

using QRCoder;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

// ===== what the host gives us =====

string DataDir = "";

PluginResult Note(string title, string subtitle = "", int score = 500)
    => new() { Id = "note:" + title, Title = title, Subtitle = subtitle, Score = score };

PluginResult Copyable(string id, string title, string subtitle, int score)
    => new() { Id = id, Title = title, Subtitle = subtitle, Score = score, Action = () => Clipboard.Copy(title) };

PluginResult Go(string id, string title, string subtitle, string command, int score)
    => new() { Id = id, Title = title, Subtitle = subtitle, Score = score, ReplaceQuery = command };

string Cut(string text, int max)
{
    var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return flat.Length > max ? flat[..(max - 1)] + "…" : flat;
}

// ===== drawing =====

/// <summary>One PNG per code, in the plugin's own data folder — overwritten when a new text comes.</summary>
string PngPathFor(string text)
    => Path.Combine(DataDir, "qr-" + Convert.ToHexString(
           System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant() + ".png");

List<PluginResult> Encode(string text)
{
    QRCodeData data;
    byte[] png;
    try
    {
        data = new QRCodeGenerator().CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

        // هشت پیکسل به ماژول و حاشیه‌ی سکوت: روی هر رتینایی تمیز می‌ماند و از صفحه خوانده می‌شود
        png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 8);
    }
    catch (Exception ex) { return [Note("Could not make a QR of that", ex.Message)]; }

    string path;
    try
    {
        Directory.CreateDirectory(DataDir);
        path = PngPathFor(text);
        File.WriteAllBytes(path, png);
    }
    catch (Exception ex) { return [Note("Could not save the code", ex.Message)]; }

    var modules = data.ModuleMatrix.Count;

    return
    [
        new PluginResult
        {
            Id = "qr",
            Title = "The QR code",
            Subtitle = $"{modules}×{modules} modules  ·  scan it off the screen  ·  Enter shows it",
            Score = 500,
            DetailTitle = $"QR  ·  {Cut(text, 60)}",
            DetailText = text,
            DetailImagePath = path,
            Action = () => Clipboard.Copy(text)
        },
        Copyable("qr-text", Cut(text, 90), "the text inside the code  ·  Enter copies", 300)
    ];
}

// ===== reading =====

List<PluginResult> FromClipboard()
{
    var image = Clipboard.Image();

    if (image is null)
    {
        var text = Clipboard.Text();
        if (text.Length > 0)
            return
            [
                Go("encode", "Encode what is on the clipboard", Cut(text, 70), "qr " + text, 500),
                Note("The clipboard holds text, not an image", "to read a QR back, copy a picture of it — a screenshot does")
            ];

        return [Note("There is nothing on the clipboard", "copy an image that holds a QR, then type: qr from clipboard")];
    }

    ZXing.Result? result;
    try
    {
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGB32);

        // TRY_HARDER: تصویر کلیپ‌بورد عکسِ عکس است — کمی کج، کمی تار، وسط چیزهای دیگر. حالت
        // پیش‌فرض دیکدر برای بارکدهای تمیزِ وسط صفحه است و اینجا اولین تلاش باید سخاوتمندانه باشد.
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
        result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints);
    }
    catch (Exception ex)
    {
        return [Note("Could not read that image", ex.Message)];
    }

    if (result is null || string.IsNullOrEmpty(result.Text))
        return [Note("No QR code in that image", $"{image.Width}×{image.Height} px  ·  a clear, straight-on shot reads best")];

    return
    [
        Copyable("decoded", Cut(result.Text, 90), "read from the clipboard  ·  Enter copies", 500),
        Go("re-encode", "Turn it back into a QR code", Cut(result.Text, 60), "qr " + result.Text, 400)
    ];
}

// ===== plugin =====

return Plugin.Create(async (query, cancellationToken) =>
{
    var text = query.Search.Trim();

    if (text.Length == 0)
    {
        var rows = new List<PluginResult>
        {
            Go("example", "qr https://example.com", "make a code of any text or link", "qr https://example.com", 500)
        };

        if (Clipboard.Text().Length > 0)
            rows.Add(Go("clip", "qr from clipboard", "the clipboard holds text — encode it", "qr from clipboard", 400));
        else if (Clipboard.Image() is not null)
            rows.Add(Go("clip", "qr from clipboard", "the clipboard holds an image — read the QR in it", "qr from clipboard", 400));

        rows.Add(Note("qr <text> makes one  ·  qr from clipboard reads one", "a code on screen is scannable by a phone"));
        return rows;
    }

    if (text.Equals("from clipboard", StringComparison.OrdinalIgnoreCase)) return FromClipboard();

    return Encode(text);
},
(context, cancellationToken) =>
{
    DataDir = context.DataDirectory;
    return Task.CompletedTask;
});
