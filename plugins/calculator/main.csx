// Global sample plugin: it declares no keyword in plugin.json, so it is called for every
// query and only returns something when the text is a math expression. Example: 12*(3+4)

using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

bool LooksLikeMath(string text)
    => Regex.IsMatch(text, @"^[\d\s\.\+\-\*/\(\)%]+$") && Regex.IsMatch(text, @"[\+\-\*/%]");

string? Evaluate(string expression)
{
    try
    {
        var value = new DataTable().Compute(expression, null);
        if (value is null || value == DBNull.Value) return null;

        return Convert.ToDouble(value, CultureInfo.InvariantCulture)
            .ToString("0.##########", CultureInfo.InvariantCulture);
    }
    catch
    {
        return null;
    }
}

return Plugin.Create(query =>
{
    var expression = query.Search.Trim();
    if (!LooksLikeMath(expression)) return [];

    var result = Evaluate(expression);
    if (result is null) return [];

    return new[]
    {
        new PluginResult
        {
            Id = "result",
            Title = result,
            Subtitle = $"{expression} — press Enter to copy",
            Score = 200,
            Action = () => System.Windows.Clipboard.SetText(result)
        }
    };
});
