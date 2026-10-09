using System.Windows;
using System.Windows.Controls;

namespace AcerCareLite.App.Controls;

/// <summary>
/// Two children on one row: the first on the left, the second on the right. When the second would squeeze the first
/// below what it needs, the second drops underneath instead. This keeps text from wrapping one word per line in narrow cards.
/// </summary>
public sealed class AdaptiveRowPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty StackGapProperty = DependencyProperty.Register(
        nameof(StackGap), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The least width the first child may be given beside the second. NaN means its full, unwrapped width.</summary>
    public static readonly DependencyProperty MinPrimaryWidthProperty = DependencyProperty.Register(
        nameof(MinPrimaryWidth), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Left offset of the second child when it sits underneath (to line up with text after an icon).</summary>
    public static readonly DependencyProperty StackedIndentProperty = DependencyProperty.Register(
        nameof(StackedIndent), typeof(double), typeof(AdaptiveRowPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public double StackGap { get => (double)GetValue(StackGapProperty); set => SetValue(StackGapProperty, value); }
    public double MinPrimaryWidth { get => (double)GetValue(MinPrimaryWidthProperty); set => SetValue(MinPrimaryWidthProperty, value); }
    public double StackedIndent { get => (double)GetValue(StackedIndentProperty); set => SetValue(StackedIndentProperty, value); }

    private bool _stacked;
    private double _secondaryWidth;
    private double _primaryHeight;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count == 0) return new Size();
        var primary = InternalChildren[0];
        var secondary = InternalChildren.Count > 1 ? InternalChildren[1] : null;

        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        primary.Measure(unbounded);
        var primaryNatural = primary.DesiredSize;

        Size secondaryNatural = default;
        if (secondary != null)
        {
            secondary.Measure(unbounded);
            secondaryNatural = secondary.DesiredSize;
        }

        // Nothing (or a collapsed element) in the second slot: the first child simply gets the row.
        if (secondary == null || secondaryNatural.Width <= 0 || secondary.Visibility == Visibility.Collapsed)
        {
            _stacked = false;
            _secondaryWidth = 0;
            primary.Measure(new Size(available.Width, double.PositiveInfinity));
            _primaryHeight = primary.DesiredSize.Height;
            return new Size(Finite(available.Width, primary.DesiredSize.Width), primary.DesiredSize.Height);
        }

        var need = double.IsNaN(MinPrimaryWidth) ? primaryNatural.Width : Math.Min(primaryNatural.Width, MinPrimaryWidth);
        var fits = double.IsInfinity(available.Width) || available.Width >= need + Gap + secondaryNatural.Width;
        _stacked = !fits;

        if (!_stacked)
        {
            _secondaryWidth = secondaryNatural.Width;
            var primaryWidth = double.IsInfinity(available.Width)
                ? primaryNatural.Width
                : available.Width - Gap - _secondaryWidth;
            primary.Measure(new Size(primaryWidth, double.PositiveInfinity));
            _primaryHeight = primary.DesiredSize.Height;
            var width = double.IsInfinity(available.Width)
                ? primary.DesiredSize.Width + Gap + _secondaryWidth
                : available.Width;
            return new Size(width, Math.Max(primary.DesiredSize.Height, secondaryNatural.Height));
        }

        primary.Measure(new Size(available.Width, double.PositiveInfinity));
        _primaryHeight = primary.DesiredSize.Height;
        var indent = Math.Min(StackedIndent, available.Width);
        secondary.Measure(new Size(Math.Max(0, available.Width - indent), double.PositiveInfinity));
        return new Size(available.Width, _primaryHeight + StackGap + secondary.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        var primary = InternalChildren[0];
        var secondary = InternalChildren.Count > 1 ? InternalChildren[1] : null;

        if (secondary == null || (!_stacked && _secondaryWidth <= 0))
        {
            primary.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            secondary?.Arrange(new Rect());
            return finalSize;
        }

        if (!_stacked)
        {
            var primaryWidth = Math.Max(0, finalSize.Width - Gap - _secondaryWidth);
            primary.Arrange(new Rect(0, 0, primaryWidth, finalSize.Height));
            secondary.Arrange(new Rect(finalSize.Width - _secondaryWidth, 0, _secondaryWidth, finalSize.Height));
            return finalSize;
        }

        var indent = Math.Min(StackedIndent, finalSize.Width);
        primary.Arrange(new Rect(0, 0, finalSize.Width, _primaryHeight));
        secondary.Arrange(new Rect(indent, _primaryHeight + StackGap, Math.Max(0, finalSize.Width - indent),
            Math.Max(0, finalSize.Height - _primaryHeight - StackGap)));
        return finalSize;
    }

    private static double Finite(double value, double fallback) => double.IsInfinity(value) ? fallback : value;
}
