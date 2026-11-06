param([switch]$FunctionsOnly)
$ErrorActionPreference='Stop'
function Copy-SidebarState([string]$From,[string]$To) {
  function AssertPlainPath([string]$value) {
    $cursor=[IO.Path]::GetFullPath($value)
    while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes-band [IO.FileAttributes]::ReparsePoint){throw 'State_reparse_path_denied'}};$parent=[IO.Path]::GetDirectoryName($cursor);if($parent-eq $cursor){break};$cursor=$parent}
  }
  AssertPlainPath $From;AssertPlainPath $To
  [IO.Directory]::CreateDirectory($To)|Out-Null
  $receipt=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');files=@()}
  foreach($name in @('layout.xml','layout.xml.bak','pos.json')){
    $source=Join-Path $From $name;$target=Join-Path $To $name
    AssertPlainPath $source;AssertPlainPath $target
    if([IO.File]::Exists($target)){$receipt.files+=@{name=$name;status='existing-target-preserved'};continue}
    if(-not [IO.File]::Exists($source)){$receipt.files+=@{name=$name;status='source-not-present'};continue}
    if((Get-Item -LiteralPath $source).Length-gt 1048576){throw 'State_file_too_large'}
    $bytes=[IO.File]::ReadAllBytes($source);$again=[IO.File]::ReadAllBytes($source)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$digest=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLowerInvariant();$check=[BitConverter]::ToString($sha.ComputeHash($again)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    if($digest-cne $check){throw 'Source_state_changed_during_read'}
    $backups=Join-Path $To 'backups\from-position-management-fix';AssertPlainPath $backups;[IO.Directory]::CreateDirectory($backups)|Out-Null
    $backup=Join-Path $backups ($digest+'-'+$name)
    if(-not [IO.File]::Exists($backup)){$f=[IO.File]::Open($backup,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}}
    $temporary=$target+'.handoff-'+[guid]::NewGuid().ToString('N')+'.tmp'
    $f=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);try{$f.Write($bytes,0,$bytes.Length);$f.Flush($true)}finally{$f.Dispose()}
    [IO.File]::Move($temporary,$target)
    $receipt.files+=@{name=$name;status='copied-with-backup';bytes=$bytes.Length;sha256=$digest}
  }
  return $receipt
}
function Get-SidebarSHA256([string]$filePath){
  $stream=[IO.File]::OpenRead($filePath);$algorithm=[Security.Cryptography.SHA256]::Create()
  try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
  finally{$algorithm.Dispose();$stream.Dispose()}
}
if(-not $FunctionsOnly){
  # Classified workspace remains inactive until explicit configuration handoff.
  $marker=Join-Path $PSScriptRoot 'state\workspace-ready.json'
  if(-not [IO.File]::Exists($marker)){throw 'Classified workspace staged only; configuration handoff is pending.'}
  $ready=[IO.File]::ReadAllText($marker)|ConvertFrom-Json
  $manifestHash=Get-SidebarSHA256 (Join-Path $PSScriptRoot 'FILE-MANIFEST.json')
  if($ready.schema-ne 'sidebar.workspace-ready.v1' -or $ready.ready-ne $true -or $ready.manifestSha256-cne $manifestHash -or $ready.stateDisposition-notin @('imported','explicit-clean-start','no-prior-layout')){throw 'Workspace activation record does not cover this exact candidate.'}
  # No implicit read/copy from a sibling, old C workspace, credentials or private state.
}
