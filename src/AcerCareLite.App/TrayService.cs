using System.Windows;
using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Notifications;
using AcerCareLite.Core.Presentation;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AcerCareLite.App;

/// <summary>
/// Notification-area icon. It does no polling: tooltip and menu text are read when you hover or open the menu.
/// Also the notifier that shows alert balloons.
/// </summary>
public sealed class TrayService : INotifier, IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly Forms.ToolStripMenuItem _batteryItem = new() { Enabled = false };
    private readonly Forms.ToolStripMenuItem _loadItem = new() { Enabled = false };
    private readonly ICpuMonitor _cpu;
    private readonly IGpuMonitor _gpu;
    private readonly IBatteryReader _battery;
    private readonly ShellViewModel _shell;
    private readonly MainWindow _window;
    private DateTime _lastHover = DateTime.MinValue;

    public TrayService(ICpuMonitor cpu, IGpuMonitor gpu, IBatteryReader battery, ShellViewModel shell, MainWindow window)
    {
        _cpu = cpu; _gpu = gpu; _battery = battery; _shell = shell; _window = window;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_batteryItem);
        menu.Items.Add(_loadItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open dashboard", null, (_, _) => Open("Dashboard"));
        menu.Items.Add("Battery settings", null, (_, _) => Open("Battery"));
        menu.Items.Add("Settings", null, (_, _) => Open("Settings"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        menu.Opening += (_, _) => RefreshMenu();

        _icon.ContextMenuStrip = menu;
        _icon.Text = "AcerCareLite";
        _icon.Icon = LoadIcon();
        _icon.MouseMove += (_, _) => RefreshTooltip();
        _icon.DoubleClick += (_, _) => Open("Dashboard");
    }

    public void Show() => _icon.Visible = true;

    public void Notify(string title, string message)
    {
        if (_icon.Visible) _icon.ShowBalloonTip(6000, title, message, Forms.ToolTipIcon.Info);
    }

    private void Open(string pageTitle)
    {
        var page = _shell.Pages.FirstOrDefault(p => p.Title == pageTitle);
        if (page != null) _shell.CurrentPage = page;
        _window.ShowFromTray();
    }

    private void Exit()
    {
        _window.AllowExit = true;
        Application.Current.Shutdown();
    }

    private void RefreshMenu()
    {
        var (battery, load) = ReadInfo();
        _batteryItem.Text = battery;
        _loadItem.Text = load;
    }

    private void RefreshTooltip()
    {
        if ((DateTime.UtcNow - _lastHover).TotalSeconds < 3) return;
        _lastHover = DateTime.UtcNow;
        var (battery, load) = ReadInfo();
        _icon.Text = $"AcerCareLite\n{battery}\n{load}";
    }

    private (string Battery, string Load) ReadInfo()
    {
        var battery = "Battery: not available";
        try
        {
            var b = _battery.Read();
            if (b?.Percent is { } p)
                battery = $"Battery: {p}% ({(b.Charging == true ? "charging" : b.OnAc == true ? "plugged in" : "on battery")})";
        }
        catch { }

        string cpu = "…", gpu = "…";
        try { if (_cpu.GetUsagePercent() is { } c) cpu = $"{c:0}%"; } catch { }
        try { if (_gpu.GetUsagePercent() is { } g) gpu = $"{g:0}%"; } catch { }
        return (battery, $"CPU {cpu}  |  GPU {gpu}");
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path != null && Drawing.Icon.ExtractAssociatedIcon(path) is { } icon) return icon;
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
