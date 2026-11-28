[CmdletBinding()]
param(
    [string]$OutputDirectory = $PSScriptRoot,
    [switch]$Verify,
    [switch]$VerifyEdge,
    [string]$PreviousPackage,
    [string]$CertificateThumbprint,
    [string]$SignTool = 'signtool.exe',
    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Security.dll', '/r:System.Web.Extensions.dll', '/r:System.Management.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
$sources = @('Program.cs', 'ProductBrand.cs', 'BrandArtwork.cs', 'Engine.cs', 'Proxy.cs', 'ProcessGuard.cs', 'AppCatalog.cs', 'AppImages.cs', 'AdvancedCredential.cs',
    'FocusSounds.cs', 'FocusHistory.cs', 'Motion.cs', 'SystemProxy.cs', 'MainWindow.cs', 'MainWindowLayout.cs', 'MainWindowActions.cs', 'MainWindowAdvanced.cs',
    'MainWindowSchedules.cs', 'MainWindowAllowlists.cs', 'MainWindowStatistics.cs', 'MainWindowSettings.cs',
    'MainWindowReference.cs', 'MainWindowPolishedControls.cs', 'MainWindowPolishedApps.cs', 'FloatingTimerWindow.cs', 'SetupWindow.cs')
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($PreviousPackage) { $PreviousPackage = (Resolve-Path -LiteralPath $PreviousPackage).Path }
if ($CertificateThumbprint) {
    if (-not $TimestampUrl) { throw 'Supply -TimestampUrl when signing a release.' }
    $signer = (Get-Command $SignTool -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x C# compiler is required.' }
$null = New-Item -ItemType Directory -Path $output -Force
$package = Join-Path $output 'StudyFocus_Setup.exe'
$manifest = Join-Path $output ('StudyFocus-' + [Guid]::NewGuid().ToString('N') + '.manifest')
Push-Location -LiteralPath $PSScriptRoot
try {
    $version = [regex]::Match((Get-Content -LiteralPath 'Program.cs' -Raw -Encoding UTF8), 'AssemblyVersion\("(\d+\.\d+\.\d+\.\d+)"\)')
    if (-not $version.Success) { throw 'Cannot determine the assembly version from Program.cs.' }
    $template = Get-Content -LiteralPath 'StudyFocus.manifest' -Raw -Encoding UTF8
    if (-not $template.Contains('__ASSEMBLY_VERSION__')) { throw 'The manifest version placeholder is missing.' }
    [IO.File]::WriteAllText($manifest, $template.Replace('__ASSEMBLY_VERSION__', $version.Groups[1].Value), [Text.UTF8Encoding]::new($false))
    $tool = Join-Path (Split-Path $PSScriptRoot -Parent) 'emergency\StudyFocus_EmergencyStop.exe'
    $resourceOption = @()
    if (Test-Path -LiteralPath $tool) { $resourceOption = @(('/resource:"' + $tool + '",StudyFocus_EmergencyStop.exe')) }
    $sourcesZip = Join-Path $PSScriptRoot 'Sources.zip'
    if (Test-Path -LiteralPath $sourcesZip) { $resourceOption += ('/resource:"' + $sourcesZip + '",StudyFocus_Sources.zip') }
    $compileOptions = @('/nologo', '/utf8output', '/platform:anycpu', '/optimize+', '/codepage:65001', '/win32icon:Focus.ico', ('/win32manifest:' + $manifest)) + $resourceOption
    & $compiler @compileOptions /target:winexe ('/out:' + $package) @references @sources
    if ($LASTEXITCODE -ne 0) { throw 'StudyFocus compilation failed.' }

    if ($CertificateThumbprint) {
        & $signer sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $package
        if ($LASTEXITCODE -ne 0 -or (Get-AuthenticodeSignature -LiteralPath $package).Status -ne 'Valid') {
            throw 'Signing or signature verification failed.'
        }
    }

    if ($Verify -or $VerifyEdge) {
        $verification = Join-Path $output 'verification'
        $null = New-Item -ItemType Directory -Path $verification -Force
        $harness = Join-Path $verification 'FocusVerification.exe'
        & $compiler @compileOptions /target:exe /define:FOCUS_TEST /main:FocusVerification ('/out:' + $harness) @references @sources FocusVerification.cs
        if ($LASTEXITCODE -ne 0) { throw 'Verification compilation failed.' }
        $fixtures = @('StudyFocusAllowed', 'StudyFocusBlocked') | ForEach-Object { Join-Path $verification ($_ + '.exe') }
        foreach ($fixture in $fixtures) {
            & $compiler /nologo /utf8output /target:winexe ('/out:' + $fixture) @references Probe.cs
            if ($LASTEXITCODE -ne 0) { throw 'Window fixture compilation failed.' }
        }
        $verifyArgs = $fixtures
        if ($PreviousPackage) { $verifyArgs = $fixtures + @($PreviousPackage, $package) }
        if ($VerifyEdge) { $verifyArgs += '--edge' }
        Push-Location -LiteralPath $verification
        try {
            & $harness @verifyArgs
            if ($LASTEXITCODE -ne 0) { throw 'StudyFocus verification failed.' }
        }
        finally { Pop-Location }
    }

    Get-Item -LiteralPath $package | Select-Object Name, Length, @{Name = 'Version'; Expression = { $_.VersionInfo.ProductVersion }}
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest -Force }
}
