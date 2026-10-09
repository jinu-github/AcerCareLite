using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AcerCareLite.App.Converters;

/// <summary>
/// Maps a page's Segoe MDL2 glyph (PageViewModel.Glyph) to a line icon from Themes/Icons.xaml.
/// The view model keeps its glyph untouched; this is purely a presentation lookup.
/// </summary>
public sealed class NavIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var glyph = value as string;
        var key = glyph switch
        {
            "\uE80F" => "Icon.Dashboard",
            "\uE83F" => "Icon.Battery",
            "\uE7F4" => "Icon.Hardware",
            "\uE74D" => "Icon.Utilities",
            "\uE713" => "Icon.Settings",
            _ => null
        };
        return key == null ? null : Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
