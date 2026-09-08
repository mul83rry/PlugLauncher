// Everyday developer conversions. Keyword: dev
//
//   dev uuid 3            three random GUIDs
//   dev b64 hello         base64 encode   ·   dev b64d aGVsbG8=   decode
//   dev url a b&c         percent encode  ·   dev urld a%20b      decode
//   dev hash hello        md5 / sha1 / sha256 of the text
//   dev ts                epoch and ISO for right now
//   dev ts 1767225600     that epoch as a date (seconds or milliseconds)
//   dev json {"a":1}      pretty printed and minified
//   dev jwt eyJhbGci...   header and payload, without verifying anything
//   dev rand 16           16 random bytes as hex and base64
//   dev slug Hello World  hello-world
//
// Enter copies the row to the clipboard. Typing just "dev" lists the commands;
// Enter on one of those writes it into the search box instead of running it.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

PluginResult Row(string id, string title, string subtitle, int score, string? copy = null)
{
    var value = copy ?? title;

    return new PluginResult
    {
        Id = id,
        Title = title,
        Subtitle = subtitle,
        Score = score,
        Action = () => Clipboard.Copy(value)
    };
}

PluginResult Problem(string title, string subtitle)
    => new PluginResult { Id = "error", Title = title, Subtitle = subtitle, Score = 200 };

// ---------- commands ----------
List<PluginResult> Uuid(string argument)
{
    var count = int.TryParse(argument.Trim(), out var n) ? Math.Clamp(n, 1, 10) : 1;
    var rows = new List<PluginResult>();

    for (var i = 0; i < count; i++)
    {
        var value = Guid.NewGuid().ToString();
        rows.Add(Row($"uuid-{i}", value, i == 0 ? "lowercase with dashes  ·  Enter to copy" : "", 200 - i));
    }

    // the other two shapes people actually paste
    var single = Guid.NewGuid();
    rows.Add(Row("uuid-n", single.ToString("N"), "no dashes", 100));
    rows.Add(Row("uuid-upper", single.ToString().ToUpperInvariant(), "uppercase", 99));

    return rows;
}

List<PluginResult> Base64Encode(string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    var standard = Convert.ToBase64String(bytes);

    // base64url is what JWTs and query strings use: different alphabet, no padding
    var url = standard.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    return
    [
        Row("b64", standard, $"base64 of {bytes.Length} byte(s)  ·  Enter to copy", 200),
        Row("b64url", url, "base64url — for JWTs and query strings", 190)
    ];
}

List<PluginResult> Base64Decode(string text)
{
    var trimmed = text.Trim().Replace('-', '+').Replace('_', '/');
    if (trimmed.Length % 4 != 0) trimmed = trimmed.PadRight(trimmed.Length + (4 - trimmed.Length % 4), '=');

    byte[] bytes;
    try { bytes = Convert.FromBase64String(trimmed); }
    catch { return [Problem("Not valid base64", "the text has characters outside the base64 alphabet")]; }

    var rows = new List<PluginResult>
    {
        Row("b64d", Encoding.UTF8.GetString(bytes), $"{bytes.Length} byte(s) as UTF-8  ·  Enter to copy", 200)
    };

    // binary payloads come out as replacement characters, so offer the bytes too
    rows.Add(Row("b64d-hex", Convert.ToHexString(bytes).ToLowerInvariant(), "the same bytes as hex", 190));
    return rows;
}

List<PluginResult> UrlDecode(string text)
{
    try
    {
        return [Row("urld", Uri.UnescapeDataString(text), "percent decoded  ·  Enter to copy", 200)];
    }
    catch (UriFormatException ex)
    {
        return [Problem("Could not decode that", ex.Message)];
    }
}

List<PluginResult> Hash(string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);

    string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    return
    [
        Row("sha256", Hex(SHA256.HashData(bytes)), "sha256  ·  Enter to copy", 200),
        Row("sha1", Hex(SHA1.HashData(bytes)), "sha1 — broken for signatures, fine as a checksum", 190),
        Row("md5", Hex(MD5.HashData(bytes)), "md5 — broken for signatures, fine as a checksum", 180)
    ];
}

List<PluginResult> Timestamp(string argument)
{
    var text = argument.Trim();
    DateTimeOffset moment;
    string origin;

    if (text.Length == 0)
    {
        moment = DateTimeOffset.Now;
        origin = "now";
    }
    else if (long.TryParse(text, out var epoch))
    {
        // ten digits is seconds, thirteen is milliseconds; the cut sits between them
        moment = Math.Abs(epoch) > 100_000_000_000L
            ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
            : DateTimeOffset.FromUnixTimeSeconds(epoch);

        origin = Math.Abs(epoch) > 100_000_000_000L ? "read as milliseconds" : "read as seconds";
    }
    else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
    {
        moment = parsed;
        origin = "parsed as a date";
    }
    else
    {
        return [Problem("Could not read that", "give an epoch number, a date like 2026-01-01T10:00, or nothing for now")];
    }

    var local = moment.ToLocalTime();
    var span = DateTimeOffset.Now - moment;
    var relative = span.TotalSeconds >= 0
        ? $"{Humanize(span)} ago"
        : $"in {Humanize(span.Negate())}";

    return
    [
        Row("epoch", moment.ToUnixTimeSeconds().ToString(), $"unix seconds  ·  {origin}  ·  Enter to copy", 200),
        Row("epoch-ms", moment.ToUnixTimeMilliseconds().ToString(), "unix milliseconds", 190),
        Row("iso", moment.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), "ISO-8601, UTC", 180),
        Row("local", local.ToString("yyyy-MM-dd HH:mm:ss"), $"local time  ·  {relative}", 170)
    ];
}

