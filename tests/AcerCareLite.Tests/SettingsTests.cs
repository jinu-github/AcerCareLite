using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Settings;
using Xunit;

namespace AcerCareLite.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "acl-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Stored = new();
        public int Saves;
        public AppSettings Load() => Stored;
        public void Save(AppSettings settings) { Stored = settings; Saves++; }
    }

    private sealed class FakeAutoStart : IAutoStartService
    {
        public bool Enabled, SetResult = true;
        public bool IsEnabled() => Enabled;
        public bool Set(bool enabled) { if (SetResult) Enabled = enabled; return SetResult; }
    }

    [Fact]
    public void Defaults_are_dark_theme_and_sensible_alerts()
    {
        var s = new AppSettings();
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.Equal(2, s.MonitoringIntervalSeconds);
        Assert.Equal(20, s.LowBatteryPercent);
        Assert.Equal(80, s.ChargeLevelPercent);
        Assert.False(s.StartMinimized);
    }

    [Fact]
    public void Normalized_snaps_odd_values_to_the_offered_choices()
    {
        var s = new AppSettings { MonitoringIntervalSeconds = 7, LowBatteryPercent = 22, ChargeLevelPercent = 5, LowStoragePercentFree = 99 }.Normalized();
        Assert.Equal(5, s.MonitoringIntervalSeconds);
        Assert.Equal(20, s.LowBatteryPercent);
        Assert.Equal(70, s.ChargeLevelPercent);
        Assert.Equal(20, s.LowStoragePercentFree);
    }

    [Fact]
    public void Json_store_round_trips_and_survives_a_corrupt_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);

        store.Save(new AppSettings { Theme = AppTheme.Light, StartMinimized = true, MonitoringIntervalSeconds = 10 });
        Assert.Contains("Light", File.ReadAllText(path));
        var loaded = store.Load();
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.True(loaded.StartMinimized);
        Assert.Equal(10, loaded.MonitoringIntervalSeconds);

        File.WriteAllText(path, "{not json");
        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Service_saves_and_notifies_only_on_real_changes()
    {
        var store = new MemoryStore();
        var service = new SettingsService(store);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        service.Update(s => s with { StartMinimized = true });
        service.Update(s => s with { StartMinimized = true });

        Assert.Equal(1, changes);
        Assert.Equal(1, store.Saves);
        Assert.True(service.Current.StartMinimized);
    }

    [Fact]
    public void ViewModel_changes_are_saved_and_theme_note_appears()
    {
        var store = new MemoryStore();
        var vm = new SettingsViewModel(new SettingsService(store), new FakeAutoStart());
        Assert.Equal("", vm.ThemeNote);

        vm.Theme = AppTheme.Light;
        vm.LowBatteryPercent = 30;

        Assert.Equal(AppTheme.Light, store.Stored.Theme);
        Assert.Equal(30, store.Stored.LowBatteryPercent);
        Assert.Contains("Restart", vm.ThemeNote);
    }

    [Fact]
    public void Start_with_windows_changes_the_registry_entry_and_reports_failure()
    {
        var store = new MemoryStore();
        var auto = new FakeAutoStart();
        var vm = new SettingsViewModel(new SettingsService(store), auto);

        vm.StartWithWindows = true;
        Assert.True(auto.Enabled);
        Assert.True(store.Stored.StartWithWindows);

        auto.SetResult = false;
        vm.StartWithWindows = false;
        Assert.True(store.Stored.StartWithWindows); // unchanged because the registry write failed
        Assert.Contains("Could not", vm.StatusText);
    }

    [Fact]
    public void Registry_state_wins_over_the_saved_value_at_startup()
    {
        var store = new MemoryStore { Stored = new AppSettings { StartWithWindows = true } };
        var auto = new FakeAutoStart { Enabled = false }; // entry was removed from Task Manager
        var service = new SettingsService(store);

        _ = new SettingsViewModel(service, auto);

        Assert.False(service.Current.StartWithWindows);
    }
}
