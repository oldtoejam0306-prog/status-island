# Non-sensitive in-process local metrics. No legacy worker, account access or network controls.
$ErrorActionPreference = 'Stop'
$Root = $env:STATUS_ISLAND_HOME
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path $env:LOCALAPPDATA 'StatusIsland' }
$Root = [IO.Path]::GetFullPath($Root)
$env:STATUS_ISLAND_HOME = $Root
$BuildRoot = Join-Path $PSScriptRoot 'state\build'
$script:releaseCandidate = Split-Path -Leaf $PSScriptRoot
New-Item -ItemType Directory -Force -Path $Root | Out-Null
$Cfg = @{ codexProcess='ChatGPT'; claudeProcess='claude'; codexLaunch=''; claudeLaunch=''; routePrimary=''; routeBackup1=''; routeBackup2='' }
function Write-Log([string]$m) { }
$mutex = New-Object System.Threading.Mutex($false, 'Local\StatusIslandColumnPreview')
# An abandoned mutex is acquired by this process and can be reused after a crash.
try { $mutexAcquired = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $mutexAcquired = $true }
if (-not $mutexAcquired) {
  $mutex.Dispose()
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.MessageBox]::Show('单列预览已在运行。请先使用已有预览，或从其菜单退出后再启动。','小组件预览') | Out-Null
  return
}

$controller=$null;$form=$null;$timer=$null;$anim=$null;$menu=$null;$script:renderGate=$null
try {
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)

# 分层窗口：逐像素透明，玻璃半透明和柔和外阴影都靠它
Add-Type -Path (Join-Path $BuildRoot 'StatusNative.dll') -ReferencedAssemblies System.Drawing

# 激活/前置其他应用窗口（后台进程直接 SetForegroundWindow 会被系统限制，用 AttachThreadInput 绕过）
# Native helper types loaded from the fingerprint-checked build.

# 液态玻璃调色板
$C_Title  = [System.Drawing.Color]::FromArgb(246, 248, 252)
$C_Text   = [System.Drawing.Color]::FromArgb(244, 246, 249)
$C_Label  = [System.Drawing.Color]::FromArgb(238, 240, 243)
$C_Faint  = [System.Drawing.Color]::FromArgb(226, 229, 233)
$C_Ok     = [System.Drawing.Color]::FromArgb(110, 235, 160)
$C_Warn   = [System.Drawing.Color]::FromArgb(255, 205, 90)
$C_Bad    = [System.Drawing.Color]::FromArgb(255, 130, 130)
$C_Codex  = [System.Drawing.Color]::FromArgb(64, 222, 158)
$C_Claude = [System.Drawing.Color]::FromArgb(242, 152, 120)
$C_Music  = [System.Drawing.Color]::FromArgb(255, 126, 136)
$C_Line   = [System.Drawing.Color]::FromArgb(36, 255, 255, 255)

# 点击 Codex / Claude 卡片：优先聚焦已有窗口，没运行则启动
function Find-AgentLaunch([string]$which) {
  if ($which -eq 'codex') {
    if ([string]$Cfg.codexLaunch) { return [string]$Cfg.codexLaunch }
    $p = Get-Process $Cfg.codexProcess -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -First 1
    if ($p) { return [string]$p.Path }
    $pkg = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pkg -and $pkg.InstallLocation) {
      $exe = Join-Path $pkg.InstallLocation 'app\ChatGPT.exe'
      if (Test-Path -LiteralPath $exe) { return $exe }
    }
    return ''
  }
  if ([string]$Cfg.claudeLaunch) { return [string]$Cfg.claudeLaunch }
  $lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Claude.lnk'
  if (Test-Path -LiteralPath $lnk) { return $lnk }
  $dirs = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'AnthropicClaude') -Directory -Filter 'app-*' -ErrorAction SilentlyContinue | Sort-Object Name -Descending
  foreach ($d in $dirs) { $exe = Join-Path $d.FullName 'claude.exe'; if (Test-Path -LiteralPath $exe) { return $exe } }
  return ''
}

function Open-AgentApp([string]$which) {
  $procName = if ($which -eq 'codex') { [string]$Cfg.codexProcess } else { [string]$Cfg.claudeProcess }
  $hit = Get-Process $procName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if ($hit) {
    $ok = [AgentWin]::Activate($hit.MainWindowHandle)
    Write-Log ('agent: focus ' + $which + ' pid=' + $hit.Id + ' ok=' + $ok)
    return
  }
  $launch = Find-AgentLaunch $which
  if (-not $launch) { Write-Log ('agent: no launch target for ' + $which); return }
  try {
    Start-Process -FilePath $launch
    Write-Log ('agent: launch ' + $which + ' -> ' + $launch)
  } catch { Write-Log ('agent: launch failed ' + $which + ': ' + $_.Exception.Message) }
}

$M     = 16                 # 阴影边距（窗口比面板大一圈）
$W_E   = 360; $H_E = 424    # 展开尺寸
$W_C   = 360; $H_C = 80     # 胶囊尺寸（两行）
$PadX  = 24
$RowHiY = 132; $RowHiH = 26 # 出口线路行高亮/命中区（面板坐标，第 4 行）
$RowHitTop = 310; $RowHitBot = 328

