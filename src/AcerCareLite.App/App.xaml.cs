using System.IO;
using System.Windows;
using AcerCareLite.Acer;
using AcerCareLite.Core.Acer;
using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Notifications;
using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Settings;
using AcerCareLite.Core.Utilities;
using AcerCareLite.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AcerCareLite.App;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _singleInstance;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy at a time. A second launch simply exits.
        _singleInstance = new Mutex(true, @"Local\AcerCareLite.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices(ConfigureServices)
            .Build();
        await _host.StartAsync();

        var sp = _host.Services;
        var settings = sp.GetRequiredService<SettingsService>();
        var dashboard = sp.GetRequiredService<DashboardViewModel>();
        var battery = sp.GetRequiredService<BatteryViewModel>();

        // The monitoring interval applies to the pages that poll while visible.
        void ApplyInterval(AppSettings s)
        {
            var interval = TimeSpan.FromSeconds(s.MonitoringIntervalSeconds);
            dashboard.Interval = interval;
            battery.Interval = interval;
        }
        ApplyInterval(settings.Current);
        settings.Changed += (_, s) => ApplyInterval(s);

        ApplyTheme(settings.Current.Theme);
        sp.GetRequiredService<BatteryHistoryRecorder>().Start();

        var window = sp.GetRequiredService<MainWindow>();
        _window = window;
        MainWindow = window;

        sp.GetRequiredService<TrayService>().Show();
        sp.GetRequiredService<NotificationService>().Start();

        var shell = sp.GetRequiredService<ShellViewModel>();
        var current = settings.Current;
        if (current.StartMinimized && current.MinimizeToTray)
        {
            shell.Suspend(); // lives in the tray; the window is created but never shown
        }
        else if (current.StartMinimized)
        {
            shell.Suspend();
            window.WindowState = WindowState.Minimized;
            window.Show();
        }
        else
        {
            window.Show();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (_window != null) _window.AllowExit = true; // never block Windows sign-out or shutdown
        base.OnSessionEnding(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    // Palette first, shared styles second: the styles look their brushes up in the palette.
    private void ApplyTheme(AppTheme theme)
    {
        foreach (var name in new[] { theme.ToString(), "Styles" })
        {
            // Attach first, load second, so a dictionary can see the ones merged before it.
            var dictionary = new ResourceDictionary();
            Resources.MergedDictionaries.Add(dictionary);
            dictionary.Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute);
        }
    }

    // Composition root. The UI never touches WMI or the registry directly.
    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcerCareLite");

        services.AddWindowsMonitoring();
        // Transient: every consumer needs its own timer instance.
        services.AddTransient<IPollingTimer, DispatcherPollingTimer>();

        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(Path.Combine(appData, "settings.json")));
        services.AddSingleton<SettingsService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<DashboardViewModel>());

        services.AddSingleton<IBatteryHistoryStore>(_ => new SqliteBatteryHistoryStore(Path.Combine(appData, "battery.db")));
        services.AddSingleton<BatteryHistoryRecorder>();
        // Acer firmware read (Phase 7a): read-only, on demand, through a separate elevated helper. The app itself stays non-elevated.
        services.AddSingleton<ElevatedHelperChannel>(_ => new ElevatedHelperChannel());
        services.AddSingleton<IAcerHelperChannel>(sp => sp.GetRequiredService<ElevatedHelperChannel>());
        services.AddSingleton<IAcerHelperWriteChannel>(sp => sp.GetRequiredService<ElevatedHelperChannel>());
        services.AddSingleton<IAcerBatteryHealthService>(sp => new AcerBatteryHealthService(sp.GetRequiredService<IAcerHelperChannel>()));
        services.AddSingleton<IHelperLocationGate>(_ => new HelperLocationGate(Path.Combine(AppContext.BaseDirectory, ElevatedHelperChannel.HelperFileName)));
        // Phase 7b-1: changing the setting. Present in every build, but WriteGate refuses unless the build was made with write
        // support (-p:AcerWriteEnabled=true), the helper is in a protected location, and a fresh read exists. Every attempt is audited.
        services.AddSingleton<IAcerWriteAuditLog>(_ => new FileAcerWriteAuditLog());
        services.AddSingleton<IAcerHealthWriteService>(sp => new AcerHealthWriteService(
            sp.GetRequiredService<IAcerHelperWriteChannel>(), sp.GetRequiredService<IAcerBatteryHealthService>(),
            sp.GetRequiredService<IHelperLocationGate>(), sp.GetRequiredService<IAcerWriteAuditLog>()));
        services.AddSingleton<AcerHealthCardViewModel>();
        services.AddSingleton<BatteryViewModel>();
        services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<BatteryViewModel>());

        services.AddSingleton<HardwareViewModel>();
        services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<HardwareViewModel>());

        services.AddSingleton<IConfirmationService, MessageBoxConfirmationService>();
        services.AddSingleton<ITempCleaner>(_ =>
        {
            try { return new TempFileCleaner(Path.GetTempPath()); }
            catch (ArgumentException) { return new DisabledTempCleaner(); }
        });
        services.AddSingleton<UtilitiesViewModel>();
        services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<UtilitiesViewModel>());

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<SettingsViewModel>());

        services.AddSingleton<AlertEngine>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TrayService>());

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
