using System.Globalization;

namespace PlugLauncher.Store;

/// <summary>
/// نسخه‌ی بسته به سبک semver ساده: <c>major.minor.patch[-prerelease]</c>.
/// مقایسه عددی است؛ نسخه‌ی prerelease همیشه پایین‌تر از نسخه‌ی نهایی هم‌شماره قرار می‌گیرد.
/// </summary>
public readonly record struct PackageVersion(int Major, int Minor, int Patch, string PreRelease, string Raw)
    : IComparable<PackageVersion>
{
    public static bool TryParse(string? text, out PackageVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = text.Trim();
        var core = raw;
        var pre = string.Empty;

        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            pre = core[(dash + 1)..];
            core = core[..dash];
            if (pre.Length == 0) return false;
        }

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
                return false;
            numbers[i] = value;
        }

        version = new PackageVersion(numbers[0], numbers[1], numbers[2], pre, raw);
        return true;
    }

    public int CompareTo(PackageVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // نسخه‌ی نهایی از prerelease هم‌شماره بالاتر است
        if (PreRelease.Length == 0 && other.PreRelease.Length > 0) return 1;
        if (PreRelease.Length > 0 && other.PreRelease.Length == 0) return -1;

        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public override string ToString() => Raw;
}
