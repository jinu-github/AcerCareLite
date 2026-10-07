using AcerCareLite.Core.Settings;

namespace AcerCareLite.Core.Presentation;

/// <summary>Every property saves immediately. The windows startup entry is the only one that can fail.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly IAutoStartService _autoStart;
    private readonly AppTheme _appliedTheme;
    private string _statusText = "";

    public SettingsViewModel(SettingsService settings, IAutoStartService autoStart) : base("Settings", "\uE713")
    {
        _settings = settings;
        _autoStart = autoStart;
        _appliedTheme = settings.Current.Theme;

        // The registry entry is the truth: it may have been removed from Task Manager or the Utilities page.
        var actual = autoStart.IsEnabled();
        if (actual != settings.Current.StartWithWindows)
            settings.Update(s => s with { StartWithWindows = actual });

        settings.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    private AppSettings S => _settings.Current;

    public IReadOnlyList<int> IntervalChoices => AppSettings.IntervalChoices;
    public IReadOnlyList<int> LowBatteryChoices => AppSettings.LowBatteryChoices;
    public IReadOnlyList<int> ChargeLevelChoices => AppSettings.ChargeLevelChoices;
    public IReadOnlyList<int> LowStorageChoices => AppSettings.LowStorageChoices;
    public IReadOnlyList<AppTheme> ThemeChoices { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<TemperatureUnit> TemperatureChoices { get; } = Enum.GetValues<TemperatureUnit>();

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ThemeNote => S.Theme != _appliedTheme ? "Restart AcerCareLite to apply the theme." : "";

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set
        {
            if (value == S.StartWithWindows) return;
            if (!_autoStart.Set(value))
            {
                StatusText = "Could not change the Windows startup entry.";
                OnPropertyChanged(nameof(StartWithWindows)); // snaps the switch back
                return;
            }
            StatusText = "";
            _settings.Update(s => s with { StartWithWindows = value });
        }
    }

    public bool StartMinimized { get => S.StartMinimized; set => _settings.Update(s => s with { StartMinimized = value }); }
    public bool MinimizeToTray { get => S.MinimizeToTray; set => _settings.Update(s => s with { MinimizeToTray = value }); }
    public int MonitoringIntervalSeconds { get => S.MonitoringIntervalSeconds; set => _settings.Update(s => s with { MonitoringIntervalSeconds = value }); }
    public bool NotificationsEnabled { get => S.NotificationsEnabled; set => _settings.Update(s => s with { NotificationsEnabled = value }); }
    public bool NotifyLowBattery { get => S.NotifyLowBattery; set => _settings.Update(s => s with { NotifyLowBattery = value }); }
    public int LowBatteryPercent { get => S.LowBatteryPercent; set => _settings.Update(s => s with { LowBatteryPercent = value }); }
    public bool NotifyChargeLevel { get => S.NotifyChargeLevel; set => _settings.Update(s => s with { NotifyChargeLevel = value }); }
    public int ChargeLevelPercent { get => S.ChargeLevelPercent; set => _settings.Update(s => s with { ChargeLevelPercent = value }); }
    public bool NotifyAcChange { get => S.NotifyAcChange; set => _settings.Update(s => s with { NotifyAcChange = value }); }
    public bool NotifyLowStorage { get => S.NotifyLowStorage; set => _settings.Update(s => s with { NotifyLowStorage = value }); }
    public int LowStoragePercentFree { get => S.LowStoragePercentFree; set => _settings.Update(s => s with { LowStoragePercentFree = value }); }
    public TemperatureUnit TemperatureUnit { get => S.TemperatureUnit; set => _settings.Update(s => s with { TemperatureUnit = value }); }
    public AppTheme Theme { get => S.Theme; set => _settings.Update(s => s with { Theme = value }); }
}
