using System.Globalization;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Utilities;
using CommunityToolkit.Mvvm.Input;

namespace AcerCareLite.Core.Presentation;

public sealed record StartupRow(StartupEntry Entry)
{
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Details => $"{Entry.SourceText}  |  {Status}";
    public bool CanToggle => Entry.CanModify;
    public string ButtonText => Entry.Enabled ? "Disable" : "Enable";

    private string Status => (Entry.Enabled ? "Enabled" : "Disabled") + (Entry.CanModify ? "" : " (needs administrator to change)");
}

/// <summary>
/// Every destructive action needs a button press and a confirmation. Nothing runs automatically.
/// The temp scan can be slow on big folders, so it runs once per visit to Refresh and the buttons stay off until it finishes.
/// </summary>
public sealed class UtilitiesViewModel : PageViewModel
{
    private const string NotAvailable = "Not available";

    private readonly IStartupAppService _startup;
    private readonly ITempCleaner _temp;
    private readonly IRecycleBinService _recycle;
    private readonly IStorageMonitor _storage;
    private readonly IConfirmationService _confirm;

    private bool _loading, _loaded, _isScanning;
    private TempScanResult _lastTemp = new(0, 0);
    private RecycleBinInfo? _lastRecycle;

    private IReadOnlyList<StartupRow> _startupRows = Array.Empty<StartupRow>();
    private IReadOnlyList<DriveUsage> _drives = Array.Empty<DriveUsage>();
    private string _tempText = "…", _recycleText = "…", _reclaimText = "", _resultText = "";

    public UtilitiesViewModel(IStartupAppService startup, ITempCleaner temp, IRecycleBinService recycle,
        IStorageMonitor storage, IConfirmationService confirm) : base("Utilities", "\uE74D")
    {
        _startup = startup; _temp = temp; _recycle = recycle; _storage = storage; _confirm = confirm;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ToggleStartupCommand = new AsyncRelayCommand<StartupRow>(ToggleAsync);
        CleanTempCommand = new AsyncRelayCommand(CleanTempAsync, () => !IsScanning);
        EmptyRecycleCommand = new AsyncRelayCommand(EmptyRecycleAsync, () => !IsScanning);
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<StartupRow> ToggleStartupCommand { get; }
    public IAsyncRelayCommand CleanTempCommand { get; }
    public IAsyncRelayCommand EmptyRecycleCommand { get; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            CleanTempCommand.NotifyCanExecuteChanged();
            EmptyRecycleCommand.NotifyCanExecuteChanged();
        }
    }

    public IReadOnlyList<StartupRow> StartupRows { get => _startupRows; private set => SetProperty(ref _startupRows, value); }
    public IReadOnlyList<DriveUsage> Drives { get => _drives; private set => SetProperty(ref _drives, value); }
    public string TempText { get => _tempText; private set => SetProperty(ref _tempText, value); }
    public string RecycleText { get => _recycleText; private set => SetProperty(ref _recycleText, value); }
    public string ReclaimText { get => _reclaimText; private set => SetProperty(ref _reclaimText, value); }
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    public override void OnNavigatedTo()
    {
        if (!_loaded) _ = LoadAsync();
    }

    /// <summary>Full load, including the temp scan.</summary>
    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        IsScanning = true;
        TempText = "Scanning temporary files…";
        try
        {
            await ReloadLightAsync();
            _lastTemp = await Task.Run(() => Safe(_temp.Scan, new TempScanResult(0, 0)));
            TempText = $"{_lastTemp.Files} files ({Size(_lastTemp.Bytes)}) older than 24 hours in your temp folder";
            UpdateReclaim();
            _loaded = true;
        }
        catch { /* leave the previous values in place */ }
        finally
        {
            _loading = false;
            IsScanning = false;
        }
    }

    /// <summary>Quick parts only (startup list, Recycle Bin, drives). Does not rescan temp files.</summary>
    private async Task ReloadLightAsync()
    {
        var snap = await Task.Run(() => (
            Startup: Safe<IReadOnlyList<StartupEntry>>(_startup.List, Array.Empty<StartupEntry>()),
            Recycle: Safe(_recycle.Query, null),
            Drives: Safe<IReadOnlyList<DriveUsage>>(_storage.Read, Array.Empty<DriveUsage>())));

        _lastRecycle = snap.Recycle;
        StartupRows = snap.Startup.Select(e => new StartupRow(e)).ToList();
        Drives = snap.Drives;
        RecycleText = snap.Recycle == null
            ? NotAvailable + (_recycle.LastError is { } err ? $" ({err})" : "")
            : $"{snap.Recycle.Items} items ({Size(snap.Recycle.Bytes)})";
        UpdateReclaim();
    }

    private void UpdateReclaim() =>
        ReclaimText = $"Could free about {Size(_lastTemp.Bytes + (_lastRecycle?.Bytes ?? 0))}";

    private async Task ToggleAsync(StartupRow? row)
    {
        if (row == null || !row.CanToggle) return;
        var enable = !row.Entry.Enabled;
        var ok = await Task.Run(() => Safe(() => _startup.SetEnabled(row.Entry, enable), false));
        ResultText = ok ? $"{row.Name}: {(enable ? "enabled" : "disabled")} at startup." : $"Could not change {row.Name}.";
        await ReloadLightAsync();
    }

    private async Task CleanTempAsync()
    {
        var scan = _lastTemp;
        if (scan.Files == 0) { ResultText = "No temporary files older than 24 hours to clean."; return; }
        if (!_confirm.Confirm("Clean temporary files",
                $"Delete {scan.Files} files ({Size(scan.Bytes)}) older than 24 hours from your temp folder?\n\nFiles that are in use are skipped. This can take a few minutes."))
            return;

        IsScanning = true; // keeps both destructive buttons off while the cleanup runs
        try
        {
            TempText = "Cleaning… this can take a few minutes";
            var result = await Task.Run(() => Safe(_temp.Clean, new TempCleanResult(0, 0, 0)));
            ResultText = $"Freed {Size(result.DeletedBytes)}: deleted {result.DeletedFiles} files, skipped {result.Skipped}.";
        }
        finally { IsScanning = false; }
        await LoadAsync();
    }

    private async Task EmptyRecycleAsync()
    {
        var info = _lastRecycle;
        if (info == null || info.Items == 0) { ResultText = "The Recycle Bin is already empty."; return; }
        if (!_confirm.Confirm("Empty Recycle Bin",
                $"Permanently delete all {info.Items} items ({Size(info.Bytes)}) in the Recycle Bin?\n\nThis cannot be undone."))
            return;

        var ok = await Task.Run(() => Safe(_recycle.Empty, false));
        ResultText = ok ? $"Recycle Bin emptied ({Size(info.Bytes)} freed)." : "Could not empty the Recycle Bin.";
        await ReloadLightAsync();
    }

    private static string Size(long bytes) => bytes >= 1L << 30
        ? $"{(bytes / 1073741824.0).ToString("0.0", CultureInfo.CurrentCulture)} GB"
        : $"{(bytes / 1048576.0).ToString("0.0", CultureInfo.CurrentCulture)} MB";

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
}
