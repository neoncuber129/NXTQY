using System.Globalization;
using System.Windows.Data;

namespace Inventory.App.Converters;

/// <summary>Cho phép ô lưới trống ↔ null với kiểu decimal?.</summary>
public sealed class NullableDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is decimal d)
        {
            return d.ToString("N0", culture);
        }

        return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Number, culture, out var parsed))
        {
            return parsed;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return Binding.DoNothing;
    }
}
