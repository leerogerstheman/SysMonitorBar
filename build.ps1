#Requires -Version 5.1
<#
    编译「顶部硬件监控条」。

    需要 .NET 8 SDK（只装运行时不够）。
    脚本会自己找 SDK，查找顺序：
      1. 环境变量 SYSMONITORBAR_DOTNET 指向的 dotnet.exe
      2. PATH 里的 dotnet
      3. %ProgramFiles%\dotnet\dotnet.exe
      4. %USERPROFILE%\.dotnet\dotnet.exe
      5. %DOTNET_ROOT%\dotnet.exe

    用法：
      build.cmd            普通编译
      build.cmd -Clean     先清掉 bin/obj 再编译
#>
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\SysMonitorBar\SysMonitorBar.csproj'
$out = Join-Path $root 'app'

Write-Host ''
Write-Host '  顶部硬件监控条 —— 编译' -ForegroundColor Cyan
Write-Host '  ----------------------------------------' -ForegroundColor DarkGray

function Test-Sdk([string]$exe) {
    try {
        $sdks = & $exe --list-sdks 2>$null
        if (-not $sdks) { return $false }
        return [bool]($sdks | Where-Object { $_ -match '^\d+\.' })
    }
    catch { return $false }
}

$candidates = New-Object System.Collections.ArrayList

# 0) 项目根目录下的 dotnet-path.txt（本机专用，已在 .gitignore 里排除）
#    第一行写 dotnet.exe 的完整路径即可，适合 SDK 装在非标准位置的机器
$pathFile = Join-Path $root 'dotnet-path.txt'
if (Test-Path -LiteralPath $pathFile) {
    $custom = (Get-Content -LiteralPath $pathFile -Encoding UTF8 |
               Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') } |
               Select-Object -First 1)
    if ($custom) { [void]$candidates.Add($custom.Trim()) }
}

# 1) 环境变量
if ($env:SYSMONITORBAR_DOTNET) { [void]$candidates.Add($env:SYSMONITORBAR_DOTNET) }

# 2) 常见的安装位置
[void]$candidates.Add('dotnet')
[void]$candidates.Add((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))
[void]$candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe'))
[void]$candidates.Add((Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'))
[void]$candidates.Add((Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'))
if ($env:DOTNET_ROOT) { [void]$candidates.Add((Join-Path $env:DOTNET_ROOT 'dotnet.exe')) }

$dotnet = $null
foreach ($c in $candidates) {
    if ($c -eq 'dotnet' -or (Test-Path -LiteralPath $c)) {
        if (Test-Sdk $c) { $dotnet = $c; break }
    }
}

if (-not $dotnet) {
    Write-Host '  [错误] 找不到带 SDK 的 .NET。' -ForegroundColor Red
    Write-Host ''
    Write-Host '  安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Gray
    Write-Host '  注意：只装「运行时」是不够的，必须是「SDK」。' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  已经装在别处的话，设一个环境变量指向它：' -ForegroundColor Gray
    Write-Host '      set SYSMONITORBAR_DOTNET=D:\path\to\dotnet.exe' -ForegroundColor Gray
    exit 1
}

$ver = (& $dotnet --version 2>$null | Select-Object -First 1)
Write-Host "  SDK: $dotnet  ($ver)" -ForegroundColor Gray

if ($Clean) {
    Write-Host '  清理 bin / obj …' -ForegroundColor Gray
    Get-ChildItem (Join-Path $root 'src') -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# 程序在运行时会锁住 app\ 里的 exe/dll，编译必然失败并刷一屏重试警告。
# 这里提前拦下来，给一句人话。
$running = Get-Process -Name 'SysMonitorBar' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host ''
    Write-Host '  [无法编译] 程序正在运行，它锁住了 app\ 里的文件。' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  请先退出程序：' -ForegroundColor Gray
    Write-Host '     · 托盘图标右键 → 退出，或者' -ForegroundColor Gray
    Write-Host '     · 双击「退出.cmd」' -ForegroundColor Gray
    Write-Host '  如果程序是以管理员身份运行的，退出脚本也要用管理员身份执行。' -ForegroundColor DarkGray
    exit 1
}

Write-Host '  正在编译…' -ForegroundColor Gray
& $dotnet publish $proj -c Release -r win-x64 --self-contained false -o $out -v q --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host '  编译失败。' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ''
Write-Host "  完成，程序已输出到：$out" -ForegroundColor Green
Write-Host '  双击「启动.cmd」或「启动-管理员.cmd」即可运行。' -ForegroundColor Gray
