using System.Globalization;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Core.Presentation;

/// <summary>Polls only while the page is visible. All hardware access is behind the monitor interfaces.</summary>
public sealed class DashboardViewModel : PageViewModel
{
    private const string NotAvailable = "Not available";

    private readonly ICpuMonitor _cpu;
    private readonly IGpuMonitor _gpu;
    private readonly IMemoryMonitor _memory;
    private readonly IStorageMonitor _storage;
    private readonly IPowerMonitor _power;
    private readonly ISystemInfoProvider _system;
    private readonly IPollingTimer _timer;
    private bool _refreshing;

    private double _cpuValue, _gpuValue, _memoryValue, _powerValue;
    private string _cpuText = "…", _gpuText = "…", _memoryText = "…", _powerText = "…", _powerStateText = "", _storageNote = "", _systemInfoText = "";
    private IReadOnlyList<DriveUsage> _drives = Array.Empty<DriveUsage>();

    public DashboardViewModel(ICpuMonitor cpu, IGpuMonitor gpu, IMemoryMonitor memory, IStorageMonitor storage,
        IPowerMonitor power, ISystemInfoProvider system, IPollingTimer timer) : base("Dashboard", "\uE80F")
    {
        _cpu = cpu; _gpu = gpu; _memory = memory; _storage = storage; _power = power; _system = system; _timer = timer;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public double CpuValue { get => _cpuValue; private set => SetProperty(ref _cpuValue, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public double GpuValue { get => _gpuValue; private set => SetProperty(ref _gpuValue, value); }
    public string GpuText { get => _gpuText; private set => SetProperty(ref _gpuText, value); }
    public double MemoryValue { get => _memoryValue; private set => SetProperty(ref _memoryValue, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public double PowerValue { get => _powerValue; private set => SetProperty(ref _powerValue, value); }
    public string PowerText { get => _powerText; private set => SetProperty(ref _powerText, value); }
    public string PowerStateText { get => _powerStateText; private set => SetProperty(ref _powerStateText, value); }
    public IReadOnlyList<DriveUsage> Drives { get => _drives; private set => SetProperty(ref _drives, value); }
    public string StorageNote { get => _storageNote; private set => SetProperty(ref _storageNote, value); }
    public string SystemInfoText { get => _systemInfoText; private set => SetProperty(ref _systemInfoText, value); }

    public override void OnNavigatedTo()
    {
        _timer.Start(Interval);
        _ = RefreshAsync();
        if (SystemInfoText.Length == 0) _ = LoadSystemInfoAsync();
    }

    public override void OnNavigatedFrom() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snap = await Task.Run(() => (
                Cpu: Safe(_cpu.GetUsagePercent, null),
                Gpu: Safe(_gpu.GetUsagePercent, null),
                Mem: Safe(_memory.Read, null),
                Drives: Safe<IReadOnlyList<DriveUsage>>(_storage.Read, Array.Empty<DriveUsage>()),
                Power: Safe(_power.Read, null)));
            Apply(snap.Cpu, snap.Gpu, snap.Mem, snap.Drives, snap.Power);
        }
        catch { /* a failed refresh must never take the app down; the next tick tries again */ }
        finally { _refreshing = false; }
    }

    private async Task LoadSystemInfoAsync()
    {
        try
        {
            var info = await Task.Run(() => Safe(_system.Read, null));
            SystemInfoText = info == null
                ? NotAvailable
                : $"{info.Manufacturer} {info.Model}\nBIOS {info.BiosVersion}\n{info.OsDescription}";
        }
        catch { SystemInfoText = NotAvailable; }
    }

    private void Apply(double? cpu, double? gpu, MemoryUsage? mem, IReadOnlyList<DriveUsage> drives, PowerStatus? power)
    {
        var ci = CultureInfo.CurrentCulture;
        CpuValue = cpu ?? 0;
        CpuText = cpu is { } c ? $"{c.ToString("0", ci)} %" : NotAvailable;
        GpuValue = gpu ?? 0;
        GpuText = gpu is { } g ? $"{g.ToString("0", ci)} %" : NotAvailable;
        MemoryValue = mem?.Percent ?? 0;
        MemoryText = mem?.Text ?? NotAvailable;
        Drives = drives;
        StorageNote = drives.Count == 0 ? NotAvailable : "";

        if (power == null) { PowerValue = 0; PowerText = NotAvailable; PowerStateText = ""; }
        else if (!power.HasBattery) { PowerValue = 0; PowerText = "No battery"; PowerStateText = ""; }
        else
        {
            PowerValue = power.Percent ?? 0;
            PowerText = power.Percent is { } p ? $"{p} %" : NotAvailable;
            PowerStateText = power.Charging == true ? "Charging"
                : power.OnAc == true ? "Plugged in, not charging"
                : power.OnAc == false ? "On battery" : "";
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
}
