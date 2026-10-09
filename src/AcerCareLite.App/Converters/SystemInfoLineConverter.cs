using System.Globalization;
using System.Windows.Data;

namespace AcerCareLite.App.Converters;

/// <summary>
/// Picks one line out of DashboardViewModel.SystemInfoText ("Maker Model", "BIOS x", "OS description").
/// ConverterParameter: "Model", "Os" or "Bios". A missing line gives "" (Model) or a dash (Bios, Os).
/// </summary>
public sealed class SystemInfoLineConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var lines = (value as string ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (parameter as string) switch
        {
            "Model" => lines.Length > 0 ? lines[0] : "",
            "Os" => lines.Length > 2 ? lines[2] : "—",
            "Bios" => lines.Length > 1 && lines[1].StartsWith("BIOS ", StringComparison.OrdinalIgnoreCase)
                ? lines[1][5..].Trim()
                : "—",
            _ => ""
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}