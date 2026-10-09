using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace AcerCareLite.App.Converters;

/// <summary>
/// Presentation-only split of a reading such as "14 %" into a large number ("14") and a small unit ("%"),
/// the way the reference cards show them. Text that is not a percentage ("Not available", "No battery") is
/// shown whole with no unit. ConverterParameter: "Number" (default) or "Unit".
/// </summary>
public sealed class UsageTextConverter : IValueConverter
{
    private static readonly Regex Percent = new(@"^\s*(\d+(?:[.,]\d+)?)\s*%\s*$", RegexOptions.Compiled);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var match = Percent.Match(text);
        var number = match.Success ? match.Groups[1].Value : text;
        var unit = match.Success ? "%" : "";
        return parameter as string == "Unit" ? unit : number;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
