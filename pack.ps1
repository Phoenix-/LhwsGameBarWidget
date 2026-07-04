<#
.SYNOPSIS
    Builds a signed, sideloadable MSIX installer for the LHWS Sensors Game Bar widget.

.DESCRIPTION
    Produces AppPackages\LhwsGameBarWidget_<version>_<platform>_Test\ containing:
      - LhwsGameBarWidget_<version>_<platform>.msix  (signed, Native AOT, self-contained)
      - LhwsGameBarWidget_<version>_<platform>.cer   (public key of the signing cert)
      - Install.ps1 / Add-AppDevPackage.ps1          (end-user install script)
      - Dependencies\                                (Microsoft.VCLibs framework packages)

    Signing uses a self-signed code-signing certificate from Cert:\CurrentUser\My whose
    Subject matches the Publisher in Package.appxmanifest; the certificate is created on
    first run. End users must trust the .cer (Install.ps1 does this) before installing.

.PARAMETER Platform
    Package architecture: x64 (default) or ARM64.

.PARAMETER Zip
    Also compress the output folder into AppPackages\LhwsGameBarWidget_<version>_<platform>.zip
    for publishing as a release asset.

.EXAMPLE
    .\pack.ps1
    .\pack.ps1 -Platform ARM64 -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src\LhwsGameBarWidget\LhwsGameBarWidget.csproj'
$manifest = Join-Path $repoRoot 'src\LhwsGameBarWidget\Package.appxmanifest'
$outDir = Join-Path $repoRoot 'AppPackages'

$identity = ([xml](Get-Content $manifest)).Package.Identity
$publisher = $identity.Publisher
$version = $identity.Version

# --- Locate VS: MSBuild for the UWP XAML targets ('dotnet build' lacks them), and put
# vswhere on PATH because the Native AOT toolchain invokes it by bare name to find link.exe.
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$vswhere = Join-Path $vsInstaller 'vswhere.exe'
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found — install Visual Studio." }
if (($env:PATH -split ';') -notcontains $vsInstaller) { $env:PATH += ";$vsInstaller" }

$vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath
$msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path $msbuild)) { throw "MSBuild.exe not found under '$vsPath'." }

# --- Signing certificate: Subject must equal the manifest Publisher or the OS rejects
# the package. Reuse the newest matching cert, or create one valid for a year.
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object Subject -eq $publisher |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "No code-signing cert with Subject '$publisher' found — creating one." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
        -KeyUsage DigitalSignature -FriendlyName 'LhwsGameBarWidget sideload signing' `
        -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Write-Host "Signing with $($cert.Subject) ($($cert.Thumbprint), expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"

$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

& $msbuild $project -restore -nologo -v:m `
    -p:Configuration=Release `
    "-p:Platform=$Platform" `
    "-p:RuntimeIdentifier=$rid" `
    -p:SelfContained=true `
    -p:PublishAot=true `
    -p:GenerateAppxPackageOnBuild=true `
    "-p:AppxPackageDir=$outDir\\" `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=true `
    "-p:PackageCertificateThumbprint=$($cert.Thumbprint)"
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE." }

$pkgName = "LhwsGameBarWidget_${version}_${Platform}"
$pkgDir = Join-Path $outDir "${pkgName}_Test"
Write-Host "`nInstaller folder: $pkgDir" -ForegroundColor Green

if ($Zip) {
    $zipPath = Join-Path $outDir "$pkgName.zip"
    Compress-Archive -Path "$pkgDir\*" -DestinationPath $zipPath -Force
    Write-Host "Release asset:    $zipPath" -ForegroundColor Green
}
