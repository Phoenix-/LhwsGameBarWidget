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

## Installing (end users)

1. Install and start LibreHardwareService (with the AppContainer ACL patch, see above).
2. Download `LhwsGameBarWidget_<version>_x64.zip` from the releases page and extract it
   (bleeding-edge builds live in the [nightly](../../releases/tag/nightly) prerelease,
   rebuilt automatically on days that have new commits).
3. Right-click `Install.ps1` → **Run with PowerShell**. The script:
   - imports the bundled `.cer` into *Local Machine → Trusted People* (asks for admin
     elevation — the package is signed with a self-signed certificate, so Windows must
     be told to trust it once);
   - installs the `Microsoft.VCLibs` framework dependency if missing;
   - installs the widget package.
4. Press **Win+G**, find **LHWS Sensors** in the widget menu, and configure rows via the
   widget's settings (title-bar gear button).

To uninstall: *Settings → Apps → LHWS Sensors → Uninstall* (or
`Get-AppxPackage LhwsGameBarWidget | Remove-AppxPackage`). The trusted certificate can be
removed from `certlm.msc` → Trusted People.

## Building the installer

Prerequisites (beyond the dev build requirements below): the *Desktop development with C++*
VS workload — Native AOT needs the MSVC linker.

```powershell
.\pack.ps1            # x64 (default)
.\pack.ps1 -Zip       # + zip the output folder for a GitHub release
.\pack.ps1 -Platform ARM64
```

The script produces `AppPackages\LhwsGameBarWidget_<version>_<platform>_Test\` with the
signed `.msix`, the public `.cer`, the `Install.ps1`/`Add-AppDevPackage.ps1` sideloading
scripts, and the `Dependencies\` folder (VCLibs). What it does:

- Finds VS MSBuild via `vswhere` and puts the VS Installer directory on `PATH`
  (the Native AOT toolchain invokes `vswhere.exe` by bare name to locate `link.exe`).
- Finds a code-signing certificate in `Cert:\CurrentUser\My` whose Subject equals the
  `Publisher` in `Package.appxmanifest` (`CN=Phoenix-`), creating a self-signed one on
  first run. The Subject **must** match the manifest Publisher or Windows rejects the package.
- Builds `Release` with `-p:PublishAot=true -p:SelfContained=true
  -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=SideloadOnly`, so the
  package contains a single native AOT executable — end users don't need the .NET runtime.
- Signs the package with the certificate (`AppxPackageSigningEnabled` +
  `PackageCertificateThumbprint`).

To publish a new version: bump `Version` in `Package.appxmanifest`, run `.\pack.ps1 -Zip`,
upload the zip as a release asset.

Nightlies: `.github/workflows/nightly.yml` runs daily (03:00 UTC; skipped when there are
no new commits) and refreshes the rolling `nightly` prerelease. It stamps the manifest
revision with the run number (`<major>.<minor>.<build>.<run>`), installs the VS
*Universal Windows Platform tools* component on the runner (the image ships without it),
and signs with the `CN=Phoenix-` certificate from the `SIGNING_CERT_PFX` /
`SIGNING_CERT_PASSWORD` repository secrets — the same certificate as local builds, so
one trusted cert covers both.

Note: `GenerateAppxPackageOnBuild` must not flow into referenced projects — the MSIX
tooling's `Pack` target collides with NuGet's `Pack` (MSB4006 cycle). The main csproj
strips it with `UndefineProperties` on the `ProjectReference`.

Native AOT gotcha: the app csproj must keep `AllowUnsafeBlocks=true`. The CsWinRT AOT
optimizer emits vtable lookup code for generic collections crossing the WinRT ABI
(e.g. `ObservableCollection<T>` assigned to `ItemsSource`) only when unsafe code is
allowed — without it the types are *silently* skipped and the widget dies at activation
with `ArgumentException: Value does not fall within the expected range` in
`set_ItemsSource` (AOT builds only; Debug/CoreCLR works because CsWinRT builds CCW
vtables dynamically at runtime).

## Requirements

- Windows 11 (or Windows 10 19041+), Xbox Game Bar
- LibreHardwareService with the AppContainer ACL patch, running
- To build: Visual Studio 2026 with the *Universal Windows Platform tools* component,
  Windows 11 SDK (26100), .NET 9+ SDK; for `pack.ps1` also *Desktop development with C++*

## Build & run (dev)

Build with **VS MSBuild**, not `dotnet build` — core MSBuild lacks the UWP XAML targets
(`InitializeComponent` won't be generated):

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" `
    src/LhwsGameBarWidget/LhwsGameBarWidget.csproj -restore -p:Platform=x64
# register the loose layout (requires Developer Mode):
Add-AppxPackage -Register src/LhwsGameBarWidget/bin/x64/Debug/net9.0-windows10.0.26100.0/AppxManifest.xml
```

Then press Win+G and open the **LHWS Sensors** widget from the widget menu.

Dev-loop notes:

- Debug builds additionally need the `Microsoft.VCLibs.140.00.Debug` framework package
  (install from `C:\Program Files (x86)\Microsoft SDKs\Windows Kits\10\ExtensionSDKs\Microsoft.VCLibs\14.0\Appx\Debug\x64\`).
- Code-only changes don't need re-registration (loose-file package) — rebuild and restart
  Game Bar. Manifest changes do: bump `Version` and re-register (same-version re-register
  fails with 0x80073CFB).
- `Remove-AppxPackage` wipes `LocalState` (user config, `widget.log`) — avoid it during dev;
  re-register over the existing package instead.
