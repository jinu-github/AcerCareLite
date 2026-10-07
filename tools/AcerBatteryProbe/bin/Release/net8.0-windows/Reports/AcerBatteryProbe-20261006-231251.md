# AcerBatteryProbe report
Generated 2026-10-06 15:12:51Z | tool 0.1.0 | elevated: True
Note (set by you): -

## Machine
- Win32_ComputerSystem.Model: Nitro AN515-58
- Win32_BIOS.SMBIOSBIOSVersion: V2.21
- BatteryControl instance: ACPI\PNP0C14\APGe_0

## Signature check (no invocation)
- GetBatteryHealthControlStatus: OK - matches documented signature
- GetBattInfoInterface: OK - matches documented signature

## Firmware reads (attempted: True)
- uFunctionList: 0x03
- uReturn: [0, 0]
- uFunctionStatus: [0, 0, 0, 0, 0]
- battery temperature raw 3040 -> 30.9 C (assumes tenths of kelvin; plausible: True)

## Decoded (per open-source documentation, unverified on this BIOS)
- Health mode supported: True
- Calibration mode supported: True
- Health mode (80% limit) enabled: False
- Calibration mode enabled: False

## Windows battery numbers at the same moment
**BatteryStaticData**
- DesignedCapacity: 58751
- DeviceName: AP18E7M
**BatteryFullChargedCapacity**
- FullChargedCapacity: 44537
**BatteryStatus**
- RemainingCapacity: 44537
- PowerOnline: True
- Charging: False
- Discharging: False
- ChargeRate: 0
- DischargeRate: 0
- Voltage: 17078
**BatteryCycleCount**
- CycleCount: 0

## Errors (0)
