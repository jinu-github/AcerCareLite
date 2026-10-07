using System.Globalization;
using AcerCareLite.Core.Hardware;
using AcerCareLite.Core.Monitoring;
using CommunityToolkit.Mvvm.Input;

namespace AcerCareLite.Core.Presentation;

public sealed record HardwareRow(string Title, string Detail);

/// <summary>Mostly static information: loaded once when the page opens, refreshed on request. No polling.</summary>
public sealed class HardwareViewModel : PageViewModel
{
    private const string NotAvailable = "Not available";
    private static readonly IReadOnlyList<HardwareRow> NoData = new[] { new HardwareRow(NotAvailable, "") };

    private readonly IHardwareInfoProvider _provider;
    private bool _loaded, _loading;
    private IReadOnlyList<HardwareRow> _cpu = NoData, _gpu = NoData, _memory = NoData, _disks = NoData;
    private string _statusText = "";

    public HardwareViewModel(IHardwareInfoProvider provider) : base("Hardware", "\uE7F4")
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IReadOnlyList<HardwareRow> Cpu { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public IReadOnlyList<HardwareRow> Gpu { get => _gpu; private set => SetProperty(ref _gpu, value); }
    public IReadOnlyList<HardwareRow> Memory { get => _memory; private set => SetProperty(ref _memory, value); }
    public IReadOnlyList<HardwareRow> Disks { get => _disks; private set => SetProperty(ref _disks, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public override void OnNavigatedTo()
    {
        if (!_loaded) _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        StatusText = "Loading…";
        try
        {
            HardwareSnapshot? snap;
            try { snap = await Task.Run(_provider.Read); } catch { snap = null; }
            Apply(snap);
            _loaded = true;
        }
        finally
        {
            _loading = false;
            StatusText = "";
        }
    }

    private void Apply(HardwareSnapshot? s)
    {
        var ci = CultureInfo.CurrentCulture;
        Cpu = Rows(s?.Cpus, c => new HardwareRow(c.Name,
            $"{c.Cores} cores, {c.LogicalProcessors} threads, up to {(c.MaxClockMhz / 1000.0).ToString("0.0", ci)} GHz"));
        Gpu = Rows(s?.Gpus, g => new HardwareRow(g.Name,
            $"Driver {g.DriverVersion}, " + (g.VramBytes is { } v ? $"{ByteFormat.Gb(v)} GB VRAM" : "VRAM not reported")));
        Memory = Rows(s?.Memory, m => new HardwareRow($"{m.Slot}  {ByteFormat.Gb(m.CapacityBytes)} GB",
            string.Join(", ", new[] { m.SpeedMhz is { } sp ? $"{sp} MHz" : "", m.Manufacturer }.Where(x => x.Length > 0))));
        Disks = Rows(s?.Disks, d => new HardwareRow(d.Model, $"{ByteFormat.Gb(d.SizeBytes)} GB, {d.MediaType}, {d.BusType}"));
    }

    private static IReadOnlyList<HardwareRow> Rows<T>(IReadOnlyList<T>? items, Func<T, HardwareRow> map) =>
        items == null || items.Count == 0 ? NoData : items.Select(map).ToList();
}