$F_Title = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]16, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$F_Name  = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]16, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$F_Sub   = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$F_Row   = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]14, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$F_Val   = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]14, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$F_Small = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$F_Pill  = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]12, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$F_Page  = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]14, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$F_Tiny  = New-Object System.Drawing.Font('Microsoft YaHei UI', [single]12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

Add-Type -Path (Join-Path $BuildRoot 'NeteaseMedia.dll')
Add-Type -Path (Join-Path $BuildRoot 'Controller.Check.dll')
[void][GlassWin]::SetProcessDPIAware()
$script:ccLauncher=[IslandIntegrated.CcSwitchLauncher]::CreateDefault('')
$script:ccConfigured=''
try {$ccConfigFile=Join-Path $Root 'config.json';if([IO.File]::Exists($ccConfigFile) -and (Get-Item -LiteralPath $ccConfigFile).Length -le 65536){$ccConfig=[IO.File]::ReadAllText($ccConfigFile)|ConvertFrom-Json;if($ccConfig.ccSwitchLaunch -is [string]){$script:ccConfigured=[string]$ccConfig.ccSwitchLaunch}}}catch{}
function Invoke-CcSwitch {
  if(-not [IslandIntegrated.StatusUiRenderer]::CcSwitchAvailable){$script:statusToolTip.Show($script:ccStatusText,$form,2500);return}
  try{$message='';$result=$script:ccLauncher.Click($script:ccConfigured,[ref]$message);if($result -eq [IslandIntegrated.CcSwitchClickResult]::Failed){$script:statusToolTip.Show('CC Switch 无法聚焦或启动，请检查程序窗口与配置',$form,3500)}else{$script:statusToolTip.Show('CC Switch 正在打开或已聚焦',$form,1600)}}catch{$script:statusToolTip.Show('CC Switch 启动失败，请检查配置',$form,3500)}
}
$form = New-Object IslandIntegrated.NoActivateForm
$form.PerPixelLayered = $true
$script:surfaceReplayRequested=$false
$form.add_LayeredSurfaceChanged({$script:surfaceReplayRequested=$true})
$script:firstFrameGate = New-Object IslandIntegrated.FirstFrameGate
$script:renderGate = New-Object IslandIntegrated.RenderGate
$script:statusFrameCache=New-Object IslandIntegrated.StatusFrameCache $M
$script:lastHighlightSequence=-1
$script:geometryContextDirty=$false
$form.Add_GeometryContextChanged({$script:geometryContextDirty=$true;$script:firstFrameGate.InvalidatePixels()})
$form.Text = '状态台 · 集成离线预览'
$form.FormBorderStyle = 'None'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.BackColor = [System.Drawing.Color]::FromArgb(10, 11, 14)
$form.AllowTransparency = $false

$wsArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
# The legacy saved pos.json is retained for rollback, but fixed-corner policy ignores it.
$PosFile = Join-Path $Root 'pos.json'
$script:anchorRight=$wsArea.Right
$script:top=$wsArea.Top+$M
$script:size = @{ w = $W_C; h = $H_C }
$script:heightMotion=New-Object IslandIntegrated.StatusHeightMotion $H_C
$script:mode = 'compact'
$script:dock=New-Object IslandIntegrated.StatusDockState
$script:dockLight=New-Object IslandIntegrated.DockHoverMotion;$script:dockHover=[single]0;$script:statusToolTip=New-Object Windows.Forms.ToolTip;$script:statusToolTip.InitialDelay=650;$script:statusToolTip.AutoPopDelay=4000;$script:lastHint=""
$script:statusDirty=$false
$script:statusHover=New-Object IslandIntegrated.StatusHoverState
$script:statusFrameCount=0
$script:dragging = $false
$script:dragScreen = New-Object System.Drawing.Point(0, 0)
$script:pulse = 0.0
$script:bgCache = @{ key = ''; bmp = $null }
$script:claudeBusySeen = $null

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r2) {
  $p2 = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r2 * 2
  if ($d -gt $w) { $d = $w }
  if ($d -gt $h) { $d = $h }
  if ($d -le 0) { return $p2 }
  $p2.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
  $p2.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
  $p2.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
  $p2.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
  $p2.CloseFigure()
  return $p2
}

function Set-Geometry {
  $area=if($controller){$controller.TargetArea}else{[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea}
  $body=if($script:dock.HandleMode([int]$script:size.h)){[IslandIntegrated.StatusDockState]::HandleBody($area,$form.DeviceDpi)}else{[IslandIntegrated.CornerLayout]::Body($area,[int]$script:size.h,$M,[System.Windows.Forms.SystemInformation]::VerticalScrollBarWidth)}
  if(-not $script:dock.HandleMode([int]$script:size.h)){$script:size.w=$body.Width;$script:anchorRight=$body.Right;$script:top=$body.Top}
  $previousBounds=$script:pendingBounds
  $script:pendingBounds=[Drawing.Rectangle]::new($body.Left-$M,$body.Top-$M,$body.Width+2*$M,$body.Height+2*$M)
  if(-not $form.IsHandleCreated){$form.Bounds=$script:pendingBounds}
  return ($null-eq $previousBounds -or $previousBounds-ne $script:pendingBounds)
}

function Get-BarColor([double]$pct) {
  if ($pct -ge 88) { return $C_Bad }
  if ($pct -ge 65) { return $C_Warn }
  return $C_Ok
}

# 服务卡片（Codex / Claude）位置：绘制与命中检测共用
$TileH = 80
function Get-TileRect([int]$i){return [IslandIntegrated.StatusUiLayout]::Service($i,[int]$script:size.w)}

function Get-TileHit([int]$cx, [int]$cy) {
  for ($i = 0; $i -lt 2; $i++) {
    $r = Get-TileRect $i
    if ($r.Contains([Drawing.Point]::new($cx,$cy))) { return $i }
  }
  return -1
}

function Draw-Glow($g, [double]$x, [double]$y, [double]$w, [double]$h, [System.Drawing.Color]$c) {
  if ($w -le 1 -or $h -le 1 -or $c.A -le 0) { return }
  $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
  $gp.AddEllipse([single]$x, [single]$y, [single]$w, [single]$h)
  $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush($gp)
  $pg.CenterColor = $c
  $pg.SurroundColors = ([System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, $c.R, $c.G, $c.B)))
  $g.FillPath($pg, $gp)
  $pg.Dispose(); $gp.Dispose()
}

# Codex（OpenAI）花结标志：优先用已安装 Codex 应用自带的 256px 资源，按品牌色着色
$script:codexLogo = $null
# Preview uses the existing vector fallback; no process discovery.
function Draw-IconCodex($g, [double]$x, [double]$y, [double]$s, [System.Drawing.Color]$c, [double]$scale = 1.0) {
  $box = New-RoundedPath $x $y $s $s ($s * 0.30)
  $ba = [int](46 * $scale)
  $bb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($ba, $c.R, $c.G, $c.B))
  $g.FillPath($bb, $box)
  $bp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb([int](150 * $scale), $c.R, $c.G, $c.B), [single]([math]::Max(1.0, $s * 0.055)))
  $g.DrawPath($bp, $box); $bp.Dispose(); $bb.Dispose(); $box.Dispose()

  if ($script:codexLogo) {
    $pad = $s * 0.17
    $dest = New-Object System.Drawing.Rectangle([int]($x + $pad), [int]($y + $pad), [int]($s - 2 * $pad), [int]($s - 2 * $pad))
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $cm = New-Object System.Drawing.Imaging.ColorMatrix
    $cm.Matrix00 = [single]($c.R / 255.0)
    $cm.Matrix11 = [single]($c.G / 255.0)
    $cm.Matrix22 = [single]($c.B / 255.0)
    $cm.Matrix33 = [single]$scale
    $cm.Matrix44 = 1.0
    $attr.SetColorMatrix($cm)
    $g.DrawImage($script:codexLogo, $dest, 0, 0, $script:codexLogo.Width, $script:codexLogo.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $attr.Dispose()
    return
  }

  $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb([int](235 * $scale), $c.R, $c.G, $c.B), [single]([math]::Max(1.0, $s * 0.06)))
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $cx = $x + $s / 2; $cy = $y + $s / 2
  for ($a = 0; $a -lt 360; $a += 60) {
    $rad = $a * [math]::PI / 180
    $st2 = $g.Save()
    $g.TranslateTransform([single]($cx + [math]::Cos($rad) * $s * 0.12), [single]($cy + [math]::Sin($rad) * $s * 0.12))
    $g.RotateTransform($a)
    $petal = New-RoundedPath (-$s * 0.30) (-$s * 0.12) ($s * 0.60) ($s * 0.24) ($s * 0.12)
    $g.DrawPath($pen, $petal); $petal.Dispose()
    $g.Restore($st2)
  }
  $pen.Dispose()
}

