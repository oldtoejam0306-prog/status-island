$global:SidebarStartupWatch=[Diagnostics.Stopwatch]::StartNew()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw '请双击 Launch.cmd，以 Windows PowerShell STA 启动。' }
# Acquire the same existing preview mutex BEFORE compilation. The UI's same-thread
# acquisition is recursive; it releases its own acquisition on close, and this
# wrapper releases the outer acquisition in finally. No old application is stopped.
$gate = New-Object Threading.Mutex($false, 'Local\StatusIslandColumnPreview')
$owned = $false
try {
  try { $owned = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
  if (-not $owned) {
    [Windows.Forms.MessageBox]::Show('单列预览已在运行。请使用已有窗口；如需切换候选版，请先从旧预览菜单退出。原状态台不用退出。','侧栏候选') | Out-Null
    exit 0
  }
  $required = @('Build.cmd','Candidate.Build.ps1','Candidate.State.ps1','GlassSurface.cs','CornerMotion.cs','PerfLegacySupport.cs','GridSupport.cs','StatusRestore.cs','StatusMaterials.cs','AgentActivity.cs','AgentLifecycleCore.cs','AgentLifecyclePublisher.cs','AgentLifecycleReader.cs','BalanceReadonly.cs','LegacyBalanceOwner.cs','StatusServiceAdapters.cs','StatusServiceTransports.cs','MusicCardControl.cs','LrclibLyrics.cs','WeatherCardControl.cs','AutoloopSidebarControl.cs','OpenMeteoWeatherProvider.cs','WeatherDetailModel.cs','WeatherDetailView.cs','CpuUtilitySource.cs','LocalSystemSource.cs','StatusLocalExtras.cs','NetworkReadonlyView.cs','StatusPresence.cs','RouteDropdown.cs','NormalUpdateHandoff.cs','StatusUiRenderer.cs','DragPreview.cs','CardDragInput.cs','EditCardChrome.cs','RevealMotion.cs','ActionButton.cs','ManagementModel.cs','FramePresentation.cs','StatusNative.1.cs','StatusNative.2.cs','Candidate.Diagnostic.ps1','StatusIsland.Integrated.ps1','NeteaseMedia.cs','MediaPipeline.cs','IntegratedController.cs','LayoutModel.cs','CalendarModel.cs','ClockCalendarControl.cs','MediaContextDiagnostic.cs','ValidationRunner.cs','synthetic-tests\LayoutTests.cs')
  foreach ($name in $required) { if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name) -PathType Leaf)) { throw ('候选包不完整，缺少：' + $name) } }
  & (Join-Path $PSScriptRoot 'Candidate.Build.ps1') | Out-Null
  # The same desktop preview mutex is held: preserve the prior saved configuration
  # only after the old preview has exited, and never overwrite this candidate's state.
  & (Join-Path $PSScriptRoot 'Candidate.State.ps1') | Out-Null
  # The UI reads media asynchronously after it is visible. Full diagnosis remains manual.
  # Do not block first frame waiting for a separate SMTC process.
  $context=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');processId=$PID;hostVersion=$PSVersionTable.PSVersion.ToString();hashImplementation='dotnet-sha256';threadApartment=[Threading.Thread]::CurrentThread.GetApartmentState().ToString()}
  [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'state\logs\startup-context.json'),($context|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
  & (Join-Path $PSScriptRoot 'StatusIsland.Integrated.ps1')
} catch {
  try{$failure=@{schema=1;utc=[DateTime]::UtcNow.ToString('o');processId=$PID;hostVersion=$PSVersionTable.PSVersion.ToString();hashImplementation='dotnet-sha256';error=$_.Exception.Message};$failureDir=Join-Path $PSScriptRoot 'state\logs';[IO.Directory]::CreateDirectory($failureDir)|Out-Null;[IO.File]::WriteAllText((Join-Path $failureDir 'startup-error.json'),($failure|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}catch{}
  $message = $_.Exception.Message.Replace($PSScriptRoot,'<候选目录>')
  Write-Host ('启动失败：' + $message) -ForegroundColor Red
  [Windows.Forms.MessageBox]::Show(('启动失败：' + $message + "`n未修改系统执行策略、未停止旧应用。"),'侧栏候选') | Out-Null
  exit 1
} finally {
  if ($owned) { try { $gate.ReleaseMutex() } catch {} }
  $gate.Dispose()
}
