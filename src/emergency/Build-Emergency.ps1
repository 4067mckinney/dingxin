param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\app'),
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutFile)) { $OutFile = Join-Path $root 'StudyFocus_EmergencyStop.exe' }
$OutFile = [IO.Path]::GetFullPath($OutFile)
$SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw "csc.exe not found: $compiler" }
$programPath = Join-Path $SourceDir 'Program.cs'
if (-not (Test-Path $programPath)) { throw "SourceDir invalid: $SourceDir" }

# read app version from Program.cs and sync the tool's supported-version gate
$program = [System.IO.File]::ReadAllText($programPath)
$match = [regex]::Match($program, 'AssemblyVersion\("(\d+)\.(\d+)')
if (-not $match.Success) { throw "AssemblyVersion not found in Program.cs" }
$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value

$toolPath = Join-Path $root 'EmergencyStop.cs'
$toolSource = [System.IO.File]::ReadAllText($toolPath)
$toolSource = [regex]::Replace($toolSource, 'new Version\(\d+,\s*\d+,\s*99,\s*0\)', "new Version($major, $minor, 99, 0)")

$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force -Path $obj | Out-Null
$patched = Join-Path $obj 'EmergencyStop.cs'
[System.IO.File]::WriteAllText($patched, $toolSource, (New-Object System.Text.UTF8Encoding($false)))

$icon = Join-Path $SourceDir 'Focus.ico'
$resourceArgs = @("/win32icon:$icon")
$manifest = Join-Path $SourceDir 'StudyFocus.manifest'
$toolVersion = [regex]::Match($toolSource, 'AssemblyVersion\("(\d+\.\d+\.\d+\.\d+)"\)')
if (-not $toolVersion.Success) { throw 'AssemblyVersion not found in EmergencyStop.cs.' }
$toolManifest = Join-Path $obj 'EmergencyStop.manifest'
$template = [IO.File]::ReadAllText($manifest)
$rendered = $template.Replace('__ASSEMBLY_VERSION__', $toolVersion.Groups[1].Value)
$rendered = $rendered.Replace('name="StudyFocus"', 'name="StudyFocus.EmergencyStop"')
[IO.File]::WriteAllText($toolManifest, $rendered, [Text.UTF8Encoding]::new($false))
$resourceArgs += "/win32manifest:$toolManifest"

$references = @('/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Security.dll', '/r:System.Web.Extensions.dll')
$sources = @(
    $patched,
    (Join-Path $SourceDir 'ProductBrand.cs'),
    (Join-Path $SourceDir 'Engine.cs'),
    (Join-Path $SourceDir 'AppCatalog.cs'),
    (Join-Path $SourceDir 'AdvancedCredential.cs'),
    (Join-Path $SourceDir 'ProcessGuard.cs')
)

Write-Host ("app version {0}.{1} -> emergency tool supports <= {0}.{1}.99" -f $major, $minor)
& $compiler /nologo /utf8output /target:winexe /platform:anycpu /optimize+ /codepage:65001 /main:StudyFocus.EmergencyStop @resourceArgs /out:$OutFile @references @sources
if ($LASTEXITCODE -ne 0) { throw "build failed: $LASTEXITCODE" }
Get-Item -LiteralPath $OutFile | Select-Object Name, Length, LastWriteTime
