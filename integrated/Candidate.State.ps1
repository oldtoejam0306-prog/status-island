param([switch]$Initialize,[switch]$FunctionsOnly,[string]$StateHome)
$ErrorActionPreference='Stop'
function Get-SidebarSHA256([byte[]]$Bytes){$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}
function Assert-SidebarPlainPath([string]$Value){$cursor=[IO.Path]::GetFullPath($Value);while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'State_reparse_path_denied'}};$parent=[IO.Path]::GetDirectoryName($cursor);if($parent-eq $cursor){break};$cursor=$parent}}
function Copy-SidebarState([string]$From,[string]$To){
  if([string]::IsNullOrWhiteSpace($From)-or [string]::IsNullOrWhiteSpace($To)){throw 'Both state paths are required.'}
  $fromPath=[IO.Path]::GetFullPath($From);$toPath=[IO.Path]::GetFullPath($To);if($fromPath-eq $toPath){throw 'State source and target must differ.'}
  Assert-SidebarPlainPath $fromPath;Assert-SidebarPlainPath $toPath
  $names=@('layout.xml','layout.xml.bak','pos.json','aihot-monitor.json')
  $plans=New-Object System.Collections.ArrayList
  $receipt=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');files=@()}

  # Validate every source, destination, and existing backup before importing
  # any files. Only AIHOT's bounded history gets the larger byte allowance.
  foreach($name in $names){
    $source=Join-Path $fromPath $name;$target=Join-Path $toPath $name
    Assert-SidebarPlainPath $source;Assert-SidebarPlainPath $target
    if([IO.Directory]::Exists($target)){throw 'State_target_invalid'}
    if([IO.File]::Exists($target)){$receipt.files+=@{name=$name;status='existing-target-preserved'};continue}
    if(-not [IO.File]::Exists($source)){$receipt.files+=@{name=$name;status='source-not-present'};continue}
    $limit=if($name-ceq'aihot-monitor.json'){33554432}else{1048576}
    if((Get-Item -LiteralPath $source).Length-gt$limit){throw 'State_file_too_large'}
    $bytes=[IO.File]::ReadAllBytes($source);$again=[IO.File]::ReadAllBytes($source)
    if($bytes.Length-gt$limit-or$again.Length-gt$limit){throw 'State_file_too_large'}
    if($bytes.Length-ne$again.Length){throw 'Source_state_changed_during_read'}
    $digest=Get-SidebarSHA256 $bytes;$check=Get-SidebarSHA256 $again
    if($digest-cne $check){throw 'Source_state_changed_during_read'}
    $backupDirectory=Join-Path $toPath 'backups\imported-state';$backup=Join-Path $backupDirectory ($digest+'-'+$name)
    Assert-SidebarPlainPath $backupDirectory;Assert-SidebarPlainPath $backup
    if([IO.Directory]::Exists($backup)){throw 'State_backup_invalid'}
    $backupExists=[IO.File]::Exists($backup)
    if($backupExists){$backupBytes=[IO.File]::ReadAllBytes($backup);if((Get-SidebarSHA256 $backupBytes)-cne $digest){throw 'State_backup_corrupt'}}
    [void]$plans.Add([pscustomobject]@{name=$name;source=$source;target=$target;bytes=$bytes;limit=$limit;sha256=$digest;backupDirectory=$backupDirectory;backup=$backup;backupExists=$backupExists;backupTemp=$null;temporary=$null})
  }

  if($plans.Count-eq 0){return $receipt}
  [IO.Directory]::CreateDirectory($toPath)|Out-Null
  foreach($plan in $plans){[IO.Directory]::CreateDirectory($plan.backupDirectory)|Out-Null}
  $committed=New-Object System.Collections.ArrayList
  try{
    # Finish and verify all backups and target-side temporary files before the
    # first target appears. File.Move stays within each destination directory.
    foreach($plan in $plans){
      if(-not $plan.backupExists){
        $plan.backupTemp=Join-Path $plan.backupDirectory ('.backup-'+[guid]::NewGuid().ToString('N')+'.tmp')
        Assert-SidebarPlainPath $plan.backupTemp
        $f=[IO.File]::Open($plan.backupTemp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$f.Write($plan.bytes,0,$plan.bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
        $backupBytes=[IO.File]::ReadAllBytes($plan.backupTemp)
        if($backupBytes.Length-gt$plan.limit){throw 'State_file_too_large'}
        if((Get-SidebarSHA256 $backupBytes)-cne $plan.sha256){throw 'State_backup_integrity_failed'}
      }
      $plan.temporary=$plan.target+'.handoff-'+[guid]::NewGuid().ToString('N')+'.tmp';Assert-SidebarPlainPath $plan.temporary
      $f=[IO.File]::Open($plan.temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
      try{$f.Write($plan.bytes,0,$plan.bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
      $stagedBytes=[IO.File]::ReadAllBytes($plan.temporary);$currentBytes=[IO.File]::ReadAllBytes($plan.source)
      if($stagedBytes.Length-gt$plan.limit-or$currentBytes.Length-gt$plan.limit){throw 'State_file_too_large'}
      if($stagedBytes.Length-ne$plan.bytes.Length-or$currentBytes.Length-ne$plan.bytes.Length){throw 'Source_state_changed_during_handoff'}
      if((Get-SidebarSHA256 $stagedBytes)-cne $plan.sha256){throw 'State_import_integrity_failed'}
      if((Get-SidebarSHA256 $currentBytes)-cne $plan.sha256){throw 'Source_state_changed_during_handoff'}
    }
    foreach($plan in $plans){
      if(-not $plan.backupExists){[IO.File]::Move($plan.backupTemp,$plan.backup);$plan.backupTemp=$null}
    }
    foreach($plan in $plans){
      if([IO.File]::Exists($plan.target)-or[IO.Directory]::Exists($plan.target)){throw 'State_target_changed_during_handoff'}
      [IO.File]::Move($plan.temporary,$plan.target);$plan.temporary=$null
      [void]$committed.Add($plan)
    }
    foreach($plan in $plans){$receipt.files+=@{name=$plan.name;status='copied-with-backup';bytes=$plan.bytes.Length;sha256=$plan.sha256}}
  }catch{
    # Roll back only files this migration created and that still match its
    # digest. A concurrent target edit is preserved for explicit resolution.
    foreach($plan in $committed){if([IO.File]::Exists($plan.target)-and(Get-SidebarSHA256 ([IO.File]::ReadAllBytes($plan.target)))-ceq $plan.sha256){[IO.File]::Delete($plan.target)}}
    throw
  }finally{
    foreach($plan in $plans){foreach($temp in @($plan.backupTemp,$plan.temporary)){if($temp-and[IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}}
  }
  return $receipt
}
if($Initialize){if([string]::IsNullOrWhiteSpace($StateHome)){throw 'StateHome must be specified.'};$root=[IO.Path]::GetFullPath($StateHome);[IO.Directory]::CreateDirectory($root)|Out-Null;[IO.Directory]::CreateDirectory((Join-Path $root 'logs'))|Out-Null;foreach($pair in @(@('config.example.json','config.json'),@('pinky-host-connection.example.json','pinky-host-connection.json'),@('pinky-start.example.json','pinky-start.json'))){$sample=Join-Path $PSScriptRoot $pair[0];$target=Join-Path $root $pair[1];if([IO.File]::Exists($sample)-and -not [IO.File]::Exists($target)){Copy-Item -LiteralPath $sample -Destination $target}}}