# Claude：十二道放射短线（太阳/星芒）
function Draw-IconClaude($g, [double]$x, [double]$y, [double]$s, [System.Drawing.Color]$c, [double]$scale = 1.0) {
  $box = New-RoundedPath $x $y $s $s ($s * 0.30)
  $ba = [int](46 * $scale)
  $bb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($ba, $c.R, $c.G, $c.B))
  $g.FillPath($bb, $box)
  $bp = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb([int](150 * $scale), $c.R, $c.G, $c.B), [single]([math]::Max(1.0, $s * 0.055)))
  $g.DrawPath($bp, $box); $bp.Dispose(); $bb.Dispose(); $box.Dispose()

  $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb([int](235 * $scale), $c.R, $c.G, $c.B), [single]([math]::Max(1.0, $s * 0.052)))
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $cx = $x + $s / 2; $cy = $y + $s / 2
  for ($a = 0; $a -lt 360; $a += 30) {
    $rad = $a * [math]::PI / 180
    $long = (($a % 60) -eq 0)
    $r1 = $s * ($(if ($long) { 0.16 } else { 0.12 }))
    $r2 = $s * ($(if ($long) { 0.35 } else { 0.29 }))
    $g.DrawLine($pen,
      [single]($cx + [math]::Cos($rad) * $r1), [single]($cy + [math]::Sin($rad) * $r1),
      [single]($cx + [math]::Cos($rad) * $r2), [single]($cy + [math]::Sin($rad) * $r2))
  }
  $pen.Dispose()
}

$script:agentIconCache = @{}
function Get-AgentIconRaster([int]$index, [int]$size, [Drawing.Color]$color, [double]$scale) {
  $key = '{0}|{1}|{2}|{3}' -f $index,$size,$color.ToArgb(),$scale
  if ($script:agentIconCache.ContainsKey($key)) { return ,$script:agentIconCache[$key] }
  if ($script:agentIconCache.Count -ge 32) {
    foreach ($image in $script:agentIconCache.Values) { $image.Dispose() }
    $script:agentIconCache.Clear()
  }
  $image = [Drawing.Bitmap]::new($size+2,$size+2)
  $graphics = [Drawing.Graphics]::FromImage($image)
  try {
    $graphics.Clear([Drawing.Color]::Transparent)
    if ($index -eq 0) { Draw-IconCodex $graphics 1 1 $size $color $scale }
    else { Draw-IconClaude $graphics 1 1 $size $color $scale }
  } catch { $image.Dispose(); throw } finally { $graphics.Dispose() }
  $script:agentIconCache[$key] = $image
  return ,$image
}

$script:view = @{
  pill = '采集中'; pillC = $C_Faint; sub = '正在读取状态…'
  rows = @(
    @{ l = '网卡链路'; v = '—'; c = $C_Faint },
    @{ l = '远端设备'; v = '—'; c = $C_Faint },
    @{ l = '账号与余额'; v = '—'; c = $C_Faint },
    @{ l = '代理与出口'; v = '—'; c = $C_Faint }
  )
  metrics = @(
    @{ l = 'CPU'; v = '—'; pct = 0; c = $C_Faint },
    @{ l = '内存'; v = '—'; pct = 0; c = $C_Faint },
    @{ l = '磁盘 D'; v = '—'; pct = 0; c = $C_Faint },
    @{ l = 'GPU'; v = '—'; pct = 0; c = $C_Faint }
  )
  services = @(
    @{ key = 'codex'; name = 'Codex'; up = $false; busy = $false; last = $null; c = $C_Codex },
    @{ key = 'claude'; name = 'Claude'; up = $false; busy = $false; last = $null; c = $C_Claude }
  )
}

# 上下翻页
$script:ticker = @{ all = @('正在读取状态…'); idx = 0; sys = @('正在读取状态…'); sysIdx = 0; hold = 0; flipping = $false; progress = 0.0 }

# 出口线路下拉：画在面板内部，不另开窗口，跟随整窗的悬停/收起逻辑
$script:picker = @{ open = $false; hover = -1; items = @() }
$script:switching = $false
$script:routeOverride = ''
$script:routeOverrideUntil = $null
$script:flashOkUntil = $null
$script:flashBadUntil = $null
$script:tileHover = -1

# 音效包切换（peon-ping）：底部工具行分别绑定 Codex 与 Claude
$script:peonCfg = ''
$script:peonPacksDir = ''
$script:peonPacks = @()
$script:peonFallbackPacks = @{ codex = 'ae_qianyu'; claude = 'starrail-kafka-peon-pack' }
$script:peonBindings = @{
  codex  = @{ pack = 'ae_qianyu'; label = '陈千语'; index = -1; flashUntil = $null; flashBadUntil = $null }
  claude = @{ pack = 'starrail-kafka-peon-pack'; label = '卡芙卡'; index = -1; flashUntil = $null; flashBadUntil = $null }
}
$script:peonPreviewProcess = $null
$script:peonHover = ''
$script:peonCheckedAt = [datetime]::MinValue

function Get-PeonLabelFor([string]$name) {
  switch ($name) {
    'starrail-kafka-peon-pack' { return '卡芙卡' }
    'ae_qianyu' { return '陈千语' }
    default {
      if ($name.Length -gt 10) { return $name.Substring(0, 10) }
      return $name
    }
  }
}

function Read-PeonState {
 $data=$controller.Legacy;$changed=$false
 foreach($ide in @('codex','claude')){$pack=if($ide-eq 'codex'){$data.CodexVoice}else{$data.ClaudeVoice};if($script:peonBindings[$ide].pack-cne $pack){$script:peonBindings[$ide].pack=$pack;$script:peonBindings[$ide].label=[IslandIntegrated.VoicePackStore]::Label($pack);$changed=$true}}
 $script:peonCheckedAt=[DateTime]::UtcNow;return $changed
}

function Play-PeonPreview { return }

function Switch-PeonPack([string]$IDE) {
 try{$result=[IslandIntegrated.VoicePreview]::SwitchNext($IDE);if($result.Duplicate){return};$pack=$result.Pack;if($IDE-eq 'codex'){$controller.Legacy.CodexVoice=$pack}else{$controller.Legacy.ClaudeVoice=$pack};[void](Read-PeonState);$script:statusDirty=$true;if(-not$result.PreviewStarted){$script:statusToolTip.Show('选择已保存，试听暂不可用',$form,2000)}}catch{$script:statusToolTip.Show('配音切换失败，原设置已保留',$form,2000)}
}
function Get-PeonChipRects{
 if(-not $controller.Legacy.VoiceAvailable){return @()}
 $items=@();for($i=0;$i-lt 2;$i++){$r=[IslandIntegrated.StatusUiLayout]::Voice($i,[int]$script:size.w);$items+=@{ide=(@('codex','claude')[$i]);x=$r.X;y=$r.Y;w=$r.Width;h=$r.Height}};return $items
}


