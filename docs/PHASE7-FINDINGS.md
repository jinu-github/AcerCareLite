# Phase 7 findings: battery health mode control on Acer Nitro AN515-58 (BIOS Insyde V2.21)

Everything below was observed on one machine (Windows 11, build 26300). Nothing here is a claim about other models or BIOS versions.
Phase 0 findings (read-only discovery) are in `PHASE0-FINDINGS.md`.

**Status in one paragraph.** Reading the health mode, changing it in both directions with readback verification, the audit log and the charge threshold are verified on this machine. **Persistence is verified across a warm restart only.** After a full shutdown with AC removed, the setting was lost once on this machine. The cause is unconfirmed; firmware or EC behavior is the leading hypothesis. Nothing in the evidence shows Acer software resetting it. See "Incident: health mode lost after a full shutdown" below. AcerCareLite does not re-apply the setting.

## Live interface (AcerSchemaInspector, metadata only; no method was invoked)
Class `root\wmi:BatteryControl`, "Class used to control smart battery, Version 2.86", GUID `{79772EC5-04B1-4bfd-843C-61E7F77B6CC9}`,
one instance `ACPI\PNP0C14\APGe_0`.

| Method (WmiMethodId) | IN | OUT |
|---|---|---|
| `GetBatteryHealthControlStatus` (20) | `uBatteryNo` u8, `uFunctionQuery` u8, `uReserved` u8[2] | `uFunctionList` u8, `uReturn` u8[2], `uFunctionStatus` u8[5] (MAX=5) |
| `SetBatteryHealthControl` (21) | `uBatteryNo` u8, `uFunctionMask` u8, `uFunctionStatus` u8, `uReservedIn` u8[5] | `uReturn` **u16**, `uReservedOut` u16 |
| `GetBattInfoInterface` (19) | `uBatteryInfoIndex` u32, `uBatteryNo` u32 | `uReturn` u32 (not used by this project) |

## Verified on this machine
| Question | Evidence |
|---|---|
| Health mode can be read from an unelevated app via a UAC-approved helper | Both states read repeatedly; `functionList=0x03`, `return=[0,0]` |
| The setter works | Off to On and On to Off, each `Applied` by readback; `setterReturn=0` both times |
| Which mask is health mode | `uFunctionMask=0x01` (battery 1) changed only `status[0]`; every other status byte and the function list were unchanged in both directions |
| Firmware latency | `readbackAttempts=2` both times: the first poll already matched, plus one confirmation sample. The "about 13 s" in the Phase 0 trace was not firmware latency |
| Charge threshold from below | With health mode On, charging from a lower level stopped at about 80% |
| Independent cross-check | The original read-only AcerBatteryProbe (separate code, elevated) read `uFunctionList=0x03`, `uReturn=[0,0]`, `uFunctionStatus=[1,0,0,0,0]` and decoded health mode On, calibration off |
| Persistence across a **warm restart** | State held (On stayed On) across one Windows restart shortly after the write. That first post-restart read returned only 4 status bytes (see open items). This is the only persistence that has been verified. **Persistence across a full shutdown with AC removed failed once; see the incident section** |
| Care Center agrees | After each write, Care Center showed the same state (Charge Limit Off, then On) without its toggle being touched |
| Our write was not overridden | Care Center was opened after each write and the firmware state stayed as written (over 5 minutes) |
| Audit trail | `%LocalAppData%\AcerCareLite\acer-write-audit.log` recorded `blocked` (refused from an unprotected dev location), then `requested` and `completed/Applied` for each write |

## Charging behaviour
- Phase 0: with the flag at 1, charging stopped at 79.6% (37,453 of 47,078 mWh) and held overnight.
- Phase 7, Off write at about 80%: charging continued (80% to 83%).
- Phase 7, On write at about 91%: the battery stayed at 92% for 15 to 20 minutes with AC connected (an active limit stops charging; it does not discharge).
- Phase 7, from below: with health mode On, charging stopped at about 80%. The card title "about 80 % charge limit" is therefore supported on this machine.

## Incident: health mode lost after a full shutdown
All times are local (UTC+8). This section records observations; it does not claim a cause.

