# Build BatterySentinel (.NET Framework 4.8 + WPF, no SDK workloads needed)
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-Run]
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$outDir = Join-Path $root 'dist\BatterySentinel'
$exe = Join-Path $outDir 'BatterySentinel.exe'

$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = 'C:\Program Files\dotnet\sdk\3.0.101\Roslyn\bincore\csc.dll'
if (-not (Test-Path $csc)) {
    $csc = (Get-ChildItem 'C:\Program Files\dotnet\sdk' -Recurse -Filter csc.dll -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1).FullName
}
if (-not $csc) { throw 'Roslyn csc.dll not found' }
if (-not (Test-Path (Join-Path $fw 'WPF\PresentationFramework.dll'))) { throw '.NET Framework 4.8 WPF reference assemblies not found' }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$files = Get-ChildItem $srcDir -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
Write-Host ("source files: {0}" -f $files.Count)

$refs = @()
foreach ($a in @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xml.dll')) {
    $p = Join-Path $fw $a
    if (Test-Path $p) { $refs += '/reference:' + $p }
}
foreach ($a in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) {
    $refs += '/reference:' + (Join-Path $fw ('WPF\' + $a))
}

$cscArgs = @(
    $csc,
    '/nologo',
    '/target:winexe',
    '/langversion:8.0',
    '/optimize+',
    '/platform:anycpu',
    '/deterministic+',
    ('/out:' + $exe),
    ('/win32icon:' + (Join-Path $assets 'app.ico'))
) + $refs + $files

& dotnet @cscArgs
if ($LASTEXITCODE -ne 0) { throw ("compile failed (exit {0})" -f $LASTEXITCODE) }

$doc1 = Get-ChildItem $root -Filter '*.md' -File
foreach ($d in $doc1) { Copy-Item $d.FullName (Join-Path $outDir $d.Name) -Force }

$size = (Get-Item $exe).Length
Write-Host ("OK: {0}  ({1:N0} KB)" -f $exe, ($size / 1KB))

if ($Run) { Start-Process -FilePath $exe }
