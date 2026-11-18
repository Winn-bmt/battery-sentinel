# 编译 HID 自测程序：.NET Framework 4.8 + dotnet SDK 自带 Roslyn（无 NuGet）
# 用法（本机执行策略禁止直接跑 .ps1，用 -ExecutionPolicy Bypass 调用）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-harness.ps1
$ErrorActionPreference = 'Stop'

$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc  = 'C:\Program Files\dotnet\sdk\3.0.101\Roslyn\bincore\csc.dll'
$root = Split-Path -Parent $PSScriptRoot          # ...\BatterySentinel
$src  = Join-Path $root 'src\Hid'
$out  = Join-Path $PSScriptRoot 'HidHarness.exe'

$files = @(Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName })
$files += (Join-Path $PSScriptRoot 'HidHarness.cs')

Write-Host "源文件："
$files | ForEach-Object { Write-Host "  $_" }

& dotnet $csc /nologo /target:exe /langversion:8.0 /optimize+ "/out:$out" /nostdlib+ `
    "/reference:$fw\mscorlib.dll" `
    "/reference:$fw\System.dll" `
    "/reference:$fw\System.Core.dll" `
    "/reference:$fw\System.Xaml.dll" `
    "/reference:$fw\WPF\WindowsBase.dll" `
    "/reference:$fw\WPF\PresentationCore.dll" `
    "/reference:$fw\WPF\PresentationFramework.dll" `
    $files

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "编译成功：$out"