### What happened
1. Health mode was On, set by an AcerCareLite write and verified by readback (`uFunctionStatus=[1,0,0,0,0]`). The from-below test then confirmed charging stopped at about 80%.
2. Windows was shut down normally (Start, Shut down) at 2026-10-05 11:56 PM. Fast Startup was off (`HiberbootEnabled=0`, "disabled by current system policy"), and the next boot was a full boot (Kernel-Boot event 27, boot type 0x0).
3. AC was disconnected after the shutdown had finished (per the user's account). The laptop stayed fully powered off for more than 8 hours. A Kernel-Power power-source event is logged 11 seconds before the shutdown began (11:56:22 PM); its direction was not recorded here, so the exact AC timing around shutdown is not fully established.
4. The laptop was powered on at 2026-10-06 7:59 AM and AC was connected immediately (power-source events at 7:59:08 and 7:59:19; ACCSvc started at 7:59:16). The battery was still at about 80%.
5. The battery then charged above 80%, which the limit would have prevented. Only afterwards was AcerCareLite opened: it showed health mode Off, and the independent AcerBatteryProbe agreed (`uFunctionList=0x03`, `uReturn=[0,0]`, `uFunctionStatus=[0,0,0,0,0]`, calibration off, no errors). Windows reported the battery at about 99% (47,832 of 48,325 mWh).

### What was ruled out
- No Windows update and no BIOS update occurred, and BIOS setup was not changed.
- Acer Care Center and AcerCareLite were not opened before the charging was noticed.
- AcerCareLite did not write: the installed build was the read-only one (the setter name was absent from the installed helper) and the audit log still held only the five entries from the two supervised writes and the blocked attempt.
- A later restart, with the flag already Off, still read Off (so nothing restored an On value at boot), and only ACCSvc was running among Acer-named processes two minutes after boot.

### Acer software footprint (read-only inspection)
- `ACCSvc` ("ACC Service"): automatic, LocalSystem, Acer-signed, version 4.00.3060.0. It starts about 16 seconds after boot. It runs on every boot, including the warm restart after which the flag held. No Acer scheduled task or startup entry was found with the filters used. No Acer service start or stop events were recorded around the incident (the System log does not appear to record such events in that window at all).
- Care Center's `CareCenterConfig.ini` is 228 bytes, was last modified on 2026-10-05 around 3:19 PM, and contains no battery, charge, limit or health text. `HKLM\SOFTWARE\OEM\CareCenter` holds only four unrelated values. No saved charge-limit setting was found in the locations checked. This is absence of evidence only: a setting could be stored somewhere not checked.
- Static string search: `ACCStd.exe` and `ACCUtilities.dll` contain the `BatteryControl` method names (including `SetBatteryHealthControl` and `GetBatteryHealthControlStatus`); `ACCSvc.exe` does not contain them and does not reference those files. `ACCUtilities.dll` was not loaded in any running process. These are capabilities in files, not observed behavior.

### WMI trace tests (Microsoft-Windows-WMI-Activity/Trace; names and process IDs only, no arguments)
| Test | Result |
|---|---|
| Open Care Center, do not touch its toggle (flag Off) | `ACCStd.exe` issued `select * from BatteryControl` and `GetBatteryHealthControlStatus`. A second process, which had exited before it could be identified (not identified), repeatedly called `GetBattInfoInterface`. **No `SetBatteryHealthControl` call.** The probe still read Off before and after |
| Cold boot with AC connected (2026-10-07): 3,803 events captured, 09:22:32 to 09:25:54; ACCSvc started 09:22:49 | **No `BatteryControl` event of any kind** |
| Cold boot with AC removed for about 5 minutes while off, reconnected before power-on (2026-10-07): 3,662 events, 15:32:04 to 15:35:29; ACCSvc started 15:32:23 | **No `BatteryControl` event of any kind** |

The trace mechanism works: it captured Care Center's reads and, in Phase 0, a set call.

### What these tests do not cover
- **The shutdown itself.** The first captured event in each cold-boot test came after power-on, so whatever happens during shutdown was not observed (either nothing used WMI, or events from before the shutdown were not kept; this was not established).
- **The incident's exact conditions.** The incident had about 8 hours off and AC connected seconds after power-on; the traced runs used shorter off times and different AC timing.
- **Anything outside WMI.** BIOS, EC, SMM, or a driver talking to ACPI directly cannot appear in this trace.
- **The flag was already Off** during every trace test, so these tests could catch software touching `BatteryControl`, but they could not observe the firmware losing an On state.
- **Care Center's own persistence across a cold power-off was never tested.** The Phase 0 "held overnight" observation does not record the power state, so it does not show persistence across a full shutdown.

### Hypotheses (current ranking; none is confirmed)
| Rank | Hypothesis | Status |
|---|---|---|
| 1 | Firmware or EC does not keep the flag across a cold power-off (and/or AC removal) | **Leading, unproven.** Consistent with every observation; no WMI-visible software activity at boot |
| 2 | The setter call lacks something persistence-related (other argument values, another call) | Open and lower. Cannot be separated from 1 without a write. Phase 0's trace recorded method names only |
| 3 | Software acts during shutdown or at an AC-attach event | Lower. The shutdown was not observed |
| 4 | A saved On value is restored at boot, or something sets Off at boot | Not supported: Off survived two boots and no `BatteryControl` calls were seen at boot |

Nothing in the evidence shows that Acer software resets the setting, and this document makes no such claim.

### Outside precedent (other models; not evidence about this machine)
- An Arch Linux wiki page for an Acer Swift Go (https://wiki.archlinux.org/title/Acer_Swift_Go_SFG16-72) describes enabling health mode with the same open-source driver as lasting only until reboot, and suggests a boot-time service to re-apply it.
- A 2011 Linux commit about Acer's wireless on/off state (acer-wmi, commit 8215af019040) says the Acer BIOS keeps that state across a warm reboot but resets it to defaults on a cold boot.
Both are consistent with the pattern seen here, but they concern other models and other features.

### Decision recorded
The finding is documented as it stands. **No implementation change follows from it:** the write path is unchanged, ACCSvc and other Acer components are untouched, and AcerCareLite does **not** re-apply the setting at startup. A re-apply-at-boot feature would need an elevated start-up action and a separate security design; none is planned. The decisive experiment (a Care Center-set flag versus ours across a cold power-off) needs a firmware write and has not been done.

## Open items (not verified)
1. **Cause of the loss after a full shutdown.** Health mode went from On to Off across a full shutdown with AC removed. The cause is unconfirmed; firmware or EC behavior is the leading hypothesis. Care Center's behavior across a cold power-off was never tested, the shutdown phase was not observed, and non-WMI paths are invisible to the traces. See the incident section.
2. **Intermittent 4-byte status read.** After a Windows restart, and on one re-check shortly afterwards, the read path returned **4** status bytes (`status=[1,0,0,0]`).
   It did **not** reproduce later: three repeated app checks and the independent probe all returned 5 (`[1,0,0,0,0]`), as did every earlier read and both write readbacks.
   Cause unknown; a short or late-initialising firmware buffer shortly after boot is a hypothesis, not a finding. If it recurs, note how long after boot it happened.
   Consequence by design: the write path requires exactly 5 bytes before it will write, so in a 4-byte state it would **refuse** (NotApplied, "Expected 5 status bytes, got 4").
   The classifier would also report a length change between the before and after reads as an anomaly and lock changes for the session. Neither check has been relaxed, because refusing is the safe failure.
3. **Care Center interference.** Not tested: writing while Care Center is open, and clicking Care Center's own toggle after our write (Phase 0 saw its switch move without a set being sent).
4. **Function bit 1 (`0x02` in the function list).** Its meaning is unknown (the probe labels it calibration, from the open-source driver, not from this machine). This project never addresses it and has no mask for it.
5. **Setter return value.** Only `0` has been seen. Its other values are unknown, which is why the readback, not the return code, decides the outcome.
6. **Helper integrity.** The helper is unsigned. The protected install location and the peer checks are the mitigation, not signing.
7. **Helper crash detection.** Exercised by unit tests only. The helper exits too quickly to kill by hand, so the early-exit path (and the pre-connect case, which relies on best-effort process observation) has not been seen on hardware.

## How the write path is constrained
- The main app is never elevated. A separate helper (`requireAdministrator` manifest) is started with a UAC prompt for each read or change.
- Write support exists only in builds made with `-p:AcerWriteEnabled=true` (the install script's `-EnableWrite`, after typing ENABLE). The default build has a stub that refuses changes, and the installed helper then contains no setter name.
- Both the app and the helper check that the helper and the app sit in a protected location (Program Files, administrator-only ACLs). The helper repeats the check itself.
- The pipe is created by the app with a random name and nonce, an ACL, a single instance, and a peer-image check in both directions. Change requests get no development-host exception in any build.
- A change needs: a supported fresh read, a confirmation dialog, a successful audit "requested" record written before the helper is launched, and at least 10 s since the last attempt.
- The helper checks strict preconditions (function list bit 0, exactly 5 status bytes, health byte 0 or 1, other bytes 0), announces `WriteSent` before the single setter call, then polls the firmware for up to 20 s.
- **Applied** needs the health byte to equal the request and every other byte and the function list to be unchanged. There is no retry and no rollback.
- Anything unexpected locks changes for the session. A helper that dies after `WriteSent` is reported as **Result unknown**, never as "not applied".

## Operating notes
- Daily use: the read-only install (`.\scripts\Install-AcerCareLite.ps1`). Confirm with a byte search of `C:\Program Files\AcerCareLite\AcerCareLite.AcerHelper.dll` for `SetBatteryHealthControl` (all `False`).
- To change the setting: `.\scripts\Install-AcerCareLite.ps1 -EnableWrite`, make the change, then reinstall without the switch.
- If a change ever reports `Unexpected result`, stop, check Care Center for a calibration state, and keep the audit log.
- On this machine the setting may not survive a full shutdown with AC removed (it was lost once). After a cold boot, press **Check** to see the current state. AcerCareLite does not re-apply the setting.
