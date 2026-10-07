using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Presentation;
using Xunit;

namespace AcerCareLite.Tests;

public class DashboardViewModelTests
{
    private sealed class FakeTimer : IPollingTimer
    {
        public event EventHandler? Tick;
        public bool Running { get; private set; }
        public void Start(TimeSpan interval) => Running = true;
        public void Stop() => Running = false;
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Fake : ICpuMonitor, IGpuMonitor, IMemoryMonitor, IStorageMonitor, IPowerMonitor, ISystemInfoProvider
    {
        public bool Throw;
        double? ICpuMonitor.GetUsagePercent() => Throw ? throw new InvalidOperationException() : 37.4;
        double? IGpuMonitor.GetUsagePercent() => null;
        MemoryUsage? IMemoryMonitor.Read() => new(16UL << 30, 8UL << 30);
        IReadOnlyList<DriveUsage> IStorageMonitor.Read() => new[] { new DriveUsage("C:", 100UL << 30, 25UL << 30) };
        PowerStatus? IPowerMonitor.Read() => new(true, 80, true, false);
        SystemInfo? ISystemInfoProvider.Read() => new("Acer", "Nitro", "V2.21", "Windows");
    }

    private static (DashboardViewModel Vm, FakeTimer Timer, Fake Fake) Create()
    {
        var f = new Fake();
        var t = new FakeTimer();
        return (new DashboardViewModel(f, f, f, f, f, f, t), t, f);
    }

    [Fact]
    public async Task Refresh_fills_values()
    {
        var (vm, _, _) = Create();
        await vm.RefreshAsync();
        Assert.Equal("37 %", vm.CpuText);
        Assert.Equal("Not available", vm.GpuText);
        Assert.Equal(50, vm.MemoryValue);
        Assert.Equal(75, Assert.Single(vm.Drives).UsedPercent);
        Assert.Equal(80, vm.PowerValue);
        Assert.Equal("Plugged in, not charging", vm.PowerStateText);
    }

    [Fact]
    public async Task A_failing_monitor_shows_not_available_and_does_not_throw()
    {
        var (vm, _, fake) = Create();
        fake.Throw = true;
        await vm.RefreshAsync();
        Assert.Equal("Not available", vm.CpuText);
        Assert.Equal(50, vm.MemoryValue);
    }

    [Fact]
    public void Timer_runs_only_while_page_is_visible()
    {
        var (vm, timer, _) = Create();
        vm.OnNavigatedTo();
        Assert.True(timer.Running);
        vm.OnNavigatedFrom();
        Assert.False(timer.Running);
    }
}
