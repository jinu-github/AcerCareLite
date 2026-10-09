using System.Windows;
using System.Windows.Media;

namespace AcerCareLite.App.Controls;

/// <summary>
/// Draws a stroke-only icon from a geometry authored on a 24 x 24 grid (see Themes/Icons.xaml).
/// The whole 24 x 24 grid is scaled to the control's size, so every icon keeps the same visual weight and padding.
/// </summary>
public sealed class IconPath : FrameworkElement
{
    private const double Grid = 24;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(IconPath),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(IconPath),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Stroke width in grid units (24 = full icon width), so it scales with the icon.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(IconPath),
        new FrameworkPropertyMetadata(1.75, FrameworkPropertyMetadataOptions.AffectsRender));

    static IconPath()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(IconPath), new UIPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Grid, availableSize.Width), Math.Min(Grid, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var data = Data;
        var stroke = Stroke;
        if (data == null || stroke == null) return;

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        double scale = size / Grid;
        var pen = new Pen(stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, data);
        dc.Pop();
    }
}
