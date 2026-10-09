using System.Windows;
using System.Windows.Controls;
using AcerCareLite.App.Controls;
using AcerCareLite.Core.Presentation;

namespace AcerCareLite.App.Views;

public partial class DashboardView : UserControl
{
    // At or above this width the four readings sit in one row; below it they fold into two rows of two.
    private const double FourColumnMinWidth = 900;

    public DashboardView()
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
        if (width > 0) StatGrid.Columns = width >= FourColumnMinWidth ? 4 : 2;
    }

    /// <summary>"View hardware details" opens the Hardware page the same way the sidebar does.</summary>
    private void HardwareLink_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is not ShellViewModel shell) return;
        var hardware = shell.Pages.OfType<HardwareViewModel>().FirstOrDefault();
        if (hardware != null) shell.CurrentPage = hardware;
    }
}
