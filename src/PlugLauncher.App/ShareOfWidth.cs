using System.Globalization;
using Avalonia.Data.Converters;

namespace PlugLauncher.App;

/// <summary>
/// عرضِ نوارِ سهم: کسری از عرض چیزی که نوار داخلش نشسته.
///
/// در WPF این کار با دو ستونِ ستاره‌ای انجام می‌شد و هیچ کدی لازم نداشت. اینجا ستون‌های یک
/// Grid داده‌ی ردیف را نمی‌بینند، پس همان یک ضرب صریح نوشته شده.
/// </summary>
public sealed class ShareOfWidth : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not double share || values[1] is not double width) return 0d;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) return 0d;

        return Math.Clamp(share, 0, 1) * width;
    }
}
