# 状态台 / Status Island — 安装脚本
# 用法：右键“使用 PowerShell 运行”，或在 PowerShell 里执行 .\install.ps1
$ErrorActionPreference = 'Stop'

$srcDir  = Join-Path $PSScriptRoot 'src'
$destDir = Join-Path $env:LOCALAPPDATA 'StatusIsland'
$wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'

if (-not (Test-Path (Join-Path $srcDir 'StatusIsland.ps1'))) {
    throw "找不到 src\StatusIsland.ps1，请在仓库根目录运行本脚本。"
}

New-Item -ItemType Directory -Force -Path $destDir | Out-Null
Copy-Item (Join-Path $srcDir 'StatusIsland.ps1') $destDir -Force
Copy-Item (Join-Path $srcDir 'launcher.vbs') $destDir -Force
Write-Host "已安装到 $destDir" -ForegroundColor Green

$cfgPath = Join-Path $destDir 'config.json'
if (-not (Test-Path $cfgPath)) {
    $template = [ordered]@{
        macHost           = '100.64.0.1'
        socksPort         = 1080
        clashGroup        = '选择节点'
        clashMacProxyName = 'SG-Mac-SOCKS5'
        tailscaleExe      = 'C:\Program Files\Tailscale\tailscale.exe'
        nvidiaSmi         = 'C:\Windows\System32\nvidia-smi.exe'
        dshPort           = 3081
        codexProcess      = 'ChatGPT'
        neteaseProcess    = 'cloudmusic'
        watchdogLockPath  = ''
        tunnelStartScript = ''
        clashExe          = 'clash-verge.exe'
    }
    ($template | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    Write-Host "已生成配置 $cfgPath —— 请按需修改（尤其是 macHost / clashGroup）" -ForegroundColor Yellow
}

$ws = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path) {
    $sc = $ws.CreateShortcut($path)
    $sc.TargetPath = $wscript
    $sc.Arguments = '"' + (Join-Path $destDir 'launcher.vbs') + '"'
    $sc.WorkingDirectory = $destDir
    $sc.IconLocation = 'C:\Windows\System32\imageres.dll,15'
    $sc.Description = '状态台 Status Island'
    $sc.WindowStyle = 7
    $sc.Save()
}

$desktop = $ws.SpecialFolders('Desktop')
New-Shortcut (Join-Path $desktop '状态台.lnk')
$startup = [Environment]::GetFolderPath('Startup')
New-Shortcut (Join-Path $startup '状态台.lnk')
Write-Host "已创建 桌面 + 开机自启 快捷方式" -ForegroundColor Green

Write-Host ""
Write-Host "完成。现在可从桌面打开；鼠标移上去会展开，移开自动收起。" -ForegroundColor Cyan
Write-Host "想立即试跑（不装快捷方式）：powershell -File `"$destDir\StatusIsland.ps1`" -Once" -ForegroundColor DarkGray
