using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Presentation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AcerCareLite.Tests;

public class BatteryTests
{
    private static BatteryInfo Info(uint design = 50000, uint full = 40000, uint cycles = 0, int? percent = 80) =>
        new(percent, true, false, false, design, full, 30000, 17000, 0, cycles, "AP18E7M");

    private sealed class FakeReader : IBatteryReader
    {
        public BatteryInfo? Value = Info();
        public BatteryInfo? Read() => Value;
    }

    private sealed class MemoryStore : IBatteryHistoryStore
    {
        public List<BatterySample> Items = new();
        public void Add(BatterySample s) => Items.Add(s);
        public IReadOnlyList<BatterySample> Query(DateTimeOffset from) => Items.Where(i => i.Time >= from).ToList();
        public void Prune(DateTimeOffset olderThan) => Items.RemoveAll(i => i.Time < olderThan);
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
    public void Health_is_full_charge_over_design()
    {
        Assert.Equal(79.7, Info(58751, 46816).HealthPercent!.Value, 1);
        Assert.Null(Info(design: 0).HealthPercent);
    }

    [Fact]
    public void Zero_cycle_count_means_not_reported()
    {
        Assert.False(Info(cycles: 0).CycleCountReported);
        Assert.True(Info(cycles: 12).CycleCountReported);
    }

    [Fact]
    public async Task Recorder_stores_a_sample_and_skips_when_unreadable()
    {
        var reader = new FakeReader();
        var store = new MemoryStore();
        var recorder = new BatteryHistoryRecorder(reader, store, new FakeTimer());

        await recorder.SampleAsync();
        reader.Value = null;
        await recorder.SampleAsync();

        var sample = Assert.Single(store.Items);
        Assert.Equal(80, sample.Percent);
    }

    [Fact]
    public async Task ViewModel_formats_values_and_hides_unreported_cycles()
    {
        var vm = new BatteryViewModel(new FakeReader(), new MemoryStore(), new FakeTimer());
        await vm.RefreshAsync();
        Assert.Equal("80 %", vm.PercentText);
        Assert.Equal("80 %", vm.HealthText);
        Assert.Equal("Plugged in, not charging", vm.StateText);
        Assert.Equal("Not reported by this firmware", vm.CycleText);
    }

    [Fact]
    public async Task ViewModel_shows_not_available_when_reader_fails()
    {
        var reader = new FakeReader { Value = null };
        var vm = new BatteryViewModel(reader, new MemoryStore(), new FakeTimer());
        await vm.RefreshAsync();
        Assert.Equal("Not available", vm.PercentText);
    }

    [Fact]
    public void Sqlite_store_round_trips_and_prunes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"acl-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteBatteryHistoryStore(path);
            var now = DateTimeOffset.UtcNow;
            store.Add(new BatterySample(now.AddMinutes(-10), 70, false, false));
            store.Add(new BatterySample(now.AddMinutes(-5), 71, true, true));
            store.Add(new BatterySample(now.AddDays(-40), 10, false, false));

            Assert.Equal(2, store.Query(now.AddDays(-1)).Count);
            store.Prune(now.AddDays(-30));
            var all = store.Query(now.AddDays(-100));
            Assert.Equal(2, all.Count);
            Assert.Equal(new[] { 70, 71 }, all.Select(s => s.Percent));
            Assert.True(all[1].Charging);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }
}
