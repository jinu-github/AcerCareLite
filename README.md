# AcerCareLite

Lightweight native Acer Nitro utility (C# / .NET 8 / WPF / MVVM). Phase 7 (Acer battery health mode, read and change) is hardware-validated on one machine: see `docs/PHASE7-FINDINGS.md`.

## Layout
- `src/AcerCareLite.Core` - interfaces, models, `CapabilityResult` (Supported/Unsupported/Unknown/Error)
- `src/AcerCareLite.Windows` - Windows API/CIM implementations (monitors, hardware info, startup apps, Recycle Bin, start-with-Windows)
- `src/AcerCareLite.Acer` - Acer-specific integration: launches the elevated helper and receives its reply (Phase 7a)
- `src/AcerCareLite.AcerHelper` - small elevated helper (the only project with `requireAdministrator`); read-only unless built with `-p:AcerWriteEnabled=true`
- `tools/AcerHardwareExplorer` - read-only WMI discovery tool (Phase 0)
- `tools/AcerBatteryProbe` - Phase 0b: calls exactly two documented getter methods with fixed arguments; cannot call setters
- `tests/AcerCareLite.Tests` - xUnit, includes a source guard keeping the Explorer read-only
- `src/AcerCareLite.App` - WPF shell (Phase 1): generic host, DI, logging, dark theme, navigation

## Bootstrap the solution
```powershell
dotnet new sln -n AcerCareLite
dotnet sln add src/AcerCareLite.Core src/AcerCareLite.Windows src/AcerCareLite.Acer src/AcerCareLite.App tools/AcerHardwareExplorer tools/AcerBatteryProbe tests/AcerCareLite.Tests
dotnet build
dotnet test
```

## Run the explorer
```powershell
dotnet run --project tools/AcerHardwareExplorer -c Release
# optional, still read-only but the BIOS runs its own query code:
dotnet run --project tools/AcerHardwareExplorer -c Release -- --read-acer-instances
```
Reports land in `Reports\` next to the built exe (`.md` for reading, `.json` for tooling).

## Phase 0b: battery probe
```powershell
# 1) signatures only, no firmware calls
dotnet run --project tools/AcerBatteryProbe -c Release -- --check-only --note "signature-check"
# 2) real reads, once per Care Center state
dotnet run --project tools/AcerBatteryProbe -c Release -- --note "care-center-limit-off"
dotnet run --project tools/AcerBatteryProbe -c Release -- --note "care-center-limit-on"
```
If you get "Access denied", repeat from an elevated terminal.

## Phase 1: run the shell
```powershell
dotnet build
dotnet test
dotnet run --project src/AcerCareLite.App
```
Findings from the hardware research are in `docs/PHASE0-FINDINGS.md`.

## Phase 2: Dashboard
A live overview of the machine. Each value comes from an interface in `AcerCareLite.Core` (`ICpuMonitor`, `IGpuMonitor`, `IMemoryMonitor`, `IStorageMonitor`, `IPowerMonitor`, `ISystemInfoProvider`) implemented in `AcerCareLite.Windows`, so the view never touches WMI, performance counters or the registry directly.
- CPU usage (the "% Processor Utility" counter, the same one Task Manager uses)
- GPU usage (the busiest GPU by 3D engine, from the "GPU Engine" counters; covers both the Intel and NVIDIA adapters)
- Memory usage (used / total)
- Battery percentage and charge state (standard Windows power status)
- Storage / drives overview (used / total for each fixed drive)
- System information: manufacturer, model and BIOS version (for example Acer Nitro AN515-58, BIOS V2.21) and the Windows version

MVVM and polling: `DashboardViewModel` builds the card values and gets its refresh timer through `IPollingTimer`, so it is unit-tested with fakes. It polls **only while the page is visible**: the timer starts when you open the page and stops when you leave it (and while the window is hidden). The refresh interval comes from Settings (default 2 s). CPU and GPU need two samples, so they show "…" for the first moment. A monitor that fails shows "Not available" and never crashes the page. No administrator rights are needed.

## Phase 3: Battery page and history
Read-only battery monitoring using the standard Windows battery classes (no administrator rights, nothing is written).
- Current battery state: charge percentage, plugged in / on battery / charging, voltage, and charge or discharge rate
- Capacity readings: design capacity, full-charge capacity and the current amount, in Wh
- Battery health: full-charge capacity as a percentage of design capacity
- Cycle count is shown as "Not reported by this firmware" when the firmware returns 0 (it does on the development machine)
- History: one sample per minute while the app is running, stored in SQLite at `%LocalAppData%\AcerCareLite\battery.db`, kept for 30 days (older rows are pruned)
- A "Last 24 hours" line chart of that history, drawn with plain WPF. The line breaks wherever the app was not running

History is recorded only while the app is running. The Acer health-mode card on this page is a separate feature (Phase 7).

## Phase 4: Hardware page
Hardware detection from standard WMI classes, loaded when the page first opens and again when you press **Refresh** (no polling, no administrator rights).
- CPU: name, cores, threads and the clock speed as reported by Windows
- GPU: every adapter (for example Intel and NVIDIA) with driver version and VRAM; VRAM comes from the 64-bit value Windows stores per adapter, because the usual WMI value stops at 4 GB. Shared-memory adapters may show "VRAM not reported"
- RAM modules: slot, size, speed and manufacturer for each installed module
- Storage devices: model, size, SSD or HDD, and bus (for example NVMe)

Each section loads independently, and anything unavailable shows "Not available". There are no Acer-specific controls on this page; the machine model and BIOS version are on the Dashboard (Phase 2).

## Phase 5: Utilities
Nothing on this page runs automatically. Every action is a button press, and deletions also need a confirmation dialog (with No as the default).
- Startup apps viewer: entries from the Run registry keys and the Startup folders, with enabled / disabled state. For your own (current-user) entries, Disable and Enable flip the same flag Task Manager uses, so nothing is deleted and every change can be undone. All-users entries are shown read-only ("needs administrator")
- Temporary file cleanup: scans your own temp folder first and shows how many files and how much space can be cleaned. **Clean** removes only files older than 24 hours inside that folder, skips files in use, never follows junctions or symlinks, and removes folders it has emptied
- Recycle Bin cleanup: shows the number of items and their size. **Empty** asks for confirmation and warns that it cannot be undone
- Storage overview: used and total space for each drive, plus how much temp files and the Recycle Bin could free

Safety approach: the scan is a preview before any deletion, the Clean and Empty buttons stay disabled while a scan or cleanup is running, and the cleaner refuses folders that are too close to a drive root. `C:\Windows\Temp` is not touched (it needs administrator rights).

## Phase 6a: Settings
The Settings page saves every change immediately to `%LocalAppData%\AcerCareLite\settings.json`. A missing or corrupt file falls back to defaults, and hand-edited values snap to the choices the page offers.
- Theme: Dark (default) or Light. A theme change applies after restarting the app (a note on the page says so)
- Start with Windows: adds or removes the app's own entry in your Run key. The registry is treated as the truth at startup, and the option refuses to register when the app was started through `dotnet.exe`
- Single instance: a second launch exits instead of opening a duplicate
- Monitoring interval (2, 5, 10 or 30 seconds): how often the Dashboard and Battery pages refresh while they are open
- Start minimized, minimize to tray, notification options and temperature units are stored here. Temperature units are saved but not used yet, because no temperature is displayed

## Phase 6b: Tray and notifications
- System tray icon (the built-in Windows notification-area icon, currently the generic application icon). Hovering shows battery, CPU and GPU; right-click shows the same plus Open dashboard, Battery settings (opens the Battery page), Settings and Exit. Double-click opens the Dashboard. These values are read when you hover or open the menu, so the tray does no polling of its own
- Minimize behavior: with **Minimize to tray** on, minimizing or closing the window hides it to the tray and Exit is only in the tray menu; with it off, the close button exits. Windows sign-out and shutdown are never blocked
- **Start minimized** starts hidden in the tray (or as a minimized window when minimize to tray is off)
- Notifications, shown as Windows tray balloons and controlled in Settings: low battery (default 20%), battery threshold while plugged in (default 80%), charger connected or disconnected, and low storage (default under 10% free)
- Background monitoring: one light check every 30 seconds while the app runs. Each alert fires once per event and re-arms after recovery, and conditions that already exist at startup do not alert (low storage alerts once). Pages that poll stop polling while the window is hidden

## Phase 7a: Acer battery health mode (read-only)
The Battery page has an "Acer battery health mode" card. Nothing is read until you press **Check charge-limit state**; Windows then shows a UAC prompt for `AcerCareLite.AcerHelper.exe`, which reads the firmware state once and reports back over a one-shot named pipe. The main app is never elevated, and nothing in this phase can write to the firmware.
```powershell
dotnet sln add src/AcerCareLite.AcerHelper   # once
dotnet build
dotnet test
Test-Path .\src\AcerCareLite.App\bin\Debug\net8.0-windows\AcerCareLite.AcerHelper.exe   # helper must sit next to AcerCareLite.exe
```

## Phase 7b-0: hardening before any write support (still read-only)
No firmware write exists in this phase. The elevated helper now speaks protocol v2 (`--op read`, `Started` then `Result` messages),
each side verifies the program on the other end of the pipe, and the Battery card shows whether the helper sits in a protected location.

```powershell
# one-time: add the new tool to the solution
dotnet sln add tools/AcerSchemaInspector

dotnet build
dotnet test

# metadata-only schema dump (never invokes any method); add --out <folder> to choose the report location
dotnet run --project tools/AcerSchemaInspector -c Release

# protected install (ELEVATED PowerShell): copies to C:\Program Files\AcerCareLite and resets permissions
.\scripts\Install-AcerCareLite.ps1
.\scripts\Install-AcerCareLite.ps1 -Uninstall
```
Debug builds also accept `dotnet.exe` as the app process in the helper's peer check; Release builds are strict.

## Phase 7b-1: changing the health mode (OFF in every default build)
The default build, the default install and the default Release publish contain **no** write code: the helper has a stub that refuses
every change request, and the Battery card has no change button. Write support exists only when built with `-p:AcerWriteEnabled=true`,
which only `Install-AcerCareLite.ps1 -EnableWrite` does (after you type ENABLE).

Safety rules in the write path: protected install location (checked by the app AND by the elevated helper), strict peer check with no
development exception, a fresh supported read first, confirmation dialog, an audit-log record written BEFORE the helper is launched,
10 s between attempts, the health bit only (one named mask constant), `WriteSent` announced before the single setter call, bounded
readback, and no retry. Anything unexpected (another byte or the function list changing, an unknown helper) locks changes for the session.
The audit log is `%LocalAppData%\AcerCareLite\acer-write-audit.log`.

```powershell
dotnet build                                   # default: no write support
dotnet test                                    # runs against the default build
dotnet build -p:AcerWriteEnabled=true          # compile-check the write path only (does not install anything)
```

## Install (Program Files)
Run from an **elevated** PowerShell in the project folder. If Windows reports that running scripts is disabled, allow it for this window only first:
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

# Everyday install: read-only (no write code is built in, and the installed helper does not contain the setter name)
.\scripts\Install-AcerCareLite.ps1

# EXPERIMENTAL: build with the ability to change the battery health mode (asks you to type ENABLE)
.\scripts\Install-AcerCareLite.ps1 -EnableWrite

# Rollback to read-only: reinstall WITHOUT the switch, then confirm the setter name is gone (all three lines should say False)
.\scripts\Install-AcerCareLite.ps1
$b = [IO.File]::ReadAllBytes("C:\Program Files\AcerCareLite\AcerCareLite.AcerHelper.dll")
$n = "SetBatteryHealthControl"
"ascii:   " + [Text.Encoding]::ASCII.GetString($b).Contains($n)
"utf16-0: " + [Text.Encoding]::Unicode.GetString($b, 0, $b.Length - ($b.Length % 2)).Contains($n)
"utf16-1: " + [Text.Encoding]::Unicode.GetString($b, 1, $b.Length - 1 - (($b.Length - 1) % 2)).Contains($n)

# Remove the installed copy
.\scripts\Install-AcerCareLite.ps1 -Uninstall
```
Close AcerCareLite (including its tray icon) before installing or uninstalling. Start the installed app normally, not as administrator:
`& "C:\Program Files\AcerCareLite\AcerCareLite.exe"`.

## Phase 7 status
Verified on the Nitro AN515-58 (BIOS V2.21): reading the health mode, changing it in both directions with readback verification, the audit log, and the charge threshold (charging stopped at about 80% from below).
**Persistence is verified across a warm restart only.** After a full shutdown with AC removed, the setting was lost once on this machine. The cause is unconfirmed, with firmware or EC behavior the leading hypothesis;
two WMI trace tests across cold boots showed no `BatteryControl` activity from Acer software or anything else, and nothing shows Acer software resetting it. AcerCareLite does not re-apply the setting at startup: after a cold boot, press **Check** on the Battery page to see the current state.
Open items (the cold-shutdown loss, a 4-byte status read that occurred twice during one post-reboot episode and was not reproducible afterward, Care Center interference) are listed in `docs/PHASE7-FINDINGS.md`.
The recommended everyday install is the default read-only one.