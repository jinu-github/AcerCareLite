```text
AcerSchemaInspector (metadata only)  2026-10-04T12:21:15.7463558+08:00
Machine: JINU   Elevated: True
Class: root\wmi:BatteryControl
Class qualifiers: Description=Class used to control smart battery, Version 2.86; dynamic=True; guid={79772EC5-04B1-4bfd-843C-61E7F77B6CC9}; Locale=MS\0x409; provider=WmiProv; WMI=True
Instances: ACPI\PNP0C14\APGe_0
Properties: Active:Boolean, InstanceName:String

Method GetBattInfoInterface   qualifiers: Description=Get battery Information Interface.; Implemented=True; read=True; WmiMethodId=19; write=True
  IN : uBatteryInfoIndex : UInt32   [CIMTYPE=uint32; ID=0; in=True]
  IN : uBatteryNo : UInt32   [CIMTYPE=uint32; ID=1; in=True]
  OUT: uReturn : UInt32   [CIMTYPE=uint32; ID=2; out=True]

Method GetBatteryHealthControlStatus   qualifiers: Description=Get Battery Health Control Status.; Implemented=True; read=True; WmiMethodId=20; write=True
  IN : uBatteryNo : UInt8   [CIMTYPE=uint8; ID=0; in=True]
  IN : uFunctionQuery : UInt8   [CIMTYPE=uint8; ID=1; in=True]
  IN : uReserved : UInt8[]   [CIMTYPE=uint8; ID=2; in=True; MAX=2]
  OUT: uFunctionList : UInt8   [CIMTYPE=uint8; ID=3; out=True]
  OUT: uReturn : UInt8[]   [CIMTYPE=uint8; ID=4; MAX=2; out=True]
  OUT: uFunctionStatus : UInt8[]   [CIMTYPE=uint8; ID=5; MAX=5; out=True]

Method SetBatteryHealthControl   qualifiers: Description=Set Battery Health Control.; Implemented=True; read=True; WmiMethodId=21; write=True
  IN : uBatteryNo : UInt8   [CIMTYPE=uint8; ID=0; in=True]
  IN : uFunctionMask : UInt8   [CIMTYPE=uint8; ID=1; in=True]
  IN : uFunctionStatus : UInt8   [CIMTYPE=uint8; ID=2; in=True]
  IN : uReservedIn : UInt8[]   [CIMTYPE=uint8; ID=3; in=True; MAX=5]
  OUT: uReturn : UInt16   [CIMTYPE=uint16; ID=4; out=True]
  OUT: uReservedOut : UInt16   [CIMTYPE=uint16; ID=5; out=True]
```
