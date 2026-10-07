using AcerCareLite.Core.Acer;
using AcerCareLite.Core.Capabilities;
using AcerCareLite.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerCareLite.Core.Presentation;

/// <summary>
/// The "Acer battery health mode" card on the Battery page. Never reads or changes the firmware by itself:
/// the user presses a button. The state shown always comes from a firmware read or from a verified readback after a change.
/// The change button exists only in builds compiled with write support, and always asks for confirmation first.
/// </summary>
public sealed class AcerHealthCardViewModel : ObservableObject
{
    private readonly IAcerBatteryHealthService _service;
    private readonly IAcerHealthWriteService? _write;
    private readonly IConfirmationService? _confirm;
    private readonly bool _writeAvailable;

    private string _statusText = "Not checked";
    private string _reasonText = "Press the button to read the state from the firmware. Windows will ask for administrator approval.";
    private string _evidenceText = "";
    private string _checkedAtText = "";
    private string _buttonText = "Check charge-limit state";
    private string _installText = "";
    private string _writeStatusText = "";
    private string _writeDetailText = "";
    private string _writeEvidenceText = "";
    private bool _isChecking;
    private bool _isWriting;
    private bool? _knownEnabled;

    public AcerHealthCardViewModel(
        IAcerBatteryHealthService service,
        IHelperLocationGate? location = null,
        IAcerHealthWriteService? write = null,
        IConfirmationService? confirm = null,
        bool? writeAvailable = null)
    {
        _service = service;
        _write = write;
        _confirm = confirm;
        // No write service or no confirmation service means no change button. Confirmation can never be skipped.
        _writeAvailable = (writeAvailable ?? WriteBuild.Compiled) && write != null && confirm != null;

        if (location != null)
        {
            var status = location.Current;
            _installText = status.IsProtected
                ? "Helper location: protected. " + status.Reason
                : "Helper location: not protected. " + status.Reason;
        }

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking && !IsWriting);
        SetCommand = new AsyncRelayCommand(SetAsync, CanSet);
        if (service.Last is { } last) { Apply(last); _buttonText = "Check again"; }
    }

    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand SetCommand { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ReasonText { get => _reasonText; private set => SetProperty(ref _reasonText, value); }
    public string EvidenceText { get => _evidenceText; private set => SetProperty(ref _evidenceText, value); }
    public string CheckedAtText { get => _checkedAtText; private set => SetProperty(ref _checkedAtText, value); }
    public string InstallText => _installText;
    public string ButtonText { get => _buttonText; private set => SetProperty(ref _buttonText, value); }

    public bool IsWriteAvailable => _writeAvailable;
    public string WriteStatusText { get => _writeStatusText; private set => SetProperty(ref _writeStatusText, value); }
    public string WriteDetailText { get => _writeDetailText; private set => SetProperty(ref _writeDetailText, value); }
    public string WriteEvidenceText { get => _writeEvidenceText; private set => SetProperty(ref _writeEvidenceText, value); }

    public string FooterText => _writeAvailable
        ? "Changing the setting asks for confirmation and administrator approval, is verified by reading the firmware back, and every attempt is logged."
        : "Read-only. This build cannot change the firmware setting.";

    public string SetButtonText => _knownEnabled switch
    {
        true => "Turn health mode Off…",
        false => "Turn health mode On…",
        null => "Change health mode…"
    };

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!SetProperty(ref _isChecking, value)) return;
            ButtonText = value ? "Waiting for administrator approval…" : (_service.Last == null ? "Check charge-limit state" : "Check again");
            NotifyCommands();
        }
    }

    public bool IsWriting
    {
        get => _isWriting;
        private set
        {
            if (!SetProperty(ref _isWriting, value)) return;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        CheckCommand.NotifyCanExecuteChanged();
        SetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SetButtonText));
    }

    private bool CanSet() => _writeAvailable && _knownEnabled != null && !IsChecking && !IsWriting;

    private async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            Apply(await _service.CheckAsync());
        }
        catch (Exception ex)
        {
            // The service is not supposed to throw; if it does, show an error and never a guessed state.
            StatusText = "Error";
            ReasonText = "The check failed unexpectedly.";
            EvidenceText = ex.Message;
            CheckedAtText = "";
            _knownEnabled = null;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task SetAsync()
    {
        if (!_writeAvailable || _write == null || _confirm == null || _knownEnabled is not { } current) return;

        var target = !current;
        var message =
            $"Change Acer battery health mode from {OnOff(current)} to {OnOff(target)}?\n\n"
            + (target ? "Health mode limits charging to about 80 %." : "Health mode will stop limiting charging to about 80 %.")
            + "\n\nThis changes a firmware setting. It does not change battery calibration. "
            + "Windows will ask for administrator approval, the result is read back from the firmware, and the attempt is logged.";

        // Confirmation is mandatory: there is no code path to the write service that skips it.
        if (!_confirm.Confirm("Change battery health mode", message))
        {
            WriteStatusText = "Cancelled";
            WriteDetailText = "Nothing was changed.";
            WriteEvidenceText = "";
            return;
        }

        IsWriting = true;
        try
        {
            ApplyWrite(await _write.SetAsync(target));
        }
        catch (Exception ex)
        {
            // The write service reports failures as results and should not throw. If it does, we do not know what happened.
            _knownEnabled = null;
            WriteStatusText = "Result unknown";
            WriteDetailText = "The change attempt ended unexpectedly. Press Check to read the current state.";
            WriteEvidenceText = ex.Message;
        }
        finally
        {
            IsWriting = false;
        }
    }

    private void ApplyWrite(AcerWriteResult r)
    {
        WriteStatusText = r.Outcome switch
        {
            AcerWriteOutcome.Applied => "Applied",
            AcerWriteOutcome.NoChange => "No change needed",
            AcerWriteOutcome.NotApplied => "Not applied",
            AcerWriteOutcome.Failed => "Failed",
            AcerWriteOutcome.Unknown => "Result unknown",
            AcerWriteOutcome.Anomaly => "Unexpected result",
            AcerWriteOutcome.Untrusted => "Blocked: unexpected program",
            AcerWriteOutcome.Blocked => "Not allowed",
            _ => ""
        };
        WriteDetailText = r.Summary;
        WriteEvidenceText = r.Evidence;

        var when = r.At.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture);
        if (r.VerifiedEnabled is { } verified)
        {
            _knownEnabled = verified;
            StatusText = verified ? "Health mode: On" : "Health mode: Off";
            ReasonText = "Verified by reading the firmware back after the change.";
            CheckedAtText = $"Verified at {when}";
            EvidenceText = r.Evidence;
        }
        else if (r.RequiresFreshRead)
        {
            _knownEnabled = null;
            StatusText = "State unknown";
            ReasonText = "Press Check to read the current state from the firmware.";
            CheckedAtText = $"Change attempt at {when}";
        }

        NotifyCommands();
    }

    private void Apply(AcerBatteryHealthResult r)
    {
        EvidenceText = r.Capability.Evidence ?? "";
        ReasonText = r.Capability.Reason;
        var when = r.CheckedAt?.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture);

        if (r.Outcome == AcerReadOutcome.Read && r.State is { } state)
        {
            StatusText = state.Enabled ? "Health mode: On" : "Health mode: Off";
            CheckedAtText = $"Read from firmware at {when}";
            _knownEnabled = state.Enabled;
            NotifyCommands();
            return;
        }

        _knownEnabled = null;
        StatusText = r.Capability.Status switch
        {
            CapabilityStatus.Unsupported => "Not supported",
            CapabilityStatus.Error => "Error",
            _ => r.Outcome == AcerReadOutcome.NotChecked ? "Not checked" : "Could not determine"
        };
        CheckedAtText = when == null ? "" : $"Last attempt at {when}";
        NotifyCommands();
    }

    private static string OnOff(bool on) => on ? "On" : "Off";
}
