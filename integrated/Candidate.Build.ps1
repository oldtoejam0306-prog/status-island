param([switch]$Force)
$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$sources=@('Build.cmd','Candidate.Build.ps1','NeteaseMedia.cs','MediaPipeline.cs','MediaContextDiagnostic.cs','CcSwitchLauncher.cs','ThemePicker.cs','AihotSidebar.cs','AihotInboxCard.cs','AihotSurface.cs','IntegratedController.cs','PinkyNativeHost.cs','PinkyHostConnection.cs','PinkySidebarSession.cs','LayoutModel.cs','CalendarModel.cs','ClockCalendarControl.cs','GlassSurface.cs','GlassColumnForm.cs','CornerMotion.cs','PerfLegacySupport.cs','GridSupport.cs','StatusRestore.cs','StatusMaterials.cs','AgentActivity.cs','AgentLifecycleCore.cs','AgentLifecyclePublisher.cs','AgentLifecycleReader.cs','BalanceReadonly.cs','LegacyBalanceOwner.cs','StatusServiceAdapters.cs','StatusServiceTransports.cs','MusicCardControl.cs','LrclibLyrics.cs','WeatherCardControl.cs','AutoloopSidebarControl.cs','AutoloopBridge.cs','AutoloopNotification.cs','AutoloopDetailView.cs','AutoloopProjectSnapshots.cs','OpenMeteoWeatherProvider.cs','WeatherDetailModel.cs','WeatherCityHistory.cs','WeatherDetailView.cs','CpuUtilitySource.cs','LocalSystemSource.cs','StatusLocalExtras.cs','NetworkReadonlyView.cs','StatusPresence.cs','RouteDropdown.cs','NormalUpdateHandoff.cs','StatusUiRenderer.cs','DragPreview.cs','CardDragInput.cs','EditCardChrome.cs','RevealMotion.cs','ActionButton.cs','ManagementModel.cs','FramePresentation.cs','StatusNative.1.cs','StatusNative.2.cs','ValidationRunner.cs','synthetic-tests\LayoutTests.cs')
$sources += @('DeviceBatterySource.cs','synthetic-tests\DeviceBatteryTests.cs','device-battery-connection.example.json')
$sources += @('Candidate.Start.ps1','Candidate.State.ps1','Candidate.Diagnostic.ps1','Launch.cmd','Launch.vbs','PinkyRuntimeProbe.ps1','Start-PinkyFusion.ps1','config.example.json','pinky-host-connection.example.json','pinky-start.example.json','StatusIsland.Integrated.ps1','README.md','synthetic-tests\StateImportChecks.ps1','synthetic-tests\PinkyRuntimeProbeChecks.ps1')
$sources += @('DesktopSwitchLauncher.cs','desktop-switch-connection.example.json')
$outputs=@('NeteaseMedia.dll','MediaContextDiagnostic.exe','Controller.Check.dll','StatusNative.dll','ValidationRunner.exe','LayoutTests.exe')
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not [IO.File]::Exists($compiler)){$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not [IO.File]::Exists($compiler)){throw 'Installed Framework compiler not found'}
function Get-SidebarSHA256([string]$filePath){
  $stream=[IO.File]::OpenRead($filePath);$algorithm=[Security.Cryptography.SHA256]::Create()
  try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
  finally{$algorithm.Dispose();$stream.Dispose()}
}
function Hash([string]$path){Get-SidebarSHA256 $path}
$fingerprintLines=@()
foreach($name in $sources){$fingerprintLines+=($name+':'+(Hash (Join-Path $PSScriptRoot $name)))}
$fingerprintLines+=('compiler:'+(Hash $compiler))
$framework=[IO.Path]::GetDirectoryName($compiler)
$runtime=Join-Path $framework 'Facades\System.Runtime.dll'
if(-not [IO.File]::Exists($runtime)){$runtime=Join-Path $framework 'System.Runtime.dll'}
$fingerprintLines+=('runtime:'+(Hash $runtime))
foreach($name in @('Windows.Media.winmd','Windows.Foundation.winmd','Windows.Storage.winmd')){$fingerprintLines+=($name+':'+(Hash (Join-Path $env:SystemRoot ('System32\WinMetadata\'+$name))))}
$sha=[Security.Cryptography.SHA256]::Create()
try{$fingerprint=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($fingerprintLines -join "`n")))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
$build=Join-Path $PSScriptRoot 'state\build'
$cache=Join-Path $build 'build-cache.json'
$valid=$false
if(-not $Force -and [IO.File]::Exists($cache)){
  try{
    $prior=[IO.File]::ReadAllText($cache)|ConvertFrom-Json
    $valid=$prior.schema-eq 1 -and $prior.fingerprint-ceq $fingerprint
    foreach($name in $outputs){$entry=@($prior.outputs|Where-Object {$_.name-ceq $name});if($entry.Count-ne 1 -or -not [IO.File]::Exists((Join-Path $build $name)) -or (Hash (Join-Path $build $name))-cne $entry[0].sha256){$valid=$false}}
  }catch{$valid=$false}
}
if(-not $valid){
  & (Join-Path $PSScriptRoot 'Build.cmd') | Out-Host
  if($LASTEXITCODE-ne 0){throw 'Framework build failed; cache not committed'}
  $hashes=@();foreach($name in $outputs){$hashes+=@{name=$name;sha256=(Hash (Join-Path $build $name))}}
  $record=@{schema=1;fingerprint=$fingerprint;outputs=$hashes}
  $temporary=$cache+'.tmp';[IO.File]::WriteAllText($temporary,($record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  if([IO.File]::Exists($cache)){[IO.File]::Replace($temporary,$cache,($cache+'.previous'))}else{[IO.File]::Move($temporary,$cache)}
}
$result=@{schema=1;cacheHit=[bool]$valid;elapsedMs=$watch.ElapsedMilliseconds;fingerprint=$fingerprint;compilerExecuted=(-not $valid);outputs=$outputs.Count;utc=[DateTime]::UtcNow.ToString('o')}
$logs=Join-Path $PSScriptRoot 'state\logs';[IO.Directory]::CreateDirectory($logs)|Out-Null
[IO.File]::WriteAllText((Join-Path $logs 'build-latest.json'),($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$result
