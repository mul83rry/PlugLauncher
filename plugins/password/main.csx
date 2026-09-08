// Password generator. Keyword: pw
//
//   pw                 16 chars, upper + lower + digits + symbols
//   pw 24              length 24 (4..128)
//   pw 24 nosym        drop symbols
//   pw 20 easy         drop look-alike characters (0 O o 1 l I |)
//   pw 8 pin           digits only
//   pw 32 hex          lowercase hex
//   pw 20 nu nd        drop uppercase and digits
//
// Enter copies the password to the clipboard. Every keystroke regenerates,
// so pressing space at the end is an easy way to reroll.
//
// Typing just "pw" also lists the options as example rows; Enter on one of them writes it into
// the search box instead of running anything, so nothing has to be memorised.

using System.Security.Cryptography;

// ---------- character sets ----------
const string Upper   = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const string Lower   = "abcdefghijklmnopqrstuvwxyz";
const string Digits  = "0123456789";
const string Symbols = "!@#$%^&*()-_=+[]{};:,.?/";
const string Hex     = "0123456789abcdef";

// characters that are easy to misread when a password is typed by hand
const string Ambiguous = "O0oIl1|S5B8Z2";

class Options
{
    public int Length = 16;
    public bool Upper = true, Lower = true, Digits = true, Symbols = true;
    public bool Easy;                 // drop ambiguous characters
    public string? Mode;              // "pin" | "hex" | null
    public bool OptionsUsed;          // any switch beyond a bare length
    public List<string> Unknown = new();
}

// ---------- parameter parsing ----------
Options Parse(string[] terms)
{
    var o = new Options();
    var setsTouched = false;

    foreach (var raw in terms)
    {
        var t = raw.ToLowerInvariant().TrimStart('-', '/');

        if (int.TryParse(t, out var n)) { o.Length = Math.Clamp(n, 4, 128); continue; }

        switch (t)
        {
            case "pin" or "digits" or "numeric":
                o.Mode = "pin"; break;
            case "hex":
                o.Mode = "hex"; break;

            case "nosym" or "nosymbols" or "ns" or "alnum" or "a":
                o.Symbols = false; setsTouched = true; break;
            case "noupper" or "nu":
                o.Upper = false; setsTouched = true; break;
            case "nolower" or "nl":
                o.Lower = false; setsTouched = true; break;
            case "nodigits" or "nd":
                o.Digits = false; setsTouched = true; break;

            case "sym" or "symbols" or "s":
                o.Symbols = true; setsTouched = true; break;

            case "easy" or "readable" or "clear":
                o.Easy = true; break;

            case "":
                break;
            default:
                o.Unknown.Add(raw); break;
        }
    }

    // "pw 20 nu nl nd ns" would leave nothing to pick from
    if (setsTouched && !o.Upper && !o.Lower && !o.Digits && !o.Symbols) o.Lower = true;

    o.OptionsUsed = setsTouched || o.Easy || o.Mode is not null;

    return o;
}

// ---------- alphabet ----------
List<string> PoolsFor(Options o)
{
    if (o.Mode == "pin") return new List<string> { Digits };
    if (o.Mode == "hex") return new List<string> { Hex };

    var pools = new List<string>();
    if (o.Upper) pools.Add(Upper);
    if (o.Lower) pools.Add(Lower);
    if (o.Digits) pools.Add(Digits);
    if (o.Symbols) pools.Add(Symbols);

    if (o.Easy)
    {
        pools = pools
            .Select(p => new string(p.Where(c => !Ambiguous.Contains(c)).ToArray()))
            .Where(p => p.Length > 0)
            .ToList();
    }

    return pools;
}

