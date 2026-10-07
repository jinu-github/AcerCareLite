using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcerCareLite.Core.Battery;

namespace AcerCareLite.App.Controls;

/// <summary>Plain WPF line chart of battery percentage over the last 24 hours. Gaps (app closed) break the line.</summary>
public sealed class BatteryChart : FrameworkElement
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<BatterySample>), typeof(BatteryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<BatterySample>? Samples
    {
        get => (IReadOnlyList<BatterySample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    private Brush Res(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);

    protected override void OnRender(DrawingContext dc)
    {
        const double left = 40, right = 8, top = 8, bottom = 22;
        double w = ActualWidth - left - right, h = ActualHeight - top - bottom;
        if (w <= 10 || h <= 10) return;

        var gridPen = new Pen(Res("AppBorderBrush"), 1);
        var subtle = Res("SubtleTextBrush");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        FormattedText Text(string s) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, subtle, dpi);

        foreach (var pct in new[] { 0, 50, 100 })
        {
            double y = top + h * (1 - pct / 100.0);
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + w, y));
            var t = Text($"{pct}%");
            dc.DrawText(t, new Point(left - t.Width - 6, y - t.Height / 2));
        }
        dc.DrawText(Text("24 h ago"), new Point(left, top + h + 4));
        var now = Text("now");
        dc.DrawText(now, new Point(left + w - now.Width, top + h + 4));

        var samples = Samples;
        if (samples == null || samples.Count == 0)
        {
            var empty = Text("No history yet. The app records one sample a minute while it is running.");
            dc.DrawText(empty, new Point(left + (w - empty.Width) / 2, top + h / 2 - empty.Height / 2));
            return;
        }

        var end = DateTimeOffset.UtcNow;
        var start = end - Window;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            DateTimeOffset? prev = null;
            foreach (var s in samples.Where(s => s.Time >= start))
            {
                double x = left + w * ((s.Time - start).TotalSeconds / Window.TotalSeconds);
                double y = top + h * (1 - Math.Clamp(s.Percent, 0, 100) / 100.0);
                var pt = new Point(x, y);
                if (prev == null || s.Time - prev > MaxGap) ctx.BeginFigure(pt, false, false);
                else ctx.LineTo(pt, true, true);
                prev = s.Time;
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(Res("AccentBrush"), 2) { LineJoin = PenLineJoin.Round }, geo);
    }
}
