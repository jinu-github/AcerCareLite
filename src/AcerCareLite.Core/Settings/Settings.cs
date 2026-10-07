namespace AcerCareLite.Core.Settings;

public enum AppTheme { Dark, Light }
public enum TemperatureUnit { Celsius, Fahrenheit }

public sealed record AppSettings
{
    public static readonly int[] IntervalChoices = { 2, 5, 10, 30 };
    public static readonly int[] LowBatteryChoices = { 10, 15, 20, 30 };
    public static readonly int[] ChargeLevelChoices = { 70, 80, 90, 100 };
    public static readonly int[] LowStorageChoices = { 5, 10, 15, 20 };

    public bool StartWithWindows { get; init; }
    public bool StartMinimized { get; init; }
    public bool MinimizeToTray { get; init; }
    public int MonitoringIntervalSeconds { get; init; } = 2;
    public bool NotificationsEnabled { get; init; } = true;
    public bool NotifyLowBattery { get; init; } = true;
    public int LowBatteryPercent { get; init; } = 20;
    public bool NotifyChargeLevel { get; init; } = true;
    public int ChargeLevelPercent { get; init; } = 80;
    public bool NotifyAcChange { get; init; } = true;
    public bool NotifyLowStorage { get; init; } = true;
    public int LowStoragePercentFree { get; init; } = 10;
    public TemperatureUnit TemperatureUnit { get; init; } = TemperatureUnit.Celsius;
    public AppTheme Theme { get; init; } = AppTheme.Dark;

    /// <summary>Snaps hand-edited values to the choices the UI offers, so a bad settings file cannot cause odd behavior.</summary>
    public AppSettings Normalized() => this with
    {
        MonitoringIntervalSeconds = Nearest(MonitoringIntervalSeconds, IntervalChoices),
        LowBatteryPercent = Nearest(LowBatteryPercent, LowBatteryChoices),
        ChargeLevelPercent = Nearest(ChargeLevelPercent, ChargeLevelChoices),
        LowStoragePercentFree = Nearest(LowStoragePercentFree, LowStorageChoices)
    };

    private static int Nearest(int value, int[] choices) => choices.OrderBy(c => Math.Abs(c - value)).First();
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>Adds or removes this app's own "start with Windows" entry for the current user.</summary>
public interface IAutoStartService
{
    bool IsEnabled();
    bool Set(bool enabled);
}
