$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\Candidate.State.ps1') -FunctionsOnly
$root=Join-Path $env:TEMP ('SidebarStateImport-'+[guid]::NewGuid().ToString('N'))
$source=Join-Path $root 'source';$target=Join-Path $root 'target'
try{
  [IO.Directory]::CreateDirectory($source)|Out-Null
  [IO.File]::WriteAllText((Join-Path $source 'layout.xml'),'<layout source="fixture"/>',[Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $source 'pos.json'),'{"x":7}',[Text.UTF8Encoding]::new($false))
  $result=Copy-SidebarState $source $target
  if((Get-Content (Join-Path $target 'layout.xml') -Raw) -ne '<layout source="fixture"/>'){throw 'State copy bytes mismatch.'}
  $entry=$result.files|Where-Object name -eq 'layout.xml';if($entry.status -ne 'copied-with-backup'){throw 'State copy receipt did not report a backed up write.'}
  $backup=Join-Path (Join-Path $target 'backups\imported-state') ($entry.sha256+'-layout.xml')
  if(-not [IO.File]::Exists($backup)){throw 'State backup was not written.'}
  [IO.File]::WriteAllText((Join-Path $target 'layout.xml'),'<layout target="preserved"/>',[Text.UTF8Encoding]::new($false))
  $second=Copy-SidebarState $source $target;$preserved=$second.files|Where-Object name -eq 'layout.xml'
  if($preserved.status -ne 'existing-target-preserved' -or (Get-Content (Join-Path $target 'layout.xml') -Raw) -ne '<layout target="preserved"/>'){throw 'Existing state target was not preserved.'}
  $missing=$second.files|Where-Object name -eq 'layout.xml.bak';if($missing.status -ne 'source-not-present'){throw 'Missing state source was not reported.'}
  $corruptTarget=Join-Path $root 'corrupt-target';$corruptBackupDir=Join-Path $corruptTarget 'backups\imported-state';[IO.Directory]::CreateDirectory($corruptBackupDir)|Out-Null
  $corruptBackup=Join-Path $corruptBackupDir ($entry.sha256+'-layout.xml');[IO.File]::WriteAllText($corruptBackup,'tampered',[Text.UTF8Encoding]::new($false))
  $rejected=$false;try{Copy-SidebarState $source $corruptTarget|Out-Null}catch{$rejected=$_.Exception.Message -match 'State_backup_corrupt'}
  if(-not $rejected -or (Get-Content $corruptBackup -Raw) -ne 'tampered' -or [IO.File]::Exists((Join-Path $corruptTarget 'layout.xml'))){throw 'Corrupt existing backup was not rejected and preserved.'}
  if(Get-ChildItem $target -Recurse -File -Filter '*.tmp'){throw 'Temporary state file was left behind.'}
  'StateImportChecks: passed (backup, explicit copy, existing-target preservation, missing source).'
}finally{if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
