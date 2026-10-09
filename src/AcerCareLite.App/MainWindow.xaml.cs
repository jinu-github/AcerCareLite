using System.ComponentModel;
using System.Reflection;
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

        VersionText.Text = $"v{AppVersion()}  ·  .NET {Environment.Version.Major}";
        UpdateWindowStateVisuals();
    }

    /// <summary>Set by the tray's Exit item (and Windows sign-out) so closing really closes.</summary>
    public bool AllowExit { get; set; }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateWindowStateVisuals();
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

    // --- Custom caption buttons. They use the same window-state and Close paths as the native ones,
    // --- so minimize-to-tray and close-to-tray behave exactly as before.

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    /// <summary>A borderless maximized window overhangs the screen by its resize border; pull the content back in.</summary>
    private void UpdateWindowStateVisuals()
    {
        var maximized = WindowState == WindowState.Maximized;
        RootBorder.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaxRestoreIcon.SetResourceReference(AcerCareLite.App.Controls.IconPath.DataProperty, maximized ? "Icon.Restore" : "Icon.Maximize");
        MaxRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaxRestoreButton, maximized ? "Restore" : "Maximize");
    }

    private static string AppVersion()
    {
        var asm = typeof(MainWindow).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            var clean = plus > 0 ? info[..plus] : info;
            // "1.0.0" reads as "1.0"
            return Version.TryParse(clean, out var v) ? $"{v.Major}.{v.Minor}" : clean;
        }
        var version = asm.GetName().Version;
        return version == null ? "" : $"{version.Major}.{version.Minor}";
    }
}
