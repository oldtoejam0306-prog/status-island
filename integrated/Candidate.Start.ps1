$global:SidebarStartupWatch=[Diagnostics.Stopwatch]::StartNew()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Start the sidebar from Launch.cmd or Launch.vbs.' }
$SidebarHome = $env:STATUS_ISLAND_HOME
if ([string]::IsNullOrWhiteSpace($SidebarHome)) { $SidebarHome = Join-Path $env:LOCALAPPDATA 'StatusIsland' }
$SidebarHome = [IO.Path]::GetFullPath($SidebarHome)
$env:STATUS_ISLAND_HOME = $SidebarHome
$gate = New-Object Threading.Mutex($false, 'Local\StatusIslandColumnPreview')
$owned = $false
try {
  try { $owned = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
  if (-not $owned) {
    [Windows.Forms.MessageBox]::Show('The sidebar is already running. Use the existing window.','Status Island') | Out-Null
    exit 0
  }
  $logs = Join-Path $SidebarHome 'logs'
  [IO.Directory]::CreateDirectory($logs) | Out-Null
  & (Join-Path $PSScriptRoot 'Candidate.State.ps1') -Initialize -StateHome $SidebarHome | Out-Null
  & (Join-Path $PSScriptRoot 'Candidate.Build.ps1') | Out-Null
  $context=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');processId=$PID;hostVersion=$PSVersionTable.PSVersion.ToString();hashImplementation='dotnet-sha256';threadApartment=[Threading.Thread]::CurrentThread.GetApartmentState().ToString()}
  [IO.File]::WriteAllText((Join-Path $logs 'startup-context.json'),($context|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
  $pinkyStart = Join-Path $PSScriptRoot 'Start-PinkyFusion.ps1'
  $pinkyConfig = Join-Path $SidebarHome 'pinky-start.json'
  if ([IO.File]::Exists($pinkyStart) -and [IO.File]::Exists($pinkyConfig)) {
    $normalHost = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',('"' + $pinkyStart + '"'),'-Background','-IfStopped')
    try {
      Start-Process -FilePath $normalHost -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'pinky-startup.json') -RedirectStandardError (Join-Path $logs 'pinky-startup-error.txt') | Out-Null
    } catch { [IO.File]::WriteAllText((Join-Path $logs 'pinky-startup-error.txt'), $_.Exception.Message) }
  }
  & (Join-Path $PSScriptRoot 'StatusIsland.Integrated.ps1')
} catch {
  try{$failure=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');processId=$PID;hostVersion=$PSVersionTable.PSVersion.ToString();hashImplementation='dotnet-sha256';error=$_.Exception.Message};[IO.File]::WriteAllText((Join-Path $logs 'startup-error.json'),($failure|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}catch{}
  $message = $_.Exception.Message.Replace($PSScriptRoot,'<application directory>')
  Write-Host ('Startup failed: ' + $message) -ForegroundColor Red
  [Windows.Forms.MessageBox]::Show(('Startup failed: ' + $message + "`nNo software was downloaded and no other application was stopped."),'Status Island') | Out-Null
  exit 1
} finally {
  if ($owned) { try { $gate.ReleaseMutex() } catch {} }
  $gate.Dispose()
}