function Draw-Ticker($g, [string]$which, [int]$x, [int]$y, [int]$w, [int]$h, [System.Drawing.Color]$color) {
  $tk = $script:ticker
  $pages = $tk.all
  $cur = $tk.idx
  if ($which -eq 'sys') { $pages = $tk.sys; $cur = $tk.sysIdx }
  if (-not $pages -or $pages.Count -eq 0) { return }
  $st = $g.Save()
  $g.SetClip((New-Object System.Drawing.Rectangle($x, $y, $w, $h)))
  $sf = New-Object System.Drawing.StringFormat
  $sf.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap
  $mh = $g.MeasureString($pages[$cur], $F_Page).Height
  $ty = $y + [int](($h - $mh) / 2)
  $br = New-Object System.Drawing.SolidBrush($color)
  if (-not $tk.flipping -or $pages.Count -lt 2) {
    $g.DrawString($pages[$cur], $F_Page, $br, [single]$x, [single]$ty, $sf)
  } else {
    $slide = [single]($tk.progress * $h)
    $nxt = ($cur + 1) % $pages.Count
    $g.DrawString($pages[$cur], $F_Page, $br, [single]$x, [single]($ty - $slide), $sf)
    $g.DrawString($pages[$nxt], $F_Page, $br, [single]$x, [single]($ty + $h - $slide), $sf)
  }
  $br.Dispose(); $sf.Dispose(); $g.Restore($st)
}

# Text now uses GDI+: Graphics.TranslateTransform already applies the body margin. Never add txtDX/txtDY again.
$script:txtDX = 0
$script:txtDY = 0
# GDI+ grayscale antialiasing on premultiplied-alpha status surfaces; no ClearType color fringe.
function Draw-Text($g, [string]$text, $font, [double]$x, [double]$y, [System.Drawing.Color]$color) {
  $brush=[Drawing.SolidBrush]::new($color);$format=[Drawing.StringFormat]::GenericTypographic.Clone()
  try{$format.FormatFlags=[Drawing.StringFormatFlags]::NoWrap -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces;$g.DrawString($text,$font,$brush,[Drawing.PointF]::new([single]($x),[single]($y)),$format)}finally{$brush.Dispose();$format.Dispose()}
}
function Draw-TextIn($g, [string]$text, $font, [double]$x, [double]$y, [double]$w, [double]$h, [System.Drawing.Color]$color, $flags) {
  $brush=[Drawing.SolidBrush]::new($color);$format=[Drawing.StringFormat]::GenericTypographic.Clone()
  try{$format.FormatFlags=[Drawing.StringFormatFlags]::NoWrap;$format.Trimming=[Drawing.StringTrimming]::EllipsisCharacter;if(($flags-band [Windows.Forms.TextFormatFlags]::HorizontalCenter)-ne 0){$format.Alignment=[Drawing.StringAlignment]::Center};if(($flags-band [Windows.Forms.TextFormatFlags]::VerticalCenter)-ne 0){$format.LineAlignment=[Drawing.StringAlignment]::Center};$g.DrawString($text,$font,$brush,[Drawing.RectangleF]::new([single]($x),[single]($y),[single]$w,[single]$h),$format)}finally{$brush.Dispose();$format.Dispose()}
}
function Measure-StatusText($g,[string]$text,$font){$format=[Drawing.StringFormat]::GenericTypographic.Clone();try{$format.FormatFlags=[Drawing.StringFormatFlags]::NoWrap -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces;return $g.MeasureString($text,$font,10000,$format)}finally{$format.Dispose()}}

function Toggle-StatusDock {
  $script:edgeGroup=$false
  $script:dock.Toggle();$script:mode='compact';$script:statusHover=New-Object IslandIntegrated.StatusHoverState
  $script:statusDirty=$true
}
function New-StatusRaster([int]$pw,[int]$ph){
  if($script:dock.HandleMode($ph)){return ,([IslandIntegrated.StatusDockState]::HandleRaster($M,$form.DeviceDpi,$script:dockHover))}
  $data=$controller.Legacy
  [IslandIntegrated.StatusSignal]::ReduceMotion=$controller.ReducedMotion
  $phase=if($script:ticker.flipping){[double]$script:ticker.progress}else{0.0}
  $iconMeasure=[Diagnostics.Stopwatch]::StartNew()
  $bmp=[IslandIntegrated.StatusUiRenderer]::RenderLive($pw,$ph,$M,$data,[int]$script:ticker.sysIdx,$phase)
  [IslandIntegrated.TransitionMetrics]::Record('raster-base',$iconMeasure.Elapsed.TotalMilliseconds);$iconMeasure.Restart()
  $g=[Drawing.Graphics]::FromImage($bmp)
  try{$g.TranslateTransform($M,$M);$fresh=$data.IsFresh([DateTimeOffset]::UtcNow);for($i=0;$i-lt 2;$i++){$icon=[IslandIntegrated.StatusUiLayout]::ServiceIcon($i,$pw,$ph);$known=if($i-eq 0){$data.CodexKnown}else{$data.ClaudeKnown};$present=if($i-eq 0){$data.CodexRun}else{$data.ClaudeUp};$color=if($fresh-and $known-and $present){if($i-eq 0){$C_Codex}else{$C_Claude}}else{[IslandIntegrated.Palette]::Secondary};$scale=if($fresh-and $known-and $present){1.0}else{0.45};if([IslandIntegrated.StatusUiRenderer]::DesktopAgentWorking($data,$i,[DateTimeOffset]::UtcNow)){$wave=if($controller.ReducedMotion){0.0}else{[math]::Sin($script:pulse)};$halo=$icon.Width*(2.0+0.35*$wave);Draw-Glow $g ($icon.X+($icon.Width-$halo)/2) ($icon.Y+($icon.Height-$halo)/2) $halo $halo ([Drawing.Color]::FromArgb(135,$color.R,$color.G,$color.B))};$iconRaster=Get-AgentIconRaster $i $icon.Width $color $scale;$g.DrawImageUnscaled($iconRaster,$icon.X-1,$icon.Y-1)}}catch{$bmp.Dispose();throw}finally{$g.Dispose()}
  [IslandIntegrated.TransitionMetrics]::Record('raster-icons',$iconMeasure.Elapsed.TotalMilliseconds)
  return ,$bmp
}


function Submit-StatusFrame {
  if (-not $script:renderGate.TryEnter()) { return $false }
  $bmp = $null
  try {
    $measure=[Diagnostics.Stopwatch]::StartNew()
    $bmp = New-StatusRaster ([int]$script:size.w) ([int]$script:size.h)
    [IslandIntegrated.TransitionMetrics]::Record('raster',$measure.Elapsed.TotalMilliseconds)
    $measure.Restart()
    $script:statusFrameCache.Update($bmp)
    $presented=$script:statusFrameCache.ComposePointer($controller.StatusPointer)
    [IslandIntegrated.TransitionMetrics]::Record('cache',$measure.Elapsed.TotalMilliseconds);$measure.Restart()
    $ok = [GlassWin]::Push($form.Handle, $presented, $script:pendingBounds.Location)
    [IslandIntegrated.TransitionMetrics]::Record('native-submit',$measure.Elapsed.TotalMilliseconds)
    if (-not $ok) { $script:lastFrameError = [GlassWin]::LastPushError }
    return $ok
  } finally { if ($bmp) { $bmp.Dispose() }; $script:renderGate.Exit() }
}
$form.Add_Paint({ $script:statusDirty=$true }) # Single submission owner: the animation tick.

function Push-ClashSelection { throw "Network control transport is not connected; no network change sent" }
function Switch-RouteTo { $controller.ShowNetworkDetails() }

