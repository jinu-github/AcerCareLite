using System.Windows;
using System.Windows.Controls;
using AcerCareLite.App.Controls;

namespace AcerCareLite.App.Views;

public partial class UtilitiesView : UserControl
{
    // Below this width the three cards stack instead of sitting in one row.
    private const double ThreeColumnMinWidth = 780;

    public UtilitiesView()
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
        if (width > 0) CardGrid.Columns = width >= ThreeColumnMinWidth ? 3 : 1;
    }
}
