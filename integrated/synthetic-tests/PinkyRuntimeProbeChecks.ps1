$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\PinkyRuntimeProbe.ps1')
$root=Join-Path $env:TEMP ('SidebarPipeProbe-'+[guid]::NewGuid().ToString('N'))
try{
  $name=Get-PinkyPipeName $root
  $prefix='\\.\pipe\pinky-'
  if(-not $name.StartsWith($prefix,[StringComparison]::Ordinal) -or $name.Substring($prefix.Length) -notmatch '^[a-f0-9]{16}$'){throw 'Pipe name format is invalid.'}
  $missing=Test-PinkyNamedPipe $name {param($n)@{Succeeded=$false;ErrorCode=2}}
  $busy=Test-PinkyNamedPipe $name {param($n)@{Succeeded=$false;ErrorCode=231}}
  $timeout=Test-PinkyNamedPipe $name {param($n)@{Succeeded=$false;ErrorCode=121}}
  if($missing -or -not $busy -or -not $timeout){throw 'Pipe state mapping is incorrect.'}
  $socket=Test-PinkySocket $root {param($n)@{Succeeded=$false;ErrorCode=2}}
  if($socket){throw 'Missing pipe must be reported offline.'}
  $denied=$false;try{Test-PinkyNamedPipe $name {param($n)@{Succeeded=$false;ErrorCode=5}}|Out-Null}catch{$denied=$true}
  if(-not $denied){throw 'Access denial must remain visible.'}
  'PinkyRuntimeProbeChecks: passed (absent, busy, timeout, access denied; fake probe only).'
}finally{if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
