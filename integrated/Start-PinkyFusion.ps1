[CmdletBinding()]
param([switch]$Background,[switch]$PlanOnly,[switch]$IfStopped)
$ErrorActionPreference='Stop'
$appHome=$env:STATUS_ISLAND_HOME
if([string]::IsNullOrWhiteSpace($appHome)){$appHome=Join-Path $env:LOCALAPPDATA 'StatusIsland'}
$appHome=[IO.Path]::GetFullPath($appHome)
$configPath=Join-Path $appHome 'pinky-start.json'
if(-not [IO.File]::Exists($configPath)){[pscustomobject]@{state='unconfigured';enabled=$false;realStartAttempted=$false}|ConvertTo-Json;return}
try{$cfg=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json}catch{throw 'Pinky integration configuration is invalid.'}
if($cfg.schema -cne 'sidebar.pinky-start.v1' -or $cfg.enabled -isnot [bool]){throw 'Pinky integration configuration is invalid.'}
if(-not $cfg.enabled){[pscustomobject]@{state='disabled';enabled=$false;realStartAttempted=$false}|ConvertTo-Json;return}
$probePath=Join-Path $PSScriptRoot 'PinkyRuntimeProbe.ps1'
if(-not [IO.File]::Exists($probePath)){throw 'Pinky runtime probe helper is missing.'}
. $probePath
foreach($field in @('candidateExecutable','candidateSha256','officialExecutable','officialSha256','profileDirectory','hostDirectory')){if($cfg.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($cfg.$field)){throw 'Pinky integration configuration is incomplete.'}}
foreach($field in @('candidateSha256','officialSha256')){if($cfg.$field -notmatch '^[0-9a-fA-F]{64}$'){throw 'Pinky integration configuration is invalid.'}}
foreach($field in @('candidateExecutable','officialExecutable','profileDirectory','hostDirectory')){if(-not [IO.Path]::IsPathRooted($cfg.$field)){throw 'Pinky integration paths must be absolute.'}}
$candidate=[IO.Path]::GetFullPath($cfg.candidateExecutable);$official=[IO.Path]::GetFullPath($cfg.officialExecutable);$profileDir=[IO.Path]::GetFullPath($cfg.profileDirectory);$hostDir=[IO.Path]::GetFullPath($cfg.hostDirectory)
$hostRoot=[IO.Path]::GetFullPath((Join-Path (Join-Path $env:LOCALAPPDATA 'StatusIsland') 'PinkyHost'))
if(-not [string]::Equals($hostDir,$hostRoot,[StringComparison]::OrdinalIgnoreCase) -and -not $hostDir.StartsWith(($hostRoot.TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Pinky host directory must be under the local Status Island data directory.'}
if($candidate -ieq $official -or -not [IO.File]::Exists($candidate) -or -not [IO.File]::Exists($official) -or -not [IO.Directory]::Exists($profileDir)){throw 'Configured Pinky files or profile are unavailable.'}
function Get-PinkySHA256([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
if((Get-PinkySHA256 $candidate) -ine $cfg.candidateSha256 -or (Get-PinkySHA256 $official) -ine $cfg.officialSha256){throw 'Configured Pinky executable hash does not match.'}
$hostConfig=Join-Path $appHome 'pinky-host-connection.json'
if(-not [IO.File]::Exists($hostConfig)){throw 'Pinky host connection is not configured.'}
$hostCfg=Get-Content -LiteralPath $hostConfig -Raw -Encoding UTF8|ConvertFrom-Json
if($hostCfg.schema -cne 'pinky.sidebar-connection.v1' -or -not $hostCfg.enabled -or $hostCfg.pinkyExecutable -ine $candidate -or $hostCfg.directory -ine $hostDir){throw 'Pinky host connection does not match the configured candidate.'}
$hold=Join-Path $appHome 'pinky-switch-hold.json'
if([IO.File]::Exists($hold)){[pscustomobject]@{state='unavailable';reason='handoff-window-held';realStartAttempted=$false}|ConvertTo-Json;return}
if($PlanOnly){[pscustomobject]@{state='ready';version=(Get-Item $candidate).VersionInfo.FileVersion;realStartAttempted=$false}|ConvertTo-Json;return}
function New-PinkyStart([string]$Executable,[string]$Arguments){$start=[Diagnostics.ProcessStartInfo]::new($Executable,$Arguments);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.WorkingDirectory=Split-Path -Parent $Executable;$start.EnvironmentVariables['PINKY_HOME']=$profileDir;return $start}
function Invoke-PinkyControl([string]$Command){$start=New-PinkyStart $official $Command;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$process=[Diagnostics.Process]::Start($start);try{$output=$process.StandardOutput.ReadToEndAsync();$errorOutput=$process.StandardError.ReadToEndAsync();if(-not $process.WaitForExit(18000)){throw 'Pinky control timed out.'};return @{exitCode=$process.ExitCode;output=$output.GetAwaiter().GetResult();errorOutput=$errorOutput.GetAwaiter().GetResult()}}finally{$process.Dispose()}}
function Write-PinkyRuntime([string]$path,[hashtable]$record){$temporary=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp';[IO.File]::WriteAllText($temporary,($record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));if([IO.File]::Exists($path)){[IO.File]::Replace($temporary,$path,$null)}else{[IO.File]::Move($temporary,$path)}}
$runtimePath=Join-Path $appHome 'pinky-runtime.json'
$gate=[Threading.Mutex]::new($false,'Local\StatusIslandPinkyFusionStart');$owned=$false
try{
 try{$owned=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException] {$owned=$true}
 if(-not $owned){[pscustomobject]@{state='unavailable';reason='start-in-progress';realStartAttempted=$false}|ConvertTo-Json;return}
 if([IO.File]::Exists($runtimePath)){
  try{$record=Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8|ConvertFrom-Json;$existing=Get-Process -Id ([int]$record.processId) -ErrorAction SilentlyContinue
   if($existing -and $existing.StartTime.ToUniversalTime().ToFileTimeUtc().ToString() -ceq [string]$record.birthFileTime -and [IO.Path]::GetFullPath($existing.MainModule.FileName) -ieq $candidate -and $record.sha256 -ieq $cfg.candidateSha256 -and $record.profile -ceq $profileDir -and $record.hostDirectory -ceq $hostDir){[pscustomobject]@{state='already-running';processId=$existing.Id;version=(Get-Item $candidate).VersionInfo.FileVersion;realStartAttempted=$false}|ConvertTo-Json;return}
  }catch [System.ArgumentException]{}catch [System.InvalidOperationException]{}catch [System.ComponentModel.Win32Exception]{}catch [System.UnauthorizedAccessException]{}
 }
 $gatewayRunning=Test-PinkySocket $profileDir
 if($gatewayRunning -and $IfStopped){[pscustomobject]@{state='existing-gateway';realStartAttempted=$false;normalExitRequested=$false}|ConvertTo-Json;return}
 if($gatewayRunning){
  $needs=Invoke-PinkyControl 'needs'
  if($needs.exitCode -ne 0 -or $needs.errorOutput){throw 'The configured Pinky gateway could not be verified.'}
  if($needs.output.Trim() -cne 'Nothing needs you.') {[pscustomobject]@{state='deferred';reason='pending-request';realStartAttempted=$false}|ConvertTo-Json;return}
  $quit=Invoke-PinkyControl 'quit'
  if($quit.exitCode -ne 0 -or $quit.errorOutput){throw 'The configured Pinky gateway did not confirm a normal exit.'}
  $watch=[Diagnostics.Stopwatch]::StartNew();while($watch.ElapsedMilliseconds -lt 3000 -and (Test-PinkySocket $profileDir)){Start-Sleep -Milliseconds 100}
  if(Test-PinkySocket $profileDir){throw 'The configured Pinky gateway did not complete normal exit.'}
 }
 $arguments=if($Background){'-background'}else{''};$start=New-PinkyStart $candidate $arguments;$start.EnvironmentVariables['PINKY_PANEL_HOST_DIR']=$hostDir
 foreach($key in @('PINKY_DEMO_EXIT_WITH_HOST','PINKY_DEMO_INITIAL_DETAILS','PINKY_PREVIEW_OFFSCREEN','MYGO_AUTOMATION','MYGO_E2E','MYGO_GPU')){$start.EnvironmentVariables.Remove($key)}
 $child=[Diagnostics.Process]::Start($start);if($null -eq $child){throw 'Configured Pinky candidate failed to start.'};if($child.WaitForExit(1500)){throw ('Configured Pinky candidate exited with code '+$child.ExitCode)}
 $child.Refresh();$birth=$child.StartTime.ToUniversalTime().ToFileTimeUtc().ToString();$actual=Get-Process -Id $child.Id -ErrorAction Stop
 if($actual.StartTime.ToUniversalTime().ToFileTimeUtc().ToString() -cne $birth -or [IO.Path]::GetFullPath($actual.MainModule.FileName) -ine $candidate){throw 'Configured Pinky candidate process identity did not match.'}
 $record=@{schema=1;processId=$child.Id;birthFileTime=$birth;program=$candidate;sha256=(Get-PinkySHA256 $candidate);profile=$profileDir;hostDirectory=$hostDir;utc=[DateTime]::UtcNow.ToString('o')};Write-PinkyRuntime $runtimePath $record
 [pscustomobject]@{state='started';processId=$child.Id;version=(Get-Item $candidate).VersionInfo.FileVersion;realStartAttempted=$true;phoneActionsSent=$false}|ConvertTo-Json
}finally{if($owned){$gate.ReleaseMutex()};$gate.Dispose()}
