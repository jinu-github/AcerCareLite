using System.Windows;
using System.Windows.Controls;
using AcerCareLite.App.Controls;

namespace AcerCareLite.App.Views;

public partial class HardwareView : UserControl
{
    // Below this width the four cards stack in one column instead of two.
    private const double TwoColumnMinWidth = 720;

    public HardwareView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyLayout(ActualWidth);
            Motion.PageEnter((FrameworkElement)Content);
        };
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var twoColumns = width >= TwoColumnMinWidth;

        Gap.Width = twoColumns ? new GridLength(16) : new GridLength(0);
        ColumnB.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Place(CpuCard, 0, 0, bottom: 16);
        Place(GpuCard, twoColumns ? 0 : 1, twoColumns ? 2 : 0, bottom: 16);
        Place(MemoryCard, twoColumns ? 1 : 2, 0, bottom: twoColumns ? 0 : 16);
        Place(DiskCard, twoColumns ? 1 : 3, twoColumns ? 2 : 0, bottom: 0);
    }

    private static void Place(FrameworkElement card, int row, int column, double bottom)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        card.Margin = new Thickness(0, 0, 0, bottom);
    }
}
