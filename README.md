# LHWS Game Bar Widget

An Xbox Game Bar (Win+G) widget that displays hardware sensors (temperatures, load, power, fans)
from [LibreHardwareService](https://github.com/epinter/LibreHardwareService) — a Windows service
exposing [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
data through memory-mapped files.

## How it works

- LibreHardwareService runs as LocalSystem, owns the ring0 driver, and publishes sensor data
  into named shared memory (`Global\LibreHardwareService/json/sensors/data`) once per second.
- This widget is a UWP app (modern UWP on .NET 9, `UseUwp=true`) hosted by Xbox Game Bar.
  It opens the shared memory read-only, guarded by the service's mutex, and renders the sensors.
- Because Game Bar widgets run in an AppContainer, the service must grant
  `ALL APPLICATION PACKAGES` read access to its named objects. Until the
  [upstream patch](https://github.com/Phoenix-/LibreHardwareService/tree/appcontainer-acl)
  is merged, install the service from that branch.

## Requirements

- Windows 11 (or Windows 10 19041+), Xbox Game Bar
- LibreHardwareService with the AppContainer ACL patch, running
- To build: Visual Studio 2026 with the *Universal Windows Platform tools* component,
  Windows 11 SDK (26100), .NET 9+ SDK

## Build & run (dev)

```powershell
dotnet build src/LhwsGameBarWidget -p:Platform=x64
# register the loose layout (requires Developer Mode):
Add-AppxPackage -Register src/LhwsGameBarWidget/bin/x64/Debug/net9.0-windows10.0.26100.0/AppxManifest.xml
```

Then press Win+G and open the **LHWS Sensors** widget from the widget menu.
