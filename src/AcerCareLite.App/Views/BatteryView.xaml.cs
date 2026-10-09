using System.Windows;
using System.Windows.Controls;
using AcerCareLite.App.Controls;

namespace AcerCareLite.App.Views;

public partial class BatteryView : UserControl
{
    // Below this width the three summary cards stack and the firmware log drops under the charge-limit text.
    private const double WideMinWidth = 760;

    public BatteryView()
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
        var wide = width >= WideMinWidth;

        TopGrid.Columns = wide ? 3 : 1;

        AcerGap.Width = wide ? new GridLength(20) : new GridLength(0);
        AcerColumnA.Width = wide ? new GridLength(3, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        AcerColumnB.Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(AcerLog, wide ? 2 : 0);
        Grid.SetRow(AcerLog, wide ? 0 : 1);
        AcerLog.Margin = wide ? new Thickness(0) : new Thickness(0, 16, 0, 0);
    }
}