// ---------- generation ----------
// Every pool contributes at least one character, then the rest is drawn from the
// union and the whole thing is shuffled. RandomNumberGenerator.GetInt32 is used
// everywhere: it is a CSPRNG and its range is unbiased (System.Random is neither).
string Generate(Options o)
{
    var pools = PoolsFor(o);
    if (pools.Count == 0) return string.Empty;

    var all = string.Concat(pools);
    var chars = new List<char>(o.Length);

    foreach (var pool in pools)
    {
        if (chars.Count == o.Length) break;
        chars.Add(pool[RandomNumberGenerator.GetInt32(pool.Length)]);
    }

    while (chars.Count < o.Length)
        chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

    // Fisher-Yates, so the guaranteed characters do not always sit at the front
    for (var i = chars.Count - 1; i > 0; i--)
    {
        var j = RandomNumberGenerator.GetInt32(i + 1);
        (chars[i], chars[j]) = (chars[j], chars[i]);
    }

    return new string(chars.ToArray());
}

// ---------- description of what was generated ----------
string Describe(Options o)
{
    if (o.Mode == "pin") return $"{o.Length} digits";
    if (o.Mode == "hex") return $"{o.Length} hex characters";

    var parts = new List<string>();
    if (o.Upper) parts.Add("A-Z");
    if (o.Lower) parts.Add("a-z");
    if (o.Digits) parts.Add("0-9");
    if (o.Symbols) parts.Add("symbols");

    var text = $"{o.Length} chars · {string.Join(" + ", parts)}";
    return o.Easy ? text + " · no look-alikes" : text;
}

string Strength(Options o)
{
    var alphabet = string.Concat(PoolsFor(o)).Length;
    if (alphabet <= 1) return "";

    var bits = (int)Math.Floor(o.Length * Math.Log2(alphabet));
    var label = bits switch
    {
        < 40 => "weak",
        < 60 => "fair",
        < 80 => "strong",
        _ => "very strong"
    };

    return $"~{bits} bits · {label}";
}

// ---------- hint rows ----------
// A row with ReplaceQuery does not generate anything: pressing Enter on it just rewrites the
// search box. That way an option can be tried without having to remember its name first.
List<PluginResult> Hints(Options o)
{
    var n = o.Length;

    var samples = new (string Args, string What)[]
    {
        ("24",          "a bare number sets the length — anything from 4 to 128"),
        ($"{n} nosym",  "letters and digits only, no !@#$ — for sites that reject symbols"),
        ($"{n} easy",   "skips look-alikes 0/O, 1/l/I, 5/S, 8/B, 2/Z — safer to read out or retype"),
        ("6 pin",       "digits only, like a PIN (hex gives hex characters instead)"),
        ($"{n} nu",     "drops uppercase — nl drops lowercase, nd drops digits, combine them freely")
    };

    var rows = new List<PluginResult>();

    for (var i = 0; i < samples.Length; i++)
    {
        var (args, what) = samples[i];

        rows.Add(new PluginResult
        {
            Id = $"hint-{i}",
            Title = $"pw {args}",
            Subtitle = $"{what}  ·  Enter to try it",
            Score = 50 - i,
            ReplaceQuery = $"pw {args}"
        });
    }

    return rows;
}

// ---------- plugin ----------
return Plugin.Create(query =>
{
    var options = Parse(query.Terms);
    var results = new List<PluginResult>();

    if (options.Unknown.Count > 0)
    {
        results.Add(new PluginResult
        {
            Id = "unknown",
            Title = $"Unknown option: {string.Join(", ", options.Unknown)}",
            Subtitle = "pick one of the examples below",
            Score = 300
        });

        results.AddRange(Hints(options));
        return results;
    }

    // three candidates, so a password with an awkward shape can be skipped
    for (var i = 0; i < 3; i++)
    {
        var password = Generate(options);
        if (password.Length == 0) continue;

        var strength = Strength(options);
        var subtitle = i == 0
            ? $"{Describe(options)}  ·  {strength}  ·  Enter to copy"
            : $"{Describe(options)}  ·  {strength}";

        results.Add(new PluginResult
        {
            Id = $"password-{i}",
            Title = password,
            Subtitle = subtitle,
            Score = 200 - i,
            Action = () => Clipboard.Copy(password)
        });
    }

    // The examples stay on screen until an option is actually used, so "pw" and "pw 24" both
    // teach the syntax; once the user types one, they get out of the way.
    if (!options.OptionsUsed) results.AddRange(Hints(options));

    return results;
});
