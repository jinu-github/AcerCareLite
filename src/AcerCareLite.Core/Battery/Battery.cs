namespace AcerCareLite.Core.Battery;

public sealed record BatteryInfo(
    int? Percent, bool? OnAc, bool? Charging, bool? Discharging,
    uint? DesignCapacityMwh, uint? FullChargeCapacityMwh, uint? RemainingMwh,
    uint? VoltageMv, int? RateMw, uint? CycleCount, string? DeviceName)
{
    /// <summary>Full-charge capacity relative to design capacity.</summary>
    public double? HealthPercent =>
        DesignCapacityMwh is > 0 && FullChargeCapacityMwh is { } full ? 100.0 * full / DesignCapacityMwh.Value : null;

    /// <summary>Some firmware (including this laptop's) always reports 0, which really means "not reported".</summary>
    public bool CycleCountReported => CycleCount is > 0;
}

public sealed record BatterySample(DateTimeOffset Time, int Percent, bool OnAc, bool Charging);

public interface IBatteryReader { BatteryInfo? Read(); }

public interface IBatteryHistoryStore
{
    void Add(BatterySample sample);
    IReadOnlyList<BatterySample> Query(DateTimeOffset from);
    void Prune(DateTimeOffset olderThan);
}
