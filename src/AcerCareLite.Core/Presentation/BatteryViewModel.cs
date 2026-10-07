using System.Globalization;
using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Core.Presentation;

public sealed class BatteryViewModel : PageViewModel
{
    private const string NotAvailable = "Not available";

    private readonly IBatteryReader _reader;
    private readonly IBatteryHistoryStore _store;
    private readonly IPollingTimer _timer;
    private bool _refreshing;
    private int _tick;

    private double _percentValue, _healthValue;
    private string _percentText = "…", _stateText = "", _healthText = "…", _designText = "…", _fullText = "…",
        _remainingText = "…", _voltageText = "…", _rateText = "…", _cycleText = "…";
    private IReadOnlyList<BatterySample> _history = Array.Empty<BatterySample>();

    public BatteryViewModel(IBatteryReader reader, IBatteryHistoryStore store, IPollingTimer timer, AcerHealthCardViewModel? acer = null)
        : base("Battery", "\uE83F")
    {
        _reader = reader; _store = store; _timer = timer;
        Acer = acer;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Acer firmware health-mode card. Separate from the Windows battery reader; null when Acer support is not wired up.</summary>
    public AcerHealthCardViewModel? Acer { get; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public double PercentValue { get => _percentValue; private set => SetProperty(ref _percentValue, value); }
    public string PercentText { get => _percentText; private set => SetProperty(ref _percentText, value); }
    public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }
    public double HealthValue { get => _healthValue; private set => SetProperty(ref _healthValue, value); }
    public string HealthText { get => _healthText; private set => SetProperty(ref _healthText, value); }
    public string DesignText { get => _designText; private set => SetProperty(ref _designText, value); }
    public string FullText { get => _fullText; private set => SetProperty(ref _fullText, value); }
    public string RemainingText { get => _remainingText; private set => SetProperty(ref _remainingText, value); }
    public string VoltageText { get => _voltageText; private set => SetProperty(ref _voltageText, value); }
    public string RateText { get => _rateText; private set => SetProperty(ref _rateText, value); }
    public string CycleText { get => _cycleText; private set => SetProperty(ref _cycleText, value); }
    public IReadOnlyList<BatterySample> History { get => _history; private set => SetProperty(ref _history, value); }

    public override void OnNavigatedTo()
    {
        _tick = 0;
        _timer.Start(Interval);
        _ = RefreshAsync();
    }

    public override void OnNavigatedFrom() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var info = await Task.Run(() => Safe(_reader.Read, null));
            Apply(info);

            // History changes once a minute, so reload it every 6th tick (about 30 s) instead of every tick.
            if (_tick++ % 6 == 0)
            {
                var from = Clock().AddHours(-24);
                History = await Task.Run(() => Safe<IReadOnlyList<BatterySample>>(() => _store.Query(from), Array.Empty<BatterySample>()));
            }
        }
        catch { /* never crash the UI on a failed refresh */ }
        finally { _refreshing = false; }
    }

    private void Apply(BatteryInfo? b)
    {
        if (b == null)
        {
            PercentValue = HealthValue = 0;
            PercentText = HealthText = DesignText = FullText = RemainingText = VoltageText = RateText = CycleText = NotAvailable;
            StateText = "";
            return;
        }
        var ci = CultureInfo.CurrentCulture;
        PercentValue = b.Percent ?? 0;
        PercentText = b.Percent is { } p ? $"{p} %" : NotAvailable;
        StateText = b.Charging == true ? "Charging"
            : b.OnAc == true ? "Plugged in, not charging"
            : b.OnAc == false ? "On battery" : "";
        HealthValue = b.HealthPercent ?? 0;
        HealthText = b.HealthPercent is { } h ? $"{h.ToString("0", ci)} %" : NotAvailable;
        DesignText = Wh(b.DesignCapacityMwh);
        FullText = Wh(b.FullChargeCapacityMwh);
        RemainingText = Wh(b.RemainingMwh);
        VoltageText = b.VoltageMv is { } mv ? $"{(mv / 1000.0).ToString("0.0", ci)} V" : NotAvailable;
        RateText = b.RateMw switch
        {
            null => NotAvailable,
            > 0 => $"{(b.RateMw.Value / 1000.0).ToString("0.0", ci)} W in",
            < 0 => $"{(-b.RateMw.Value / 1000.0).ToString("0.0", ci)} W out",
            _ => "0 W"
        };
        CycleText = b.CycleCountReported ? b.CycleCount!.Value.ToString(ci) : "Not reported by this firmware";
    }

    private static string Wh(uint? mwh) =>
        mwh is { } v ? $"{(v / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} Wh" : NotAvailable;

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
}
