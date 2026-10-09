using System.Globalization;
using System.Windows.Data;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.App.Converters;

/// <summary>Formats a byte count with the same rule the rest of the app uses (ByteFormat.Gb), e.g. 285.7.</summary>
public sealed class BytesToGbConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ulong bytes ? ByteFormat.Gb(bytes) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