# ---------- 出口线路下拉（面板内自绘，不另开窗口） ----------
function Get-CurrentRouteName { $n=$controller.Legacy.Network;if($n.Fresh([DateTimeOffset]::UtcNow)-and$n.MihomoState-eq "ok"){return $n.Current};return "" }

function Get-PickerRect {
  # 卡片挂在「出口线路」行（第 4 行）下方，横向铺满面板内边距
  $w = $W_E - 2*$PadX
  return @{ x = $PadX; y = 161; w = $w; h = 88; pad = 5; itemH = 26; ix = ($PadX+4); iw = ($w - 8) }
}

function Get-PickerHit([int]$cx, [int]$cy) {
  if (-not $script:picker.open -or [int]$script:size.h -le 120) { return -1 }
  $pr = Get-PickerRect
  if ($cx -lt $pr.ix -or $cx -gt ($pr.ix + $pr.iw) -or $cy -lt $pr.y -or $cy -gt ($pr.y + $pr.h - 1)) { return -1 }
  for ($i = 0; $i -lt $script:picker.items.Count; $i++) {
    $iy = $pr.y + $pr.pad + $i * $pr.itemH
    if ($cy -ge $iy -and $cy -le ($iy + 23)) { return $i }
  }
  return -1
}

function Show-RoutePicker { [void][IslandIntegrated.WebsiteLatency]::Start();$script:statusDirty=$true }

function Close-RoutePicker {
  $controller.CloseNetworkDropdown()
  if ($script:picker.open) {
    $script:picker.open = $false
    $script:picker.hover = -1
    $script:statusDirty=$true
  }
}
$onDown = {
  param($sender, $e)
  if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
    $script:dragging = $false # Fixed corner; preserve click actions without positional drag.
    $script:dragScreen = [System.Windows.Forms.Cursor]::Position
    $script:dragBaseRight = [int]$script:anchorRight
    $script:dragBaseTop = [int]$script:top
    $script:moved = $false
  }
}
$onMove = {
  param($sender, $e)
  $hint=if($script:dock.HandleMode([int]$script:size.h)){[string][char]0x5c55+[char]0x5f00+[char]0x72b6+[char]0x6001+[char]0x53f0}else{[IslandIntegrated.StatusHints]::At([Drawing.Point]::new($e.X-$M,$e.Y-$M),[int]$script:size.w,([int]$script:size.h-lt 232),$controller.Legacy)}
  if([IslandIntegrated.StatusUiLayout]::CcSwitchButton([int]$script:size.w,([int]$script:size.h -lt 232)).Contains([Drawing.Point]::new($e.X-$M,$e.Y-$M))){$hint=$script:ccStatusText}
  if($hint-ne $script:lastHint){$script:lastHint=$hint;$script:statusToolTip.SetToolTip($form,$hint);$form.AccessibleDescription=$hint}
  if($script:dock.HandleMode([int]$script:size.h)){$form.Cursor=[Windows.Forms.Cursors]::Hand;return}
  if (-not $script:dragging) {
    $expanded = [int]$script:size.h -gt 120
    $panelY = [int]$e.Y - $M
    $cardX = [int]$e.X - $M
    $inRow=$expanded -and [IslandIntegrated.StatusUiLayout]::WebsiteTestHit([int]$script:size.w).Contains([Drawing.Point]::new($cardX,$panelY))
    $hit = -1
    if ($expanded -and $script:picker.open) { $hit = Get-PickerHit $cardX $panelY }
    $peonHit = ''
    if ($expanded -and -not $script:picker.open) {
      foreach ($chip in (Get-PeonChipRects)) {
        if ($cardX -ge $chip.x -and $cardX -le ($chip.x + $chip.w) -and $panelY -ge $chip.y -and $panelY -le ($chip.y + $chip.h)) {
          $peonHit = [string]$chip.ide
          break
        }
      }
    }
    if ($peonHit -ne $script:peonHover) { $script:peonHover = $peonHit; $script:statusDirty=$true }
    $tileHit = -1
    if ($expanded -and -not $script:picker.open) { $tileHit = Get-TileHit $cardX $panelY }
    if ($tileHit -ne $script:tileHover) { $script:tileHover = $tileHit; $script:statusDirty=$true }
    $hand = $inRow -or [bool]$peonHit -or ($tileHit -ge 0) -or [IslandIntegrated.StatusUiLayout]::CcSwitchButton([int]$script:size.w,([int]$script:size.h -lt 232)).Contains([Drawing.Point]::new($cardX,$panelY)) -or ([int]$script:size.h -ge 232 -and [IslandIntegrated.StatusUiLayout]::ThemeButton([int]$script:size.w).Contains([Drawing.Point]::new($cardX,$panelY)))
    if ($hit -ge 0) { $hand = [bool]$script:picker.items[$hit].proxy }
    $want = [System.Windows.Forms.Cursors]::Default
    if ($hand) { $want = [System.Windows.Forms.Cursors]::Hand }
    if ($form.Cursor -ne $want) { $form.Cursor = $want }
    if ($hit -ne $script:picker.hover) { $script:picker.hover = $hit; $script:statusDirty=$true }
    if ($inRow -ne $script:hoverRow) { $script:hoverRow = $inRow; $script:statusDirty=$true }
  }
  # Positional dragging is disabled by the fixed-corner layout policy.
}
$onUp = {
  param($sender, $e)
  $wasMoved = [bool]$script:moved
  $script:dragging = $false
  if ($wasMoved) { return }
  if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
  if($script:dock.HandleMode([int]$script:size.h)){if([Drawing.Rectangle]::new([Drawing.Point]::Empty,[IslandIntegrated.StatusDockState]::HandleSize($form.DeviceDpi)).Contains([Drawing.Point]::new($e.X-$M,$e.Y-$M))){Toggle-StatusDock};return}
  if([IslandIntegrated.StatusDockState]::CollapseButton([int]$script:size.w).Contains([Drawing.Point]::new($e.X-$M,$e.Y-$M))){Toggle-StatusDock;return}
  $surfacePoint=[Drawing.Point]::new($e.X-$M,$e.Y-$M)
  if([IslandIntegrated.StatusUiLayout]::CcSwitchButton([int]$script:size.w,([int]$script:size.h -lt 232)).Contains($surfacePoint)){Invoke-CcSwitch;return}
  if([int]$script:size.h -ge 232 -and [IslandIntegrated.StatusUiLayout]::ThemeButton([int]$script:size.w).Contains($surfacePoint)){$controller.ShowThemePicker($form,[Drawing.Point]::new($e.X,$e.Y));return}
  if ([int]$script:size.h -le 120) { return }
  $panelY = [int]$e.Y - $M
  $cardX = [int]$e.X - $M
  if ($cardX -lt 0 -or $cardX -gt $W_E) { return }
  if ($script:picker.open) {
    $hit = Get-PickerHit $cardX $panelY
    if ($hit -ge 0) {
      $it = $script:picker.items[$hit]
      if (-not $it.proxy) { $script:flashBadUntil = (Get-Date).AddMilliseconds(700); $script:statusDirty=$true; return }
      if (($it.proxy -replace '^[^\p{L}\p{N}]+', '') -eq (Get-CurrentRouteName)) { Close-RoutePicker; return }
      Switch-RouteTo ([string]$it.proxy)
      return
    }
    $pr = Get-PickerRect
    $inCard = $cardX -ge $pr.x -and $cardX -le ($pr.x + $pr.w) -and $panelY -ge $pr.y -and $panelY -le ($pr.y + $pr.h)
    if (-not $inCard) { Close-RoutePicker }
    return
  }
  $tileHit = Get-TileHit $cardX $panelY
  if ($tileHit -ge 0) {
    $svc = $script:view.services[$tileHit]
    if ($svc) { Open-AgentApp ([string]$svc.key) }
    return
  }
  foreach ($chip in (Get-PeonChipRects)) {
    if ($cardX -ge $chip.x -and $cardX -le ($chip.x + $chip.w) -and $panelY -ge $chip.y -and $panelY -le ($chip.y + $chip.h)) {
      Switch-PeonPack ([string]$chip.ide)
      return
    }
  }
  if([IslandIntegrated.StatusUiLayout]::WebsiteTestHit([int]$script:size.w).Contains([Drawing.Point]::new($cardX,$panelY))){Show-RoutePicker}
}
$form.Add_MouseDown($onDown)
$form.Add_MouseMove($onMove)
$form.Add_MouseUp($onUp)
$form.KeyPreview = $true
$form.Add_KeyDown({ param($sender, $e) if ($e.KeyCode -eq [System.Windows.Forms.Keys]::Escape) { Close-RoutePicker; if(-not $controller.CancelManagement()){$controller.Collapse(); $script:mode='compact'} } })

