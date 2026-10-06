using System.Globalization;
using System.Windows.Data;

namespace Inventory.App.Converters;

/// <summary>Chuỗi rỗng/null → ConverterParameter (mặc định "(không)").</summary>
public sealed class BlankFallbackConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return parameter as string ?? "(không)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