string Humanize(TimeSpan span)
{
    if (span.TotalDays >= 365) return $"{span.TotalDays / 365:0.#} year(s)";
    if (span.TotalDays >= 1) return $"{span.TotalDays:0.#} day(s)";
    if (span.TotalHours >= 1) return $"{span.TotalHours:0.#} hour(s)";
    if (span.TotalMinutes >= 1) return $"{span.TotalMinutes:0.#} minute(s)";
    return $"{span.TotalSeconds:0} second(s)";
}

List<PluginResult> Json(string text)
{
    JsonDocument document;
    try { document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true }); }
    catch (JsonException ex) { return [Problem("Invalid JSON", ex.Message)]; }

    using (document)
    {
        var pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var minified = JsonSerializer.Serialize(document.RootElement);

        // a pretty printed document is many lines; the row shows one, the clipboard gets all of it
        return
        [
            Row("pretty", OneLine(pretty), $"valid JSON  ·  Enter copies the {pretty.Split('\n').Length}-line indented form", 200, pretty),
            Row("minified", OneLine(minified), $"minified  ·  {minified.Length} characters", 190, minified)
        ];
    }
}

string OneLine(string text)
{
    var flat = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
    flat = flat.Trim();

    return flat.Length > 120 ? flat.Substring(0, 117) + "..." : flat;
}

byte[]? FromBase64Url(string text)
{
    var padded = text.Replace('-', '+').Replace('_', '/');
    if (padded.Length % 4 != 0) padded = padded.PadRight(padded.Length + (4 - padded.Length % 4), '=');

    try { return Convert.FromBase64String(padded); }
    catch { return null; }
}

List<PluginResult> Jwt(string token)
{
    var parts = token.Trim().Split('.');
    if (parts.Length < 2) return [Problem("Not a JWT", "expected header.payload.signature")];

    var rows = new List<PluginResult>();
    var names = new[] { "header", "payload" };

    for (var i = 0; i < 2; i++)
    {
        var bytes = FromBase64Url(parts[i]);
        if (bytes is null)
        {
            rows.Add(Problem($"The {names[i]} is not base64url", "the token is truncated or not a JWT"));
            continue;
        }

        var text = Encoding.UTF8.GetString(bytes);
        string pretty;
        try
        {
            using var document = JsonDocument.Parse(text);
            pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { pretty = text; }

        rows.Add(Row(names[i], OneLine(text), $"{names[i]}  ·  Enter copies the indented form", 200 - i * 10, pretty));
    }

    // exp/iat are the two claims worth reading at a glance
    var claims = FromBase64Url(parts[1]);
    if (claims is not null)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(claims));
            foreach (var claim in new[] { "exp", "iat", "nbf" })
            {
                if (!document.RootElement.TryGetProperty(claim, out var value) || !value.TryGetInt64(out var epoch)) continue;

                var moment = DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
                var note = claim == "exp"
                    ? (moment < DateTimeOffset.Now ? "expired" : "expires")
                    : claim == "iat" ? "issued" : "not valid before";

                rows.Add(Row($"claim-{claim}", $"{claim}: {moment:yyyy-MM-dd HH:mm:ss}", $"{note}  ·  local time", 150));
            }
        }
        catch
        {
            // the payload is not JSON; the raw rows above still say everything there is to say
        }
    }

    // signatures are not checked here — say so rather than let the rows imply otherwise
    rows.Add(new PluginResult
    {
        Id = "jwt-note",
        Title = "The signature is not verified",
        Subtitle = "this only decodes the token — it says nothing about whether it is genuine",
        Score = 10
    });

    return rows;
}

List<PluginResult> Random(string argument)
{
    var count = int.TryParse(argument.Trim(), out var n) ? Math.Clamp(n, 1, 512) : 16;
    var bytes = RandomNumberGenerator.GetBytes(count);

    return
    [
        Row("hex", Convert.ToHexString(bytes).ToLowerInvariant(), $"{count} random byte(s) as hex  ·  Enter to copy", 200),
        Row("b64", Convert.ToBase64String(bytes), "the same bytes as base64", 190),
        Row("int", RandomNumberGenerator.GetInt32(0, int.MaxValue).ToString(), "a random 31-bit integer", 180)
    ];
}

