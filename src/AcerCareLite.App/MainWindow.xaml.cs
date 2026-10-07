using System.ComponentModel;
using System.Windows;
using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Settings;

namespace AcerCareLite.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly SettingsService _settings;

    public MainWindow(ShellViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _shell = viewModel;
        _settings = settings;
    }

    /// <summary>Set by the tray's Exit item (and Windows sign-out) so closing really closes.</summary>
    public bool AllowExit { get; set; }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            _shell.Suspend();
            if (_settings.Current.MinimizeToTray) Hide();
        }
        else _shell.Resume();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowExit && _settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            _shell.Suspend();
            Hide();
        }
        base.OnClosing(e);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _shell.Resume();
        Activate();
    }
}
