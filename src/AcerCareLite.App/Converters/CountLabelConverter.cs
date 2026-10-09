using System.Globalization;
using System.Windows.Data;

namespace AcerCareLite.App.Converters;

/// <summary>"2" with ConverterParameter "adapter" becomes "2 adapters"; "1" becomes "1 adapter"; "0" gives an empty string.</summary>
public sealed class CountLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int count || count <= 0) return "";
        var noun = parameter as string ?? "item";
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
