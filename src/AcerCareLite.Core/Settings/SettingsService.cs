namespace AcerCareLite.Core.Settings;

/// <summary>Holds the current settings, saves every change and tells listeners.</summary>
public sealed class SettingsService
{
    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
        Current = store.Load().Normalized();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var next = change(Current).Normalized();
        if (next == Current) return;
        Current = next;
        try { _store.Save(next); }
        catch { /* keep running with the in-memory settings if the disk write fails */ }
        Changed?.Invoke(this, next);
    }
}