$script:workerProc = $null
function Start-Worker { return }

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$dockItem=$menu.Items.Add([string][char]0x6536+[char]0x8D77+[char]0x72B6+[char]0x6001+[char]0x53F0)
$dockItem.Add_Click({Toggle-StatusDock})
$menu.Add_Opening({$dockItem.Text=if($script:dock.Hidden){[string][char]0x6062+[char]0x590D+[char]0x72B6+[char]0x6001+[char]0x53F0}else{[string][char]0x6536+[char]0x8D77+[char]0x72B6+[char]0x6001+[char]0x53F0}})
$themeAction=$menu.Items.Add('切换主题');$themeAction.Add_Click({$controller.ShowThemePicker($form,[Drawing.Point]::new(30,30))})
$routeInfo=$menu.Items.Add('网络与线路（只读）');$routeInfo.ToolTipText='查看状态和线路预览，不会切换网络';$routeInfo.Add_Click({$controller.ShowNetworkDetails()})
$exitItem = $menu.Items.Add('退出集成预览')
$exitItem.Add_Click({ $form.Close() })
$voiceCodex=$menu.Items.Add('Codex 配音 · 切换并试听');$voiceCodex.Add_Click({Switch-PeonPack 'codex'})
$voiceClaude=$menu.Items.Add('Claude 配音 · 切换并试听');$voiceClaude.Add_Click({Switch-PeonPack 'claude'})
$balanceInfo=$menu.Items.Add('DeepSeek 余额');$balanceInfo.Add_Click({[void][Windows.Forms.MessageBox]::Show('余额查询尚未接入，本次未查询账户，也未读取密钥。','DeepSeek')})
$form.AccessibleName='状态台';$form.AccessibleDescription='性能轮播；线路；DeepSeek；Codex 和 Claude 配音。右键打开操作菜单。'
$form.ContextMenuStrip = $menu
# Offline adapter: never opens shared status or session stores.
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1200
$script:view.pill = '本机采样中'
$script:view.sub = '数据源尚未接入'
$script:ticker.all = @('离线预览 · 数据不可用')
$script:ticker.sys = @('GPU / 显存 · 不可用')
foreach ($row in $script:view.rows) { $row.v = '不可用' }
foreach ($metric in $script:view.metrics) { $metric.v = '—'; $metric.pct = 0 }
$controller = New-Object IslandIntegrated.App (Join-Path $Root 'layout.xml'), $form
$controller.StatusShadowMargin=$M
$script:lastLegacyStamp=''
$script:ccChecked=[DateTime]::MinValue; $script:ccStatusText='正在检查 CC Switch'; $script:ccTask=$null
$script:lastWebsiteLabel=''
$timer.Add_Tick({
  if($script:ccTask -and $script:ccTask.IsCompleted){try{$ccStatus=$script:ccTask.GetAwaiter().GetResult();$available=$ccStatus.State -eq [IslandIntegrated.CcSwitchState]::Running -or $ccStatus.State -eq [IslandIntegrated.CcSwitchState]::Available;if([IslandIntegrated.StatusUiRenderer]::CcSwitchAvailable -ne $available){[IslandIntegrated.StatusUiRenderer]::CcSwitchAvailable=$available;$script:statusDirty=$true};$script:ccStatusText=if($available){'启动或切换到 CC Switch'}else{'未检测到 CC Switch'}}catch{[IslandIntegrated.StatusUiRenderer]::CcSwitchAvailable=$false;$script:ccStatusText='CC Switch 状态不可用'}finally{$script:ccTask=$null}}
  if($form.Visible -and -not $script:ccTask -and (([DateTime]::UtcNow-$script:ccChecked).TotalSeconds -ge 30)){$script:ccChecked=[DateTime]::UtcNow;$script:ccTask=$script:ccLauncher.GetStatusAsync($script:ccConfigured)}
  if($form.Visible){[void][IslandIntegrated.WebsiteLatency]::StartIfDue()};$websiteLabel=[IslandIntegrated.WebsiteLatency]::Label([DateTimeOffset]::UtcNow);if($websiteLabel-ne $script:lastWebsiteLabel){$script:lastWebsiteLabel=$websiteLabel;$script:statusDirty=$true}
  $data=$controller.Legacy
  $fresh=$data.IsFresh([DateTimeOffset]::UtcNow)
  $state=if($data.LocalOnly-and -not $fresh-and $data.Fresh){'stale'}else{$data.State}
  $stamp=$state+'|'+$data.Timestamp.ToString('o')
  if($stamp-eq $script:lastLegacyStamp){return};$script:lastLegacyStamp=$stamp
  if($fresh){
    $script:view.pill='本机状态';$script:view.pillC=$C_Text;$script:view.sub='设备与应用状态'
    $script:view.rows[0].v=if(-not $data.NetworkKnown){'读数不可用'}elseif($data.NetworkAvailable){'链路可用'}else{'未连接'}
    $script:view.rows[1].v='未接入';$script:view.rows[2].v='未授权';$script:view.rows[3].v='未检测'
    $numbers=@($data.Cpu,$data.Memory,$data.DiskD,$data.Gpu)
    for($i=0;$i-lt 4;$i++){if($null-ne $numbers[$i]){$script:view.metrics[$i].v=([double]$numbers[$i]).ToString('0.0')+'%';$script:view.metrics[$i].pct=[double]$numbers[$i]}else{$script:view.metrics[$i].v=if($i-eq 3){'未提供'}else{'采样中'};$script:view.metrics[$i].pct=0}}
    $script:view.services[0].up=$data.CodexRun;$script:view.services[0].known=$data.CodexKnown;$script:view.services[1].up=$data.ClaudeUp;$script:view.services[1].known=$data.ClaudeKnown
    $script:ticker.sys=[IslandIntegrated.StatusPerformance]::Pages($data)
  }else{
    $label=switch($state){'stale'{'采样已过期'}'starting'{'采样中'}default{'本机采样不可用'}}
    $script:view.pill=$label;$script:view.pillC=$C_Warn;foreach($metric in $script:view.metrics){$metric.v='—';$metric.pct=0};foreach($row in $script:view.rows){$row.v=$label};foreach($service in $script:view.services){$service.up=$false;$service.known=$false;$service.busy=$false};$script:ticker.sys=@($label)
  }
  $script:statusDirty=$true
})
$script:tickerHold = 0
$script:frame = 0
$script:lastAnimationTick = [IslandIntegrated.PreviewClock]::NowMs
$script:lastPulseFrameAt = 0
$anim = New-Object System.Windows.Forms.Timer
$anim.Interval = 30
$anim.Add_Tick({
    if($script:renderGate.Closed){return}
    [IslandIntegrated.TransitionMetrics]::MarkTick("status",[IslandIntegrated.PreviewClock]::NowMs)
    $submittedThisTick=$false
    $animationNow=[IslandIntegrated.PreviewClock]::NowMs
    $animationDelta=[math]::Min(100,[math]::Max(0,$animationNow-$script:lastAnimationTick))
    $script:lastAnimationTick=$animationNow
    if($controller.EdgeRevealRequested){$controller.EdgeRevealRequested=$false;if($script:dock.Hidden){Toggle-StatusDock};$script:edgeGroup=$true}
    $cp=[System.Windows.Forms.Cursor]::Position
    $hs=[IslandIntegrated.StatusDockState]::HandleSize($form.DeviceDpi);$hb=[Drawing.Rectangle]::new($script:pendingBounds.Left+$M,$script:pendingBounds.Top+$M,$hs.Width,$hs.Height);if($script:dockLight.Step(($script:dock.HandleMode([int]$script:size.h)-and $hb.Contains($cp)),[IslandIntegrated.PreviewClock]::NowMs,$controller.ReducedMotion)){$script:dockHover=$script:dockLight.Amount;$script:statusDirty=$true}
    $body=[Drawing.Rectangle]::new($script:pendingBounds.Left+$M,$script:pendingBounds.Top+$M,[int]$script:size.w,[int]$script:size.h)
    $blocked=$controller.Suppressed -or $controller.FullscreenBlocked -or $script:dock.HandleMode([int]$script:size.h)
    $wasMode=$script:mode
    if(-not $controller.Expanded){$script:edgeGroup=$false}
    $script:mode=if($script:statusHover.Tick([IslandIntegrated.PreviewClock]::NowMs,$cp,$body,$blocked)){'expanded'}else{'compact'}
    if(($controller.NetworkDropdownVisible -or $controller.ThemePickerVisible -or ($script:edgeGroup -and $controller.Expanded)) -and -not $controller.Suppressed -and -not $controller.FullscreenBlocked){$script:mode='expanded'}
    if($script:mode-ne $wasMode){
      $script:statusDirty=$true
      try{$record=@{schema=1;candidate=$script:releaseCandidate;mode=$script:mode;utc=[DateTime]::UtcNow.ToString('o');cursor=@{x=$cp.X;y=$cp.Y};body=@{x=$body.X;y=$body.Y;width=$body.Width;height=$body.Height};frames=$script:statusFrameCount};[IO.File]::WriteAllText((Join-Path $Root 'logs/status-hover-latest.json'),($record|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}catch{}
    }
    if($controller.PinkyPanelRequested){$script:mode='compact'}
    $tgtW = $W_C; $tgtH = $H_C
    if ($script:mode -eq 'expanded') { $tgtW = $W_E; $tgtH = [math]::Min($H_E,$controller.MaximumStatusBodyHeight) }
    if($script:dock.Hidden){$tgtH=0}

    $previousHeight=[int]$script:size.h
    $script:size.h=$script:heightMotion.Step($tgtH,[IslandIntegrated.PreviewClock]::NowMs,$controller.ReducedMotion)
    $changed=($previousHeight-ne [int]$script:size.h)
    $anim.Interval=if($script:heightMotion.Moving -or $controller.RevealAnimating){15}else{30}
    $controller.SetStatusAnimationActive($script:heightMotion.Moving -or $controller.RevealAnimating)
    if ($changed) {
      [IslandIntegrated.TransitionMetrics]::MarkTick("status-transition",[IslandIntegrated.PreviewClock]::NowMs)
      [void](Set-Geometry)
      $script:statusDirty=$true
    }

    # 翻页动画
    $tk = $script:ticker
    $script:tickerHold += $animationDelta
    if ($tk.flipping) {
      $tk.progress = if($controller.ReducedMotion){1.0}else{$tk.progress+0.09*$animationDelta/30.0}
      if ($tk.progress -ge 1.0) {
        $tk.progress = 0.0
        $tk.flipping = $false
        $tk.idx = ($tk.idx + 1) % [math]::Max(1, $tk.all.Count)
        $tk.sysIdx = ($tk.sysIdx + 1) % [math]::Max(1, $tk.sys.Count)
        $script:statusDirty=$true
$script:tickerHold = 0
      }
      $script:statusDirty=$true
    } elseif ($script:tickerHold -ge 3200 -and ([math]::Max($tk.all.Count,$tk.sys.Count) -gt 1)) {
      $tk.flipping = $true
      $tk.progress = 0.0
      $script:statusDirty=$true
    }

    # 呼吸：展开态且有人在忙时，让光斑/忙碌点轻微起伏（8fps，够用且省 CPU）
    $script:pulse += 0.07854*$animationDelta/30.0
    if ($script:pulse -gt 6.28) { $script:pulse -= 6.28 }
    $script:frame++
    if ($form.Visible -and -not $controller.ReducedMotion -and ($animationNow-$script:lastPulseFrameAt) -ge 120) {
      $script:lastPulseFrameAt=$animationNow
      $signal=[IslandIntegrated.StatusSignal]::From($controller.Legacy)
      $anyBusy = $script:picker.open -or $signal.Kind -eq "warning" -or $signal.Kind -eq "error"
      for($i=0;$i-lt 2;$i++){if([IslandIntegrated.StatusUiRenderer]::DesktopAgentWorking($controller.Legacy,$i,[DateTimeOffset]::UtcNow)){$anyBusy=$true}}
      if ($anyBusy) { $script:statusDirty=$true }
    }

    # 切换反馈：闪色期间保持重绘，结束后清掉状态再重绘一次
    if ($script:flashOkUntil) {
      if ((Get-Date) -lt $script:flashOkUntil) { $script:statusDirty=$true } else { $script:flashOkUntil = $null; $script:statusDirty=$true }
    }
    if ($script:flashBadUntil) {
      if ((Get-Date) -lt $script:flashBadUntil) { $script:statusDirty=$true } else { $script:flashBadUntil = $null; $script:statusDirty=$true }
    }
    foreach ($ide in @('codex', 'claude')) {
      $flashUntil = $script:peonBindings[$ide].flashUntil
      if ($flashUntil) {
        if ((Get-Date) -lt $flashUntil) { $script:statusDirty=$true } else { $script:peonBindings[$ide].flashUntil = $null; $script:statusDirty=$true }
      }
      $flashBadUntil = $script:peonBindings[$ide].flashBadUntil
      if ($flashBadUntil) {
        if ((Get-Date) -lt $flashBadUntil) { $script:statusDirty=$true } else { $script:peonBindings[$ide].flashBadUntil = $null; $script:statusDirty=$true }
      }
    }

    # 展开态每 4 秒同步一次 peon 当前音效包，外部 CLI 切换也能反映到按钮
    # This preview has no peon data adapter. An offline no-op must not repaint every 30ms.
    if ([IslandIntegrated.PreviewRefresh]::Due($false, [DateTime]::UtcNow, $script:peonCheckedAt)) {
      if (Read-PeonState) { $script:statusDirty=$true }
    }

    if($controller.MaintainStatusStack()){$script:surfaceReplayRequested=$true}
    if($script:mode-eq 'compact'){$script:tileHover=-1;$script:peonHover='';Close-RoutePicker}
    # Edge-open widgets no longer force the status panel to expand or move.
    $controller.StatusDetailOpen=($script:mode-eq 'expanded' -or [int]$script:size.h-gt $H_C)
    $controller.StatusHidden=$script:dock.Hidden
    $layoutMeasure=[Diagnostics.Stopwatch]::StartNew()
    $controller.StatusBodyHeight=[int]$script:size.h
    [IslandIntegrated.TransitionMetrics]::Record('linked-layout',$layoutMeasure.Elapsed.TotalMilliseconds)
    $geometryChanged=Set-Geometry
    if($script:geometryContextDirty){
      $script:geometryContextDirty=$false
      if($script:bgCache.bmp){$script:bgCache.bmp.Dispose();$script:bgCache.bmp=$null};$script:bgCache.key=''
      $geometryChanged=$true
    }
    if($geometryChanged){$script:statusDirty=$true}
    if($script:lastTheme-ne [IslandIntegrated.Palette]::Theme){$script:lastTheme=[IslandIntegrated.Palette]::Theme;$script:statusDirty=$true}
    if($script:lastPalette-ne [IslandIntegrated.Palette]::Legacy){$script:lastPalette=[IslandIntegrated.Palette]::Legacy;$script:statusDirty=$true}
    [IslandIntegrated.TransitionMetrics]::FlushIfDue([IslandIntegrated.PreviewClock]::NowMs)
    # UpdateLayeredWindow does not depend on a normal WM_PAINT cycle. Commit once per changed tick.
    if($script:statusDirty){
      if(Submit-StatusFrame){$script:statusDirty=$false;$script:statusFrameCount++;$submittedThisTick=$true}
    }
    if($script:lastHighlightSequence-ne $controller.HighlightSequence -or $script:surfaceReplayRequested){$script:surfaceReplayRequested=$false;
      $script:lastHighlightSequence=$controller.HighlightSequence
      if(-not $submittedThisTick -and $form.Visible -and $script:statusFrameCache.MatchesSize($script:pendingBounds.Size) -and $script:renderGate.TryEnter()){
        $shine=$null
        try{$shine=$script:statusFrameCache.ComposePointer($controller.StatusPointer);if($shine){[void][GlassWin]::Push($form.Handle,$shine,$script:pendingBounds.Location)}}
        finally{$script:renderGate.Exit()} # ComposePointer buffer is cache-owned, not disposable by caller.
      }
    }
  })

Read-PeonState
[void](Set-Geometry)

$form.Add_Shown({
    # Layered style and initial geometry are already applied before the first Show.
    try {
      $elapsed=if($global:SidebarStartupWatch){$global:SidebarStartupWatch.ElapsedMilliseconds}else{$null}
      $first=@{schema=1;phase='shown';firstPixelsCommitted=$script:firstFrameGate.CanShow;candidate=$script:releaseCandidate;elapsedFromEntryMs=$elapsed;bounds=@{left=$form.Left;top=$form.Top;width=$form.Width;height=$form.Height};startPosition=[string]$form.StartPosition;utc=[DateTime]::UtcNow.ToString('o')}
      [IO.File]::WriteAllText((Join-Path $Root 'logs\startup-first-shown.json'),($first|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    } catch {}
    $script:statusDirty=$true
    # Collector disabled in isolated preview
    $timer.Start()
    $anim.Start()
  })
$form.Add_FormClosed({
  try { @{right=[int]$script:anchorRight;top=[int]$script:top} | ConvertTo-Json | Set-Content -LiteralPath $PosFile -Encoding UTF8 } catch { }
  $script:renderGate.Close()
  $timer.Stop()
  $anim.Stop()
})
# Prepare all controls and the actual complete compact raster before visibility.
[void](Set-Geometry)
$controller.PrepareInitialLayout([System.Windows.Forms.Screen]::FromPoint($script:pendingBounds.Location))
$script:firstFrameGate.GeometryPrepared($script:pendingBounds)
$script:firstFrameGate.ControlsPrepared()
$firstCommitted = Submit-StatusFrame
$script:firstFrameGate.PixelsCommitted($script:pendingBounds.Size, $firstCommitted)
$script:firstFrameGate.RequireReady()
[IO.File]::WriteAllText((Join-Path $Root 'logs\startup-first-pixels.json'),(@{schema=1;candidate=$script:releaseCandidate;phase='pixels-before-show';elapsedFromEntryMs=$global:SidebarStartupWatch.ElapsedMilliseconds;committed=$firstCommitted;left=$script:pendingBounds.Left;top=$script:pendingBounds.Top;width=$script:pendingBounds.Width;height=$script:pendingBounds.Height;utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
[void]$form.ShowDialog()

} finally {
  # Runs on normal close and failures before ShowDialog/FormClosed. Each cleanup is isolated.
  if($script:renderGate){$script:renderGate.Close()}
  if($script:statusFrameCache){try{$script:statusFrameCache.Dispose()}catch{}}
  foreach($image in $script:agentIconCache.Values){try{$image.Dispose()}catch{}}
  $script:agentIconCache.Clear()
  foreach($resource in @($anim,$timer)){if($resource){try{$resource.Stop()}catch{};try{$resource.Dispose()}catch{}}}
  if($controller){try{$controller.Shutdown()}catch{}};if($script:statusToolTip){try{$script:statusToolTip.Dispose()}catch{}}
  if($form){try{$form.Dispose()}catch{}}
  if($menu){try{$menu.Dispose()}catch{}}
  if($script:bgCache -and $script:bgCache.bmp){try{$script:bgCache.bmp.Dispose()}catch{};$script:bgCache.bmp=$null}
  foreach($fontName in @('F_Title','F_Name','F_Sub','F_Row','F_Val','F_Small','F_Pill','F_Page','F_Tiny')){
    $font=Get-Variable -Name $fontName -ValueOnly -ErrorAction SilentlyContinue
    if($font){try{$font.Dispose()}catch{}}
  }
  if($mutexAcquired){try{$mutex.ReleaseMutex()}catch{};$mutexAcquired=$false}
  $mutex.Dispose()
}
