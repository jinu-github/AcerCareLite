using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Notifications;
using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Settings;
using Xunit;

namespace AcerCareLite.Tests;

public class Phase6bTests
{
    private static BatteryInfo Bat(int percent, bool onAc) =>
        new(percent, onAc, false, !onAc, 50000, 40000, 30000, 17000, 0, 0, null);

    private static readonly IReadOnlyList<DriveUsage> NoDrives = Array.Empty<DriveUsage>();
    private static IReadOnlyList<DriveUsage> Drive(int freePercent) =>
        new[] { new DriveUsage("C:", 100UL << 30, (ulong)freePercent << 30) };

    private static string[] Titles(IEnumerable<Alert> alerts) => alerts.Select(a => a.Title).ToArray();

    // ---- alert engine ----
    [Fact]
    public void Existing_low_battery_at_startup_does_not_alert_but_a_later_drop_does()
    {
        var engine = new AlertEngine();
        var s = new AppSettings();

        Assert.Empty(engine.Evaluate(s, Bat(15, false), NoDrives));
        Assert.Empty(engine.Evaluate(s, Bat(10, false), NoDrives));

        Assert.Equal(new[] { "Charger connected" }, Titles(engine.Evaluate(s, Bat(30, true), NoDrives)));
        var again = Titles(engine.Evaluate(s, Bat(15, false), NoDrives));
        Assert.Contains("Battery low", again);
        Assert.Contains("Charger disconnected", again);
    }

    [Fact]
    public void Low_battery_fires_once_per_drop()
    {
        var engine = new AlertEngine();
        var s = new AppSettings();
        engine.Evaluate(s, Bat(50, false), NoDrives);

        Assert.Equal(new[] { "Battery low" }, Titles(engine.Evaluate(s, Bat(19, false), NoDrives)));
        Assert.Empty(engine.Evaluate(s, Bat(18, false), NoDrives));
        Assert.Empty(engine.Evaluate(s, Bat(22, false), NoDrives)); // not yet 5 points above the level
    }

    [Fact]
    public void Charge_level_fires_when_plugged_in_at_the_level_and_re_arms_after_unplugging()
    {
        var engine = new AlertEngine();
        var s = new AppSettings();
        engine.Evaluate(s, Bat(70, true), NoDrives);

        Assert.Equal(new[] { "Battery at 80%" }, Titles(engine.Evaluate(s, Bat(80, true), NoDrives)));
        Assert.Empty(engine.Evaluate(s, Bat(81, true), NoDrives));

        engine.Evaluate(s, Bat(60, false), NoDrives);
        var back = Titles(engine.Evaluate(s, Bat(80, true), NoDrives));
        Assert.Contains("Battery at 80%", back);
        Assert.Contains("Charger connected", back);
    }

    [Fact]
    public void Low_storage_fires_once_per_drive_and_again_after_recovery()
    {
        var engine = new AlertEngine();
        var s = new AppSettings();

        Assert.Equal(new[] { "Low storage on C:" }, Titles(engine.Evaluate(s, null, Drive(5))));
        Assert.Empty(engine.Evaluate(s, null, Drive(4)));
        Assert.Empty(engine.Evaluate(s, null, Drive(20)));
        Assert.Equal(new[] { "Low storage on C:" }, Titles(engine.Evaluate(s, null, Drive(5))));
    }

    [Fact]
    public void Disabled_notifications_and_disabled_categories_stay_silent()
    {
        var off = new AlertEngine();
        var s = new AppSettings { NotificationsEnabled = false };
        Assert.Empty(off.Evaluate(s, null, Drive(5)));

        var noAc = new AlertEngine();
        var t = new AppSettings { NotifyAcChange = false };
        noAc.Evaluate(t, Bat(50, true), NoDrives);
        Assert.Empty(noAc.Evaluate(t, Bat(50, false), NoDrives));
    }

    // ---- notification service ----
    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class FakeBattery : IBatteryReader
    {
        public BatteryInfo? Value;
        public BatteryInfo? Read() => Value;
    }

    private sealed class FakeStorage : IStorageMonitor
    {
        public IReadOnlyList<DriveUsage> Read() => Array.Empty<DriveUsage>();
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<string> Titles = new();
        public void Notify(string title, string message) => Titles.Add(title);
    }

    private sealed class FakeTimer : IPollingTimer
    {
        public event EventHandler? Tick;
        public bool Running { get; private set; }
        public void Start(TimeSpan interval) => Running = true;
        public void Stop() => Running = false;
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public async Task Service_notifies_through_the_notifier_and_starts_its_timer()
    {
        var battery = new FakeBattery { Value = Bat(50, true) };
        var notifier = new FakeNotifier();
        var timer = new FakeTimer();
        var service = new NotificationService(battery, new FakeStorage(), new SettingsService(new MemoryStore()),
            new AlertEngine(), notifier, timer);

        await service.CheckAsync(); // seeds the state

        battery.Value = Bat(50, false);
        await service.CheckAsync();

        Assert.Contains("Charger disconnected", notifier.Titles);
    }

    [Fact]
    public void Service_start_begins_the_timer()
    {
        var timer = new FakeTimer();
        var service = new NotificationService(new FakeBattery(), new FakeStorage(), new SettingsService(new MemoryStore()),
            new AlertEngine(), new FakeNotifier(), timer);
        service.Start();
        Assert.True(timer.Running);
    }

    // ---- shell suspend/resume ----
    private sealed class TestPage : PageViewModel
    {
        public int Entered, Left;
        public TestPage(string title) : base(title, "\uE80F") { }
        public override void OnNavigatedTo() => Entered++;
        public override void OnNavigatedFrom() => Left++;
    }

    [Fact]
    public void Suspend_and_resume_pause_the_current_page_and_defer_navigation_hooks()
    {
        var a = new TestPage("A");
        var b = new TestPage("B");
        var shell = new ShellViewModel(new PageViewModel[] { a, b });
        Assert.Equal(1, a.Entered);

        shell.Suspend();
        Assert.Equal(1, a.Left);

        shell.CurrentPage = b;          // e.g. the tray opens Settings while the window is hidden
        Assert.Equal(0, b.Entered);

        shell.Resume();
        Assert.Equal(1, b.Entered);
        Assert.Equal(1, a.Left);
    }
}
