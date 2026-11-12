$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\Candidate.State.ps1') -FunctionsOnly
function Check([bool]$condition,[string]$message){if(-not$condition){throw $message};Write-Output ('PASS '+$message)}
function Write-Large([string]$path,[long]$size){$f=[IO.File]::Open($path,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.SetLength($size)}finally{$f.Dispose()}}
function Hash([string]$path){(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()}
function New-Source([string]$path,[bool]$withMonitor=$false){[IO.Directory]::CreateDirectory($path)|Out-Null;[IO.File]::WriteAllText((Join-Path $path 'layout.xml'),'<layout source="fixture"/>',[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $path 'layout.xml.bak'),'<layout source="backup-fixture"/>',[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $path 'pos.json'),'{"x":7}',[Text.UTF8Encoding]::new($false));if($withMonitor){$json='{"unread":"'+([string][char]0x4E2D*524288)+'"}';[IO.File]::WriteAllText((Join-Path $path 'aihot-monitor.json'),$json,[Text.UTF8Encoding]::new($false))}}
$root=Join-Path $env:TEMP ('SidebarStateImport-'+[guid]::NewGuid().ToString('N'))
try{
  $initialized=Join-Path $root 'initialized';$candidateState=Join-Path $PSScriptRoot '..\Candidate.State.ps1'
  & $candidateState -Initialize -StateHome $initialized
  $batteryConfig=Join-Path $initialized 'device-battery-connection.json'
  $batteryExample=Get-Content -LiteralPath $batteryConfig -Raw|ConvertFrom-Json
  Check ($batteryExample.schema-eq'pinky.device-battery-connection.v1'-and-not$batteryExample.enabled-and[string]::IsNullOrEmpty($batteryExample.host)-and[string]::IsNullOrEmpty($batteryExample.account)-and[string]::IsNullOrEmpty($batteryExample.keyFile)-and[string]::IsNullOrEmpty($batteryExample.knownHostsFile)) 'first-run battery configuration is disabled with blank SSH connection fields'
  [IO.File]::WriteAllText($batteryConfig,'local-user-config',[Text.UTF8Encoding]::new($false))
  & $candidateState -Initialize -StateHome $initialized
  Check ((Get-Content -LiteralPath $batteryConfig -Raw)-eq'local-user-config') 'reinitialization preserves existing battery configuration'
  $desktopConfig=Join-Path $initialized 'desktop-switch-connection.json'
  $desktopExample=Get-Content -LiteralPath $desktopConfig -Raw|ConvertFrom-Json
  Check ($desktopExample.schema-eq'sidebar.desktop-switch-connection.v1'-and-not$desktopExample.enabled-and[string]::IsNullOrEmpty($desktopExample.executable)) 'first-run desktop switch config is disabled with an empty executable path'
  [IO.File]::WriteAllText($desktopConfig,'local-desktop-config',[Text.UTF8Encoding]::new($false))
  & $candidateState -Initialize -StateHome $initialized
  Check ((Get-Content -LiteralPath $desktopConfig -Raw)-eq'local-desktop-config') 'reinitialization preserves existing desktop switch configuration'
  $source=Join-Path $root 'source';$target=Join-Path $root 'target';New-Source $source $true
  $monitorSource=Join-Path $source 'aihot-monitor.json';$monitorBytes=([IO.FileInfo]::new($monitorSource)).Length
  Check ($monitorBytes-gt1048576-and$monitorBytes-lt33554432) 'AIHOT fixture exceeds 1 MiB and stays below its 32 MiB limit'
  $result=Copy-SidebarState $source $target
  Check ($result.files.Count-eq4-and@($result.files|Where-Object status -eq 'copied-with-backup').Count-eq4) 'normal migration imports layout, backup, position, and AIHOT state'
  foreach($name in @('layout.xml','layout.xml.bak','pos.json','aihot-monitor.json')){
    $entry=$result.files|Where-Object name -eq $name
    $saved=Join-Path (Join-Path $target 'backups\imported-state') ($entry.sha256+'-'+$name)
    Check ((Hash (Join-Path $source $name))-eq(Hash (Join-Path $target $name))-and(Hash (Join-Path $target $name))-eq(Hash $saved)) ("source, imported, and backup hashes match for "+$name)
  }
  [IO.File]::WriteAllText((Join-Path $target 'layout.xml'),'<layout target="preserved"/>',[Text.UTF8Encoding]::new($false))
  $second=Copy-SidebarState $source $target;$preserved=$second.files|Where-Object name -eq 'layout.xml'
  Check ($preserved.status-eq'existing-target-preserved'-and(Get-Content (Join-Path $target 'layout.xml') -Raw)-eq'<layout target="preserved"/>') 'existing layout target remains preserved'
  $missingSource=Join-Path $root 'missing-source';[IO.Directory]::CreateDirectory($missingSource)|Out-Null
  [IO.File]::WriteAllText((Join-Path $missingSource 'layout.xml'),'<layout source="only"/>',[Text.UTF8Encoding]::new($false))
  $missing=Copy-SidebarState $missingSource (Join-Path $root 'missing-target')
  Check (($missing.files|Where-Object name -eq 'layout.xml.bak').status-eq'source-not-present') 'absent state files are reported without blocking migration'

  $corruptRoot=Join-Path $root 'corrupt-backup';$corruptSource=Join-Path $corruptRoot 'source';$corruptTarget=Join-Path $corruptRoot 'target';New-Source $corruptSource $true
  $corruptMonitor=Join-Path $corruptSource 'aihot-monitor.json';$corruptDigest=Hash $corruptMonitor;$backupDirectory=Join-Path $corruptTarget 'backups\imported-state'
  [IO.Directory]::CreateDirectory($backupDirectory)|Out-Null
  $corruptBackup=Join-Path $backupDirectory ($corruptDigest+'-aihot-monitor.json');[IO.File]::WriteAllText($corruptBackup,'tampered',[Text.UTF8Encoding]::new($false))
  $rejected=$false;try{Copy-SidebarState $corruptSource $corruptTarget|Out-Null}catch{$rejected=$_.Exception.Message-eq'State_backup_corrupt'}
  Check ($rejected-and(Get-Content $corruptBackup -Raw)-eq'tampered') 'corrupt AIHOT backup is rejected and preserved'
  Check (-not[IO.File]::Exists((Join-Path $corruptTarget 'layout.xml'))-and-not[IO.File]::Exists((Join-Path $backupDirectory ((Hash (Join-Path $corruptSource 'layout.xml'))+'-layout.xml')))) 'backup validation failure imports none of the earlier files'

  $candidateState=Join-Path $PSScriptRoot '..\Candidate.State.ps1';$candidateText=[IO.File]::ReadAllText($candidateState)
  $functionStart=$candidateText.IndexOf('function Get-SidebarSHA256',[StringComparison]::Ordinal);$functionEnd=$candidateText.IndexOf('if($Initialize)', $functionStart,[StringComparison]::Ordinal)
  if($functionStart-lt0-or$functionEnd-le$functionStart){throw 'Could not isolate state import functions for the failure fixture.'}
  $functionText=$candidateText.Substring($functionStart,$functionEnd-$functionStart)
  $moveLine='[IO.File]::Move($plan.temporary,$plan.target);$plan.temporary=$null'
  if(-not$functionText.Contains($moveLine)){throw 'State import commit line changed; failure fixture needs review.'}
  $injectedMove="if(`$plan.name-eq'layout.xml.bak'){throw 'Fixture_commit_failure'};"+$moveLine
  $functionText=$functionText.Replace($moveLine,$injectedMove)
  . ([scriptblock]::Create($functionText))
  $failureRoot=Join-Path $root 'commit-failure';$failureSource=Join-Path $failureRoot 'source';$failureTarget=Join-Path $failureRoot 'target';New-Source $failureSource $true
  $failureMessage='';try{Copy-SidebarState $failureSource $failureTarget|Out-Null}catch{$failureMessage=$_.Exception.Message}
  Check ($failureMessage-eq'Fixture_commit_failure') 'injected post-commit failure preserves the original exception'
  Check (-not[IO.File]::Exists((Join-Path $failureTarget 'layout.xml'))-and-not[IO.File]::Exists((Join-Path $failureTarget 'layout.xml.bak'))-and-not[IO.File]::Exists((Join-Path $failureTarget 'pos.json'))-and-not[IO.File]::Exists((Join-Path $failureTarget 'aihot-monitor.json'))) 'rollback removes the first committed target after a later commit fails'
  $sourceNames=@('layout.xml','layout.xml.bak','pos.json','aihot-monitor.json');$preservedSourceCount=@($sourceNames|Where-Object{[IO.File]::Exists((Join-Path $failureSource $_))}).Count
  Check ($preservedSourceCount-eq4) 'rollback failure fixture preserves every source file'
  Check (-not(Get-ChildItem $failureTarget -Recurse -File -Filter '*.tmp')) 'rollback failure fixture cleans staged files'

  $readGrowthText=$candidateText.Substring($functionStart,$functionEnd-$functionStart)
  $lengthGuard="if((Get-Item -LiteralPath `$source).Length-gt`$limit){throw 'State_file_too_large'}"
  if(-not$readGrowthText.Contains($lengthGuard)){throw 'Could not locate source length guard for the growth fixture.'}
  $readGrowthText=$readGrowthText.Replace($lengthGuard,$lengthGuard+";if(`$name-eq'aihot-monitor.json'){Write-Large `$source 33554433}")
  . ([scriptblock]::Create($readGrowthText))
  $readGrowthRoot=Join-Path $root 'read-growth';$readGrowthSource=Join-Path $readGrowthRoot 'source';$readGrowthTarget=Join-Path $readGrowthRoot 'target';New-Source $readGrowthSource $true
  $readGrowthRejected=$false;try{Copy-SidebarState $readGrowthSource $readGrowthTarget|Out-Null}catch{$readGrowthRejected=$_.Exception.Message-eq'State_file_too_large'}
  Check ($readGrowthRejected-and-not[IO.File]::Exists((Join-Path $readGrowthTarget 'layout.xml'))-and-not[IO.Directory]::Exists((Join-Path $readGrowthTarget 'backups'))) 'source growth after the initial size check still enforces the AIHOT limit'

  $stageGrowthText=$candidateText.Substring($functionStart,$functionEnd-$functionStart)
  $stageGuard='$stagedBytes=[IO.File]::ReadAllBytes($plan.temporary);$currentBytes=[IO.File]::ReadAllBytes($plan.source)'
  if(-not$stageGrowthText.Contains($stageGuard)){throw 'Could not locate staged-file check for the growth fixture.'}
  $growStage="`$stream=[IO.File]::Open(`$plan.temporary,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None);try{`$stream.SetLength(`$plan.limit+1)}finally{`$stream.Dispose()};"
  $stageGrowthText=$stageGrowthText.Replace($stageGuard,$growStage+$stageGuard)
  . ([scriptblock]::Create($stageGrowthText))
  $stageGrowthRoot=Join-Path $root 'stage-growth';$stageGrowthSource=Join-Path $stageGrowthRoot 'source';$stageGrowthTarget=Join-Path $stageGrowthRoot 'target';New-Source $stageGrowthSource $true
  $stageGrowthRejected=$false;try{Copy-SidebarState $stageGrowthSource $stageGrowthTarget|Out-Null}catch{$stageGrowthRejected=$_.Exception.Message-eq'State_file_too_large'}
  Check ($stageGrowthRejected-and-not[IO.File]::Exists((Join-Path $stageGrowthTarget 'layout.xml'))-and-not(Get-ChildItem $stageGrowthTarget -Recurse -File -Filter '*.tmp')) 'staged-file growth above 32 MiB is rejected before any target commit'

  $layoutLimit=Join-Path $root 'layout-limit';$layoutFrom=Join-Path $layoutLimit 'source';$layoutTo=Join-Path $layoutLimit 'target';[IO.Directory]::CreateDirectory($layoutFrom)|Out-Null
  Write-Large (Join-Path $layoutFrom 'layout.xml') 1048577
  $layoutRejected=$false;try{Copy-SidebarState $layoutFrom $layoutTo|Out-Null}catch{$layoutRejected=$_.Exception.Message-eq'State_file_too_large'}
  Check ($layoutRejected-and[IO.File]::Exists((Join-Path $layoutFrom 'layout.xml'))-and-not[IO.File]::Exists((Join-Path $layoutTo 'layout.xml'))) 'other state files remain capped at 1 MiB'

  $largeRoot=Join-Path $root 'aihot-limit';$largeFrom=Join-Path $largeRoot 'source';$largeTo=Join-Path $largeRoot 'target';[IO.Directory]::CreateDirectory($largeFrom)|Out-Null
  [IO.File]::WriteAllText((Join-Path $largeFrom 'layout.xml'),'<layout source="first"/>',[Text.UTF8Encoding]::new($false))
  Write-Large (Join-Path $largeFrom 'aihot-monitor.json') 33554433
  $largeRejected=$false;try{Copy-SidebarState $largeFrom $largeTo|Out-Null}catch{$largeRejected=$_.Exception.Message-eq'State_file_too_large'}
  Check ($largeRejected-and[IO.File]::Exists((Join-Path $largeFrom 'aihot-monitor.json'))-and-not[IO.File]::Exists((Join-Path $largeTo 'layout.xml'))-and-not[IO.Directory]::Exists((Join-Path $largeTo 'backups'))) 'AIHOT state above 32 MiB is rejected before any import or backup'

  $blockedRoot=Join-Path $root 'blocked-target';$blockedFrom=Join-Path $blockedRoot 'source';$blockedTo=Join-Path $blockedRoot 'target';New-Source $blockedFrom
  [IO.Directory]::CreateDirectory((Join-Path $blockedTo 'pos.json'))|Out-Null
  $blocked=$false;try{Copy-SidebarState $blockedFrom $blockedTo|Out-Null}catch{$blocked=$_.Exception.Message-eq'State_target_invalid'}
  Check ($blocked-and-not[IO.File]::Exists((Join-Path $blockedTo 'layout.xml'))-and-not[IO.Directory]::Exists((Join-Path $blockedTo 'backups'))) 'invalid later destination fails before earlier state is committed'
  Check (-not(Get-ChildItem $root -Recurse -File -Filter '*.tmp')) 'migration leaves no temporary files after success or failure'
  'StateImportChecks: passed (four-file migration, per-file limits, preserved targets, atomic backups, and failure consistency).'
}finally{if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
