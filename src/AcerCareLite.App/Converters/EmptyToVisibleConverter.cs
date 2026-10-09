using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AcerCareLite.App.Converters;

/// <summary>Visible when the text is empty, Collapsed otherwise. Used for "nothing yet" placeholders.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