List<PluginResult> Slug(string text)
{
    var builder = new StringBuilder();

    foreach (var c in text.Trim().ToLowerInvariant())
    {
        if (char.IsLetterOrDigit(c)) builder.Append(c);
        else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
    }

    var slug = builder.ToString().Trim('-');
    if (slug.Length == 0) return [Problem("Nothing left", "the text has no letters or digits in it")];

    return
    [
        Row("slug", slug, "url slug  ·  Enter to copy", 200),
        Row("snake", slug.Replace('-', '_'), "snake_case", 190),
        Row("screaming", slug.Replace('-', '_').ToUpperInvariant(), "SCREAMING_SNAKE_CASE", 180)
    ];
}

// ---------- hints ----------
// A row with ReplaceQuery runs nothing: Enter (or Tab) just rewrites the search box, so a
// command can be tried without having to remember its name first.
//
// Needs marks a command that says nothing useful without a payload — "dev b64" on its own
// used to answer with the base64 of nothing, which is an empty row. Now it answers with the
// finished command instead, ready to be taken with Tab.
//
// Secondary keeps a command out of the unfiltered list, which only has room for eight rows —
// a ninth would be dropped silently, which is worse than being one keystroke away. The two
// decoders and rand are the ones that carry: "dev b" offers b64 and b64d together, "dev r"
// finds rand, and uuid right at the top already covers most of what rand is wanted for.
(string Command, string Example, string What, bool Needs, bool Secondary)[] Commands =
[
    ("uuid", "uuid 3",           "random GUIDs — a count of 1 to 10",                           false, false),
    ("b64",  "b64 hello",        "base64 encode, standard and base64url",                       true,  false),
    ("b64d", "b64d aGVsbG8=",    "base64 decode, as text and as hex",                           true,  true),
    ("url",  "url a b&c",        "percent encode",                                              true,  false),
    ("urld", "urld a%20b",       "percent decode",                                              true,  true),
    ("hash", "hash hello",       "sha256, sha1 and md5 of the text",                            true,  false),
    ("ts",   "ts",               "epoch and ISO for now; ts 1767225600 converts a number back", false, false),
    ("json", "json {\"a\":1}",   "validate, then pretty print or minify",                       true,  false),
    ("jwt",  "jwt eyJhbGciOi",   "decode a token's header and payload (no verification)",       true,  false),
    ("rand", "rand 16",          "random bytes as hex and base64",                              false, true),
    ("slug", "slug Hello World", "url slug, snake_case and SCREAMING_SNAKE_CASE",               true,  false)
];

List<PluginResult> Hints(string filter)
{
    var rows = new List<PluginResult>();
    var score = 100;

    foreach (var (command, example, what, _, secondary) in Commands)
    {
        if (filter.Length == 0)
        {
            if (secondary) continue;
        }
        else if (!command.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;

        rows.Add(new PluginResult
        {
            Id = $"hint-{command}",
            Title = $"dev {example}",
            Subtitle = $"{what}  ·  Enter to try it",
            Score = score--,
            ReplaceQuery = $"dev {example}"
        });
    }

    return rows;
}

// The canonical name behind whatever was typed, or null when nothing here answers to it.
string? Canonical(string command) => command switch
{
    "uuid" or "guid" => "uuid",
    "b64" or "base64" => "b64",
    "b64d" or "unbase64" => "b64d",
    "url" or "urlencode" => "url",
    "urld" or "urldecode" => "urld",
    "hash" or "sum" => "hash",
    "ts" or "time" or "epoch" => "ts",
    "json" => "json",
    "jwt" => "jwt",
    "rand" or "random" => "rand",
    "slug" or "kebab" => "slug",
    _ => null
};

bool NeedsArgument(string canonical) => Commands.Any(c => c.Command == canonical && c.Needs);

// ---------- plugin ----------
return Plugin.Create(query =>
{
    var search = query.Search.Trim();
    if (search.Length == 0) return Hints("");

    // only the first word is the command; everything after it is the payload, spaces and all
    var space = search.IndexOf(' ');
    var command = (space < 0 ? search : search.Substring(0, space)).ToLowerInvariant();
    var argument = space < 0 ? "" : search.Substring(space + 1);

    var canonical = Canonical(command);

    // A half-typed name and a real command still waiting for its payload want the same answer:
    // the finished command, so the next word never has to be guessed.
    if (canonical is null || (NeedsArgument(canonical) && argument.Trim().Length == 0))
    {
        var hints = Hints(canonical ?? command);
        if (hints.Count > 0) return hints;

        List<PluginResult> unknown = [Problem($"No command starts with \"{command}\"", "one of these instead"), .. Hints("")];
        return unknown;
    }

    List<PluginResult> rows = canonical switch
    {
        "uuid" => Uuid(argument),
        "b64" => Base64Encode(argument),
        "b64d" => Base64Decode(argument),
        "url" => [Row("url", Uri.EscapeDataString(argument), "percent encoded  ·  Enter to copy", 200)],
        "urld" => UrlDecode(argument),
        "hash" => Hash(argument),
        "ts" => Timestamp(argument),
        "json" => Json(argument),
        "jwt" => Jwt(argument),
        "rand" => Random(argument),
        "slug" => Slug(argument),
        _ => Hints("")
    };

    return rows;
});
