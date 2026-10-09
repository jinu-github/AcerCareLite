using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcerCareLite.Core.Battery;

namespace AcerCareLite.App.Controls;

/// <summary>Plain WPF area chart of battery percentage over the last 24 hours. Gaps (app closed) break the line.</summary>
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
        const double left = 34, right = 6, top = 8, bottom = 24;
        double w = ActualWidth - left - right, h = ActualHeight - top - bottom;
        if (w <= 10 || h <= 10) return;

        var gridPen = new Pen(Res("BorderSubtleBrush"), 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
        var subtle = Res("SubtleTextBrush");
        var accent = Res("AccentBrush");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        FormattedText Text(string s) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, subtle, dpi);

        foreach (var pct in new[] { 0, 25, 50, 75, 100 })
        {
            double y = top + h * (1 - pct / 100.0);
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + w, y));
            var t = Text(pct.ToString(CultureInfo.CurrentCulture));
            dc.DrawText(t, new Point(left - t.Width - 8, y - t.Height / 2));
        }
        dc.DrawText(Text("24 h ago"), new Point(left, top + h + 6));
        var now = Text("now");
        dc.DrawText(now, new Point(left + w - now.Width, top + h + 6));

        var samples = Samples;
        if (samples == null || samples.Count == 0)
        {
            var empty = Text("No history yet. The app records one sample a minute while it is running.");
            dc.DrawText(empty, new Point(left + (w - empty.Width) / 2, top + h / 2 - empty.Height / 2));
            return;
        }

        // Split into runs of points; a gap longer than MaxGap starts a new run.
        var end = DateTimeOffset.UtcNow;
        var start = end - Window;
        var runs = new List<List<Point>>();
        DateTimeOffset? prev = null;
        foreach (var s in samples.Where(s => s.Time >= start))
        {
            double x = left + w * ((s.Time - start).TotalSeconds / Window.TotalSeconds);
            double y = top + h * (1 - Math.Clamp(s.Percent, 0, 100) / 100.0);
            if (prev == null || s.Time - prev > MaxGap) runs.Add(new List<Point>());
            runs[^1].Add(new Point(x, y));
            prev = s.Time;
        }

        double baseline = top + h;
        var c = (accent as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var fill = new LinearGradientBrush(Color.FromArgb(70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            foreach (var run in runs)
            {
                lc.BeginFigure(run[0], false, false);
                for (int i = 1; i < run.Count; i++) lc.LineTo(run[i], true, true);

                if (run.Count < 2) continue;
                ac.BeginFigure(new Point(run[0].X, baseline), true, true);
                foreach (var p in run) ac.LineTo(p, false, false);
                ac.LineTo(new Point(run[^1].X, baseline), false, false);
            }
        }
        line.Freeze();
        area.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
    }
}
