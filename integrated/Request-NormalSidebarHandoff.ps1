[CmdletBinding(SupportsShouldProcess=$true)]
param(
 [Parameter(Mandatory=$true)][string]$CurrentCandidate,
 [Parameter(Mandatory=$true)][string]$CurrentManifestSha256,
 [Parameter(Mandatory=$true)][string]$NextCandidate,
 [Parameter(Mandatory=$true)][string]$NextManifestSha256,
 [switch]$StartupAuthorizationReady
)
$ErrorActionPreference='Stop'
# A caller must resolve the exact next-start authorization BEFORE asking the
# old app to close. This helper neither grants it nor changes execution policy.
if(-not $StartupAuthorizationReady){throw 'Next startup authorization is not ready; old preview retained'}
foreach($id in @($CurrentCandidate,$NextCandidate)){if($id-notmatch '^[a-z0-9][a-z0-9-]{0,79}$'){throw 'Invalid project candidate identity'}}
foreach($hash in @($CurrentManifestSha256,$NextManifestSha256)){if($hash-notmatch '^[0-9a-f]{64}$'){throw 'Invalid manifest pin'}}
$workspace=$env:SIDEBAR_WORKSPACE;if ([string]::IsNullOrWhiteSpace($workspace)) { throw 'Set SIDEBAR_WORKSPACE to the local workspace before requesting a handoff.' };$releases=Join-Path $workspace 'releases';$current=Join-Path $releases $CurrentCandidate;$next=Join-Path $releases $NextCandidate
function Hash([string]$file){$f=[IO.File]::OpenRead($file);$sha=[Security.Cryptography.SHA256]::Create();try{[BitConverter]::ToString($sha.ComputeHash($f)).Replace('-','').ToLowerInvariant()}finally{$f.Dispose();$sha.Dispose()}}
function Safe([string]$file){$full=[IO.Path]::GetFullPath($file);if([IO.File]::Exists($full)-and ([IO.File]::GetAttributes($full)-band [IO.FileAttributes]::ReparsePoint)){throw 'Redirected file rejected'};$d=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($full));while($d){if($d.Exists-and ($d.Attributes-band [IO.FileAttributes]::ReparsePoint)){throw 'Redirected directory rejected'};$d=$d.Parent}}
foreach($pair in @(@($current,$CurrentManifestSha256),@($next,$NextManifestSha256))){$file=Join-Path $pair[0] 'FILE-MANIFEST.json';Safe $file;if((Hash $file)-cne $pair[1]){throw 'Candidate pin changed; old preview retained'}}
$ownerPath=Join-Path $current 'state\update\owner.json';if(-not [IO.File]::Exists($ownerPath)){throw 'Running version has no normal update protocol. Bootstrap requires its existing normal exit; no alternate close attempted'}
Safe $ownerPath;if(([IO.FileInfo]::new($ownerPath)).Length-gt 4096){throw 'Invalid update owner record'};$owner=[IO.File]::ReadAllText($ownerPath)|ConvertFrom-Json
if($owner.protocol-cne 'sidebar.normal-update.v1'-or $owner.candidate-cne $CurrentCandidate-or $owner.manifestSha256-cne $CurrentManifestSha256){throw 'Running update owner identity mismatch'}
$process=Get-Process -Id ([int]$owner.processId) -ErrorAction Stop
if($process.StartTime.ToUniversalTime().ToString('o')-cne [DateTime]::Parse($owner.processBirthUtc).ToUniversalTime().ToString('o')){throw 'Running process birth mismatch'}
if(-not $PSCmdlet.ShouldProcess($CurrentCandidate,'Save layout with backup, then normal application close for this verified update')){return}
$nonce=[guid]::NewGuid().ToString('N');$requestPath=Join-Path $current 'state\update\request.json';$replyPath=Join-Path $current ('state\update\receipts\'+$nonce+'.json')
function Submit([string]$operation){Safe $requestPath;$data=@{schema=1;operation=$operation;nonce=$nonce;utc=[DateTime]::UtcNow.ToString('o');currentManifestSha256=$CurrentManifestSha256;nextCandidate=$NextCandidate;nextManifestSha256=$NextManifestSha256};$tmp=$requestPath+'.'+$nonce+'.tmp';[IO.File]::WriteAllText($tmp,($data|ConvertTo-Json),[Text.UTF8Encoding]::new($false));if([IO.File]::Exists($requestPath)){[IO.File]::Replace($tmp,$requestPath,$null)}else{[IO.File]::Move($tmp,$requestPath)}}
function WaitReply([string]$expected){$watch=[Diagnostics.Stopwatch]::StartNew();while($watch.Elapsed.TotalSeconds-lt 20){if([IO.File]::Exists($replyPath)){Safe $replyPath;$reply=[IO.File]::ReadAllText($replyPath)|ConvertFrom-Json;if($reply.nonce-cne $nonce-or $reply.manifestSha256-cne $CurrentManifestSha256-or [int]$reply.processId-ne $process.Id){throw 'Wrong normal-handoff receipt'};if($reply.status-eq $expected){return $reply};if($reply.status-in @('busy','rejected','failed','conflict')){throw ('Normal update deferred: '+$reply.status+'; '+$reply.reason)}};Start-Sleep -Milliseconds 100};throw 'No matching normal-handoff receipt; outcome must be inspected before any retry'}
Submit 'prepare-update';$prepared=WaitReply 'prepared';if($process.HasExited){throw 'Preview exited before commit; inspect, do not retry'}
Submit 'commit-update';$closing=WaitReply 'closing';if(-not $process.WaitForExit(15000)){throw 'Normal close was requested but process remains; do not terminate it'}
[pscustomobject]@{nonce=$nonce;prepared=$prepared;closing=$closing;oldProcessExited=$true;newProcessStarted=$false;catalogChanged=$false;nextCandidate=$NextCandidate;nextManifestSha256=$NextManifestSha256;nextStep='Guarded state handoff, authorized exact startup and fresh PID/Shown verification before selecting canonical entry'}|ConvertTo-Json -Depth 7
