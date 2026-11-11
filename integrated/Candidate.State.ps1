param([switch]$Initialize,[switch]$FunctionsOnly,[string]$StateHome)
$ErrorActionPreference='Stop'
function Get-SidebarSHA256([byte[]]$Bytes){$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}
function Assert-SidebarPlainPath([string]$Value){$cursor=[IO.Path]::GetFullPath($Value);while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'State_reparse_path_denied'}};$parent=[IO.Path]::GetDirectoryName($cursor);if($parent-eq $cursor){break};$cursor=$parent}}
function Copy-SidebarState([string]$From,[string]$To){
  if([string]::IsNullOrWhiteSpace($From)-or [string]::IsNullOrWhiteSpace($To)){throw 'Both state paths are required.'}
  $fromPath=[IO.Path]::GetFullPath($From);$toPath=[IO.Path]::GetFullPath($To);if($fromPath-eq $toPath){throw 'State source and target must differ.'}
  Assert-SidebarPlainPath $fromPath;Assert-SidebarPlainPath $toPath;[IO.Directory]::CreateDirectory($toPath)|Out-Null
  $receipt=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');files=@()}
  foreach($name in @('layout.xml','layout.xml.bak','pos.json')){
    $source=Join-Path $fromPath $name;$target=Join-Path $toPath $name;Assert-SidebarPlainPath $source;Assert-SidebarPlainPath $target
    if([IO.File]::Exists($target)){$receipt.files+=@{name=$name;status='existing-target-preserved'};continue}
    if(-not [IO.File]::Exists($source)){$receipt.files+=@{name=$name;status='source-not-present'};continue}
    if((Get-Item -LiteralPath $source).Length-gt 1048576){throw 'State_file_too_large'}
    $bytes=[IO.File]::ReadAllBytes($source);$again=[IO.File]::ReadAllBytes($source);$digest=Get-SidebarSHA256 $bytes;$check=Get-SidebarSHA256 $again
    if($digest-cne $check){throw 'Source_state_changed_during_read'}
    $backups=Join-Path $toPath 'backups\imported-state';Assert-SidebarPlainPath $backups;[IO.Directory]::CreateDirectory($backups)|Out-Null
    $backup=Join-Path $backups ($digest+'-'+$name)
    if([IO.File]::Exists($backup)){$backupBytes=[IO.File]::ReadAllBytes($backup);if((Get-SidebarSHA256 $backupBytes)-cne $digest){throw 'State_backup_corrupt'}}else{$f=[IO.File]::Open($backup,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}}
    $temporary=$target+'.handoff-'+[guid]::NewGuid().ToString('N')+'.tmp';Assert-SidebarPlainPath $temporary
    $f=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
    try{[IO.File]::Move($temporary,$target)}catch{if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)};throw}
    $receipt.files+=@{name=$name;status='copied-with-backup';bytes=$bytes.Length;sha256=$digest}
  }
  return $receipt
}
if($Initialize){if([string]::IsNullOrWhiteSpace($StateHome)){throw 'StateHome must be specified.'};$root=[IO.Path]::GetFullPath($StateHome);[IO.Directory]::CreateDirectory($root)|Out-Null;[IO.Directory]::CreateDirectory((Join-Path $root 'logs'))|Out-Null;foreach($pair in @(@('config.example.json','config.json'),@('pinky-host-connection.example.json','pinky-host-connection.json'),@('pinky-start.example.json','pinky-start.json'))){$sample=Join-Path $PSScriptRoot $pair[0];$target=Join-Path $root $pair[1];if([IO.File]::Exists($sample)-and -not [IO.File]::Exists($target)){Copy-Item -LiteralPath $sample -Destination $target}}}
