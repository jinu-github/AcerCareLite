using AcerCareLite.Core.Hardware;
using AcerCareLite.Core.Presentation;
using Xunit;

namespace AcerCareLite.Tests;

public class HardwareTests
{
    private sealed class FakeProvider : IHardwareInfoProvider
    {
        public bool Throw;
        public int Calls;
        public HardwareSnapshot Read()
        {
            Calls++;
            if (Throw) throw new InvalidOperationException();
            return new HardwareSnapshot(
                new[] { new CpuInfo("Test CPU", 6, 12, 4500) },
                new[] { new GpuInfo("Test GPU", "1.2.3", null) },
                Array.Empty<MemoryModuleInfo>(),
                new[] { new DiskInfo("Test SSD", 512UL << 30, "SSD", "NVMe") });
        }
    }

    [Fact]
    public async Task Load_builds_rows_and_marks_empty_sections()
    {
        var vm = new HardwareViewModel(new FakeProvider());
        await vm.LoadAsync();

        Assert.Equal("Test CPU", Assert.Single(vm.Cpu).Title);
        Assert.Contains("6 cores, 12 threads", vm.Cpu[0].Detail);
        Assert.Contains("VRAM not reported", vm.Gpu[0].Detail);
        Assert.Equal("Not available", Assert.Single(vm.Memory).Title);
        Assert.Contains("SSD", vm.Disks[0].Detail);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task Provider_failure_shows_not_available_everywhere()
    {
        var vm = new HardwareViewModel(new FakeProvider { Throw = true });
        await vm.LoadAsync();
        Assert.Equal("Not available", Assert.Single(vm.Cpu).Title);
        Assert.Equal("Not available", Assert.Single(vm.Disks).Title);
    }

    [Fact]
    public async Task Page_loads_once_until_refresh_is_pressed()
    {
        var provider = new FakeProvider();
        var vm = new HardwareViewModel(provider);
        await vm.LoadAsync();
        vm.OnNavigatedTo();
        await Task.Delay(50);
        Assert.Equal(1, provider.Calls);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, provider.Calls);
    }
}
