param([switch]$AlreadyBuilt)
$ErrorActionPreference = 'Stop'
$appHome=$env:STATUS_ISLAND_HOME
if([string]::IsNullOrWhiteSpace($appHome)){$appHome=Join-Path $env:LOCALAPPDATA 'StatusIsland'}
$logs = Join-Path $appHome 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$exe = Join-Path $PSScriptRoot 'state\build\MediaContextDiagnostic.exe'
if (-not $AlreadyBuilt -and -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
  & (Join-Path $PSScriptRoot 'Build.cmd')
  if ($LASTEXITCODE -ne 0) { throw '包内媒体诊断编译失败；没有安装或下载软件。' }
}
$exe = Join-Path $PSScriptRoot 'state\build\MediaContextDiagnostic.exe'
$log = Join-Path $logs 'smtc-latest.log'
$err = Join-Path $logs 'smtc-stderr.log'
$child = $null
try {
  $child = Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $err
  if (-not $child.WaitForExit(8000)) {
    # Only this just-created diagnostic helper, never a player or the existing sidebar.
    $child.Kill()
    $child.WaitForExit()
    Add-Content -LiteralPath $log -Encoding UTF8 -Value 'RESULT=OWN_DIAGNOSTIC_TIMEOUT'
  } else { $child.WaitForExit() }
} finally {
  if ($null -ne $child) { $child.Dispose() }
  # Remove stderr even if starting or waiting fails; it may contain local paths.
  if (Test-Path -LiteralPath $err) { Remove-Item -LiteralPath $err -Force }
}
Write-Host '只读 SMTC 诊断完成。日志：state\logs\smtc-latest.log（仅公开媒体信息/错误码，无账号身份）。'
