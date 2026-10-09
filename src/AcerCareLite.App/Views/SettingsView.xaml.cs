using System.Windows;
using System.Windows.Controls;
using AcerCareLite.App.Controls;

namespace AcerCareLite.App.Views;

public partial class SettingsView : UserControl
{
    // Below this width the four cards stack in one column instead of two.
    private const double TwoColumnMinWidth = 760;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyLayout(ActualWidth);
            Motion.PageEnter((FrameworkElement)Content);
        };
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private bool? _twoColumns;

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var twoColumns = width >= TwoColumnMinWidth;
        if (_twoColumns == twoColumns) return;
        _twoColumns = twoColumns;

        Gap.Width = twoColumns ? new GridLength(16) : new GridLength(0);
        ColumnB.Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        LeftColumn.Children.Clear();
        RightColumn.Children.Clear();
        if (twoColumns)
        {
            // Heights come out close: General + Display on the left, Notifications + Monitoring on the right.
            LeftColumn.Children.Add(GeneralCard);
            LeftColumn.Children.Add(DisplayCard);
            RightColumn.Children.Add(NotificationsCard);
            RightColumn.Children.Add(MonitoringCard);
        }
        else
        {
            LeftColumn.Children.Add(GeneralCard);
            LeftColumn.Children.Add(MonitoringCard);
            LeftColumn.Children.Add(NotificationsCard);
            LeftColumn.Children.Add(DisplayCard);
        }
    }
}
