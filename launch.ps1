param(
    [switch]$Elevated,
    [switch]$Settings,
    [switch]$Restart,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $root 'app'
$exe = Join-Path $appDir 'SysMonitorBar.exe'

# ---------------- 退出 ----------------
if ($Stop) {
    $running = Get-Process -Name 'SysMonitorBar' -ErrorAction SilentlyContinue
    if (-not $running) {
        Write-Host '  程序当前没有在运行。' -ForegroundColor Gray
    }
    else {
        & taskkill.exe /IM SysMonitorBar.exe /F 2>&1 | Out-Null
        Start-Sleep -Milliseconds 800
        if (Get-Process -Name 'SysMonitorBar' -ErrorAction SilentlyContinue) {
            Write-Host '  退出失败 —— 程序若以管理员身份运行，请用管理员身份执行本脚本。' -ForegroundColor Yellow
        }
        else {
            Write-Host '  已退出顶部硬件监控条。' -ForegroundColor Green
        }
    }
    Start-Sleep -Seconds 1
    exit 0
}

# ---------------- 启动 ----------------
Write-Host ''
Write-Host '  顶部硬件监控条' -ForegroundColor Cyan
Write-Host '  ----------------------------------------' -ForegroundColor DarkGray

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "  [错误] 找不到程序：$exe" -ForegroundColor Red
    Write-Host '  请先运行 build.cmd 编译。' -ForegroundColor Yellow
    Read-Host '  按回车退出'
    exit 1
}

if ($Restart) {
    Write-Host '  正在结束已在运行的实例…' -ForegroundColor DarkGray
    & taskkill.exe /IM SysMonitorBar.exe /F 2>&1 | Out-Null
    Start-Sleep -Milliseconds 1200
}

$argList = @()
if ($Settings) { $argList += '--settings' }

$running = Get-Process -Name 'SysMonitorBar' -ErrorAction SilentlyContinue
if ($running -and -not $Settings -and -not $Restart) {
    Write-Host '  程序已经在运行了。' -ForegroundColor Yellow
    Write-Host '  请在右下角托盘图标上右键 → 设置 / 显示隐藏 / 退出。' -ForegroundColor Gray
    Start-Sleep -Seconds 2
    exit 0
}

$sp = @{
    FilePath         = $exe
    WorkingDirectory = $appDir
}
if ($argList.Count -gt 0) { $sp['ArgumentList'] = $argList }
if ($Elevated) { $sp['Verb'] = 'RunAs' }

try {
    if ($Elevated) {
        Write-Host '  正在请求管理员权限（会弹出 UAC 确认框）…' -ForegroundColor Yellow
        Write-Host '  提权后能多读到 CPU 风扇转速等需要内核驱动的数据。' -ForegroundColor Gray
    }
    else {
        Write-Host '  以普通权限启动。' -ForegroundColor Gray
    }
    Start-Process @sp
    Write-Host '  已启动。' -ForegroundColor Green
}
catch {
    Write-Host "  启动失败：$($_.Exception.Message)" -ForegroundColor Red
    Read-Host '  按回车退出'
    exit 1
}

Start-Sleep -Milliseconds 800
