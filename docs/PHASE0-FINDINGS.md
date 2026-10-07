# Phase 0 findings: battery charge limit on Acer Nitro AN515-58 (BIOS Insyde V2.21)

Everything below was observed on one machine. Nothing here is a claim about other models or BIOS versions.

## Interface
- `root\wmi:BatteryControl` (GUID 79772EC5-04B1-4bfd-843C-61E7F77B6CC9), instance `ACPI\PNP0C14\APGe_0`.
- Methods: `GetBattInfoInterface`, `GetBatteryHealthControlStatus`, `SetBatteryHealthControl`.
- Live method signatures matched the layout used by the open-source acer-wmi-battery driver (checked by AcerBatteryProbe before any call).
- Reading requires an elevated process ("Access denied" otherwise).

## Validation (Care Center was the only thing that changed settings; we only read)
| Question | Evidence |
|---|---|
| Interface exists | Class and methods enumerated by AcerHardwareExplorer |
| State readable | `GetBatteryHealthControlStatus` returns function list 0x03 and a status byte per function |
| Controls charging | Charging stopped at 37,453 of 47,078 mWh (79.6%) with the charger connected and the flag at 1 |
| Persists | Flag stayed 1 after closing/reopening Care Center and after a reboot, and held at 79.5% overnight |
| Reversible | One `SetBatteryHealthControl` call by Care Center (WMI-Activity trace), flag 0 about 13 s later, charging resumed (about 14 W) |

## Caveats
- Care Center's UI switch sometimes moves without any set being sent (observed twice, untraced). Always read the firmware back.
- Care Center shows no charge-limit page when it is not run as administrator.
- Care Center's window hung on "initializing" until its helper (`ACCStd.exe`) was started.
- The trace records method names, not arguments or return values.
- Whether our own set call would be honoured, or overwritten by Care Center's helper, is untested. No write has been made by this project.
- Cycle count reads 0 (not reported by this firmware). Battery temperature varied between runs, but is unverified against an independent source.
