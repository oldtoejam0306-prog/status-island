using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Threading.Tasks;
using IslandMedia;
using SidebarClockCalendar;

namespace IslandIntegrated {
  public class BufferedFlowPanel : Panel {
    public FlowDirection FlowDirection;public bool WrapContents;
    public BufferedFlowPanel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
  }
  public class WidgetPanel : Panel {
    Bitmap material;readonly PointerLight pointer=new PointerLight();
    public void UpdatePointer(long now,Point screenPoint,bool enabled){
      var old=GlassSurface.PointerBounds(ClientRectangle,pointer);var local=PointToClient(screenPoint);
      if(!pointer.Tick(now,local,Visible&&ClientRectangle.Contains(local),enabled&&Visible))return;
      var next=GlassSurface.PointerBounds(ClientRectangle,pointer);var dirty=old.IsEmpty?next:next.IsEmpty?old:Rectangle.Union(old,next);if(Visible&&!dirty.IsEmpty)Invalidate(dirty,true);
    }
    internal void PaintMaterialLight(Graphics g){GlassSurface.PaintPointer(g,new RectangleF(0,0,Width-1,Height-1),24,pointer);}
    public WidgetPanel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.SupportsTransparentBackColor,true);}
    protected override void OnPaintBackground(PaintEventArgs e){
      if(Width<2||Height<2)return;
      if(material==null){material=new Bitmap(Width,Height);using(var g=Graphics.FromImage(material)){g.Clear(Palette.Base);GlassSurface.Paint(g,new RectangleF(0,0,Width-1,Height-1),24,true);}}
      e.Graphics.DrawImageUnscaled(material,0,0);
      GlassSurface.PaintPointer(e.Graphics,new RectangleF(0,0,Width-1,Height-1),24,pointer);
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(material!=null){material.Dispose();material=null;}if(Width<2||Height<2)return;using(var p=GlassSurface.Path(new RectangleF(0,0,Width,Height),Math.Min(24,Height/2f))){var old=Region;Region=new Region(p);if(old!=null)old.Dispose();}Invalidate();}
    protected override void Dispose(bool disposing){if(disposing&&material!=null){material.Dispose();material=null;}base.Dispose(disposing);}
  }
  public class MediaControlButton : Button {
    public string Action; public bool Playing;
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var color=Enabled?Color.FromArgb(239,242,247):Color.FromArgb(112,122,138);using(var brush=new SolidBrush(color))using(var pen=new Pen(color,2)){float x=Width/2f,y=Height/2f;if(Action=="toggle"&&Playing){e.Graphics.FillRectangle(brush,x-7,y-7,4,14);e.Graphics.FillRectangle(brush,x+3,y-7,4,14);}else{bool back=Action=="previous";float d=back?-1:1;e.Graphics.FillPolygon(brush,new[]{new PointF(x-5*d,y-7),new PointF(x-5*d,y+7),new PointF(x+6*d,y)});if(Action!="toggle")e.Graphics.DrawLine(pen,x+9*d,y-7,x+9*d,y+7);}} }
  }
  public sealed class HoverState {
    public bool Open, Pinned; public long EdgeSince=-1, OutsideSince=-1;
    public void Tick(long now, bool edge, bool inside, bool blocked) {
      if (blocked) { EdgeSince=-1; Open=false; OutsideSince=-1; return; }
      if (!Open) { if(inside){Open=true;EdgeSince=-1;OutsideSince=-1;return;} if (!edge) EdgeSince=-1; else if (EdgeSince<0) EdgeSince=now; else if(now-EdgeSince>=250) { Open=true; EdgeSince=-1; } }
      else if (Pinned || inside || edge) OutsideSince=-1;
      else if (OutsideSince<0) OutsideSince=now;
      else if(now-OutsideSince>=650) { Open=false; OutsideSince=-1; }
    }
  }
  public static class Native {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    public static bool Fullscreen(Screen s) {
      IntPtr h=GetForegroundWindow(); RECT r;
      if(h==IntPtr.Zero || !GetWindowRect(h,out r)) return false;
      // Maximized windows use WorkingArea and are not treated as exclusive fullscreen.
      return CoversScreen(new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top), s.Bounds, IsZoomed(h));
    }
    public static bool CoversScreen(Rectangle window, Rectangle screen, bool maximized) { return !maximized && window.Left<=screen.Left && window.Top<=screen.Top && window.Right>=screen.Right && window.Bottom>=screen.Bottom; }
    public static bool ButtonsDown() { return (GetAsyncKeyState(1)&0x8000)!=0 || (GetAsyncKeyState(2)&0x8000)!=0 || (GetAsyncKeyState(4)&0x8000)!=0; }
  }
  public class NoActivateForm : Form {
    public bool PerPixelLayered {get;set;}
    public event EventHandler GeometryContextChanged,LayeredSurfaceChanged;
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    public const uint NoActivateTopmostFlags=0x0013|0x0200;
    public static Func<IntPtr,bool> RestoreOwnTopmost=window=>SetWindowPos(window,new IntPtr(-1),0,0,0,0,NoActivateTopmostFlags);
    public bool RestoreTopmostWithoutActivation(){return IsHandleCreated&&Visible&&RestoreOwnTopmost(Handle);}
    protected override void OnPaintBackground(PaintEventArgs e){if(!PerPixelLayered)base.OnPaintBackground(e);}
    protected override void OnPaint(PaintEventArgs e){if(!PerPixelLayered)base.OnPaint(e);}
    protected override void WndProc(ref Message m){if(PerPixelLayered&&m.Msg==0x0014){m.Result=new IntPtr(1);return;}base.WndProc(ref m);if(m.Msg==0x007e||m.Msg==0x02e0||m.Msg==0x001a){var handler=GeometryContextChanged;if(handler!=null)handler(this,EventArgs.Empty);}if(PerPixelLayered&&(m.Msg==0x031e||m.Msg==0x0018&&m.WParam!=IntPtr.Zero)){var handler=LayeredSurfaceChanged;if(handler!=null)handler(this,EventArgs.Empty);}}
    public NoActivateForm(){StartPosition=FormStartPosition.Manual;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);DoubleBuffered=true;}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80;if(PerPixelLayered)p.ExStyle|=0x80000;return p;}}
  }

 // Presentation-only anchor. Raw native snapshots remain untouched for controls/seeking.
 public sealed class MediaResumePresentation {
  string key;DateTimeOffset? pausedAt,anchorAt,nativeAnchorAt;double? anchorPosition,nativeAnchorPosition;
  public Snapshot Present(Snapshot raw){
   if(raw==null||String.IsNullOrEmpty(raw.SourceId)){Reset();return raw??new Snapshot();}
   string next=NeteaseSource.TrackKey(raw);if(next!=key){Reset();key=next;}
   if(!raw.Playing){pausedAt=raw.PlaybackObservedAt;ClearAnchor();return raw;}
   bool valid=raw.PositionSeconds.HasValue&&!Double.IsNaN(raw.PositionSeconds.Value)&&!Double.IsInfinity(raw.PositionSeconds.Value)&&raw.PositionSeconds.Value>=0&&raw.PositionSeconds.Value<=86400&&(!raw.DurationSeconds.HasValue||raw.PositionSeconds.Value<=raw.DurationSeconds.Value)&&raw.PositionTimestamp.HasValue&&raw.PlaybackObservedAt.HasValue;
   if(!valid||!pausedAt.HasValue||raw.PlaybackObservedAt.Value<pausedAt.Value){ClearAnchor();return raw;}
   if(raw.PositionTimestamp.Value>pausedAt.Value){pausedAt=null;ClearAnchor();return raw;}
   if(!anchorAt.HasValue||nativeAnchorAt!=raw.PositionTimestamp||nativeAnchorPosition!=raw.PositionSeconds){anchorAt=raw.PlaybackObservedAt;anchorPosition=raw.PositionSeconds;nativeAnchorAt=raw.PositionTimestamp;nativeAnchorPosition=raw.PositionSeconds;}
   return new Snapshot{Title=raw.Title,Artist=raw.Artist,Album=raw.Album,Detail=raw.Detail,SourceId=raw.SourceId,Cover=raw.Cover,PositionSeconds=anchorPosition,DurationSeconds=raw.DurationSeconds,PositionTimestamp=anchorAt,PlaybackObservedAt=raw.PlaybackObservedAt,CanToggle=raw.CanToggle,CanPrevious=raw.CanPrevious,CanNext=raw.CanNext,Playing=raw.Playing,CanSeek=raw.CanSeek,ObservedTicks=raw.ObservedTicks};
  }
  void ClearAnchor(){anchorAt=null;anchorPosition=null;nativeAnchorAt=null;nativeAnchorPosition=null;}
  void Reset(){key=null;pausedAt=null;ClearAnchor();}
 }

  public class App : ApplicationContext {
    readonly MediaResumePresentation musicPresentation=new MediaResumePresentation();
    readonly Form status;
    LayoutEditSession editSession;
    Layout CurrentLayout {get{return editSession==null?layout:editSession.Draft;}}
    public LegacySnapshot Legacy {get;private set;}
    readonly LocalSystemSource localSource=new LocalSystemSource();
    bool legacyPending;long legacyChecked=-2000;
    void PollLegacy(long now){
      if(legacyPending||now-legacyChecked<2000)return;var target=status??form;if(!target.IsHandleCreated)return;legacyPending=true;legacyChecked=now;
      Task.Run(()=>localSource.Read()).ContinueWith(task=>{try{if(!shuttingDown&&!target.IsDisposed)target.BeginInvoke(new Action(()=>{if(!shuttingDown)Legacy=task.Status==TaskStatus.RanToCompletion?task.Result:new LegacySnapshot();legacyPending=false;}));}catch{legacyPending=false;}});
    }
    public int StatusShadowMargin=16;
    public bool StatusDetailOpen {get;set;}
    public bool StatusHidden {get;set;}
    public bool EdgeRevealRequested {get;set;}
    readonly EdgeWakeGate edgeWake=new EdgeWakeGate();
    int statusBodyHeight=CornerLayout.CompactHeight;
    public int StatusBodyHeight {
      get{return statusBodyHeight;}
      set{
        int next=Math.Max(0,value);if(next==statusBodyHeight)return;statusBodyHeight=next;
        // Called on the shared UI thread before the new layered status frame is submitted.
        // Move the existing controls immediately, so the next controller timer cannot lag a frame.
        if(active!=null&&form.Visible&&!shuttingDown){PrepareInitialLayout(active);var target=AnchoredBounds(active);form.Location=new Point(target.Left+reveal.Offset(form.DeviceDpi),target.Top);}
      }
    }
    public int MaximumStatusBodyHeight {get{return CornerLayout.MaximumStatusHeight(TargetArea,StatusShadowMargin);}}
    public PointerLight StatusPointer {get;private set;}
    readonly PointerLight statusPointer=new PointerLight();
    readonly RevealMotion reveal=new RevealMotion();
    public bool HighlightEnabled {get;private set;}
    public double HighlightPhase {get;private set;}
    public long HighlightSequence {get;private set;}
    public bool ReducedMotion {get{return CurrentLayout.ReduceMotion||SystemInformation.HighContrast||!SystemInformation.UIEffectsEnabled;}}
    readonly NeteaseSource mediaSource;
    readonly LrclibSource lyricsSource;LyricResult currentLyrics=new LyricResult("disabled");string lyricsKey=null;int lyricsSequence;bool lyricsPending;System.Threading.CancellationTokenSource lyricsCancellation;DateTime lyricsRetryAt=DateTime.MinValue; readonly MediaRefreshPolicy mediaRefreshPolicy=new MediaRefreshPolicy(); Snapshot media=new Snapshot(); Image artwork; bool mediaRefreshing, shuttingDown;DateTime mediaFastUntil=DateTime.MinValue;ControlTrace mediaTrace;bool mediaBeforePlaying;string mediaBeforeTrack;DateTime mediaChecked=DateTime.MinValue;
    public bool Expanded { get { return state.Open; } }
    public bool FullscreenBlocked { get { return Native.Fullscreen(active??Screen.PrimaryScreen); } }
    public bool Suppressed { get { return PreviewClock.NowMs < suppressedUntil; } }
    public Rectangle TargetArea { get { return (active ?? Screen.PrimaryScreen).WorkingArea; } }
    public bool UseCompactStatus { get { return TargetArea.Height<780; } }
    public int StatusTop { get { return TargetArea.Top+StatusShadowMargin; } }
    public void Collapse() { if(editing)CancelEditing();editing=false; state.Open=false; state.Pinned=false; CurrentLayout.Pinned=false; suppressedUntil=PreviewClock.NowMs+1000L; reveal.Target(false,PreviewClock.NowMs,ReducedMotion); Save(); }
    readonly PreviewCleanup cleanup=new PreviewCleanup();
    ContextMenuStrip addMenu;
    public int CleanupErrors {get;private set;}
    public void Shutdown() {
      if(shuttingDown)return;shuttingDown=true;if(dragInput!=null)dragInput.Cancel();EndDragPreview();
      CleanupErrors=cleanup.Run(()=>{if(routeDropdown!=null){routeDropdown.Dispose();routeDropdown=null;}},()=>timer.Stop(),()=>timer.Dispose(),()=>localSource.Dispose(),()=>{mediaSource.StateChanged-=OnMediaChanged;mediaSource.Dispose();VoicePreview.Close();},()=>{lyricsSequence++;if(lyricsCancellation!=null){lyricsCancellation.Cancel();lyricsCancellation.Dispose();lyricsCancellation=null;}lyricsSource.Dispose();},()=>{var linked=status as NoActivateForm;if(linked!=null)linked.GeometryContextChanged-=OnGeometryContextChanged;},()=>{if(addMenu!=null)addMenu.Dispose();},()=>{if(form.ContextMenuStrip!=null)form.ContextMenuStrip.Dispose();},()=>form.Dispose(),()=>{if(artwork!=null){artwork.Dispose();artwork=null;}});
    }
    public static bool TestShutdown(NeteaseSource source) {
      var app=new App(Path.Combine(Path.GetTempPath(),"column-test-no-layout.xml"),null,source);
      app.Shutdown();
      return source.ReadAsync().GetAwaiter().GetResult().Detail=="媒体源已关闭";
    }
    NormalUpdateHandoff normalUpdate;
    RouteDropdown routeDropdown;readonly StatusPresencePolicy statusPresence=new StatusPresencePolicy();
    public bool NetworkDropdownVisible{get{return routeDropdown!=null&&routeDropdown.Visible;}}
    public void CloseNetworkDropdown(){if(routeDropdown!=null)routeDropdown.Close();}
    public void ShowNetworkDetails(){if(modal)return;if(NetworkDropdownVisible){routeDropdown.Toggle();return;}if(routeDropdown!=null)routeDropdown.Dispose();var owner=status??form;int width=Math.Max(220,owner.Width-2*StatusShadowMargin);var row=StatusUiLayout.RouteHit(width);row.Offset(StatusShadowMargin,StatusShadowMargin);routeDropdown=new RouteDropdown(NetworkReadonlySnapshot.Load(Path.Combine(Path.GetDirectoryName(path),"network-readonly.json")),Math.Min(344,width),false);routeDropdown.ShowAt(owner,row);}
    public bool MaintainStatusStack(){if(status==null||status.IsDisposed||!status.IsHandleCreated)return false;bool blocked=FullscreenBlocked;if(status.TopMost==blocked)status.TopMost=!blocked;if(form.TopMost==blocked)form.TopMost=!blocked;var foreground=Native.GetForegroundWindow();bool own=foreground==status.Handle||(form.IsHandleCreated&&foreground==form.Handle);bool popup=modal||NetworkDropdownVisible||(status.ContextMenuStrip!=null&&status.ContextMenuStrip.Visible)||(form.ContextMenuStrip!=null&&form.ContextMenuStrip.Visible);if(!statusPresence.Step(PreviewClock.NowMs,foreground,status.Visible,blocked,popup,own))return false;if(form.Visible)form.RestoreTopmostWithoutActivation();var layered=status as NoActivateForm;return layered!=null&&layered.RestoreTopmostWithoutActivation();}
    readonly string path; readonly Layout layout; readonly HoverState state=new HoverState();
    readonly NoActivateForm form=new NoActivateForm(); readonly BufferedFlowPanel cards=new BufferedFlowPanel();
    readonly Panel footer=new Panel();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); readonly GlassActionButton pin=new GlassActionButton();
    readonly GlassActionButton edit=new GlassActionButton(); readonly GlassActionButton add=new GlassActionButton(); readonly GlassActionButton close=new GlassActionButton(); readonly Label title=new Label();
    DateTime? clockSelected, clockDisplay;
    bool hasMediaSnapshot; public int MediaPatchCount { get; private set; }
    public int RebuildCount { get; private set; }
    bool editing, modal; string dragging; Screen active; long suppressedUntil; int columnCapacity=600,baseColumnWidth=344;
    static readonly Color Bg=Color.Fuchsia;static Color Panel {get{return Palette.Base;}}static Color Text {get{return Palette.Text;}}static Color Muted {get{return Palette.Secondary;}}static Color Green {get{return Palette.Accent;}}
    public App(string file, Form linkedStatus = null, NeteaseSource source = null) {
      Legacy=new LegacySnapshot{Source="local-system",LocalOnly=true,State="starting"};StatusPointer=statusPointer;
      mediaSource=source ?? new NeteaseSource();mediaSource.StateChanged+=OnMediaChanged;
      status=linkedStatus;lyricsSource=LrclibSource.FromApprovedConfiguration(Path.Combine(Path.GetDirectoryName(file),"lyrics-connection.json"));
      weatherProvider=OpenMeteoWeatherProvider.FromApprovedConfiguration(Path.Combine(Path.GetDirectoryName(file),"weather-connection.json"));path=file; layout=Load(file);localSource.NetworkSnapshotPath=Path.Combine(Path.GetDirectoryName(file),"network-readonly.json");localSource.BalanceSnapshotPath=Path.Combine(Path.GetDirectoryName(file),"balance-readonly.json");localSource.ActivitySnapshotPath=Path.Combine(Path.GetDirectoryName(file),"agent-activity.json");localSource.BalanceConnectionPath=Path.Combine(Path.GetDirectoryName(file),"balance-connection.json");
      try{var releaseRoot=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(file)));if(File.Exists(Path.Combine(releaseRoot,"FILE-MANIFEST.json"))){normalUpdate=new NormalUpdateHandoff(releaseRoot);normalUpdate.PublishOwner();}}catch{normalUpdate=null;}
      WeatherOnboarding.Apply(layout);Palette.Legacy=layout.LegacyPalette;Palette.Solid=layout.SolidCards;TransitionMetrics.Directory=Path.Combine(Path.GetDirectoryName(file),"logs"); state.Pinned=CurrentLayout.Pinned;
      form.Text="小组件列 · 独立预览"; form.FormBorderStyle=FormBorderStyle.None; form.TopMost=true; form.ShowInTaskbar=false; form.BackColor=Bg; form.TransparencyKey=Bg; form.KeyPreview=true;
      form.FormClosed+=(s,e)=> { Shutdown(); ExitThread(); };
      form.Width=390; form.Height=600;
      title.Text="工作台  ·  预览"; title.Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold); title.ForeColor=Text; title.SetBounds(16,12,145,30);
      SetButton(pin,"固定",170,9,48); SetButton(edit,"编辑",222,9,48); SetButton(add,"添加",274,9,48); SetButton(close,"收起",326,9,48);
      pin.Click+=(s,e)=> { state.Pinned=!state.Pinned; CurrentLayout.Pinned=state.Pinned; Save(); UpdateHeader(); };
      close.Click+=(sender,e)=>CancelEditing();
      edit.Click+=(sender,e)=>SaveEditing();
      var menu=new ContextMenuStrip();addMenu=menu;
      foreach(var id in new[]{"music","shortcuts","clock","weather","autoloop"}) { string key=id; var item=menu.Items.Add(NameOf(id)); item.Click+=(s,e)=> { if(CurrentLayout.Add(key)) { Save(); Rebuild(); } }; }
      add.Click+=(s,e)=>{if(!editing){BeginEditing();return;}menu.Show(add,0,add.Height);}; menu.Opening+=(s,e)=>{ foreach(ToolStripItem i in menu.Items) i.Enabled=!CurrentLayout.Cards.Any(c=>NameOf(c.Id)==i.Text); };
      var context=new ContextMenuStrip();
      var manage=context.Items.Add("\u7BA1\u7406\u5C0F\u7EC4\u4EF6",null,(sender,e)=>{if(editing)SaveEditing();else BeginEditing();});context.Opening+=(sender,e)=>manage.Text=editing?"保存布局并退出编辑":"管理小组件";
      context.Items.Add("\u56FA\u5B9A\u5C55\u5F00",null,(sender,e)=>{state.Pinned=!state.Pinned;CurrentLayout.Pinned=state.Pinned;Save();});
      var reduce=context.Items.Add("\u51CF\u5C11\u52A8\u6001\u6548\u679C") as ToolStripMenuItem;
      reduce.Click+=(sender,e)=>{CurrentLayout.ReduceMotion=!CurrentLayout.ReduceMotion;Save();};
      var solid=context.Items.Add("\u589E\u5F3A\u53EF\u8BFB\u6027") as ToolStripMenuItem;
      solid.Click+=(sender,e)=>{CurrentLayout.SolidCards=!CurrentLayout.SolidCards;Save();};
      context.Opening+=(sender,e)=>{reduce.Checked=CurrentLayout.ReduceMotion;solid.Checked=CurrentLayout.SolidCards;};
      context.Items.Add("\u6536\u8D77",null,(sender,e)=>Collapse());
      context.Items.Add("\u9000\u51FA\u9884\u89C8",null,(sender,e)=>{timer.Stop();form.Close();if(status!=null)status.Close();ExitThread();});form.ContextMenuStrip=context;
      var legacy=context.Items.Add("\u65E7\u914D\u8272\uFF08\u56DE\u9000\uFF09") as ToolStripMenuItem;
      legacy.Click+=(sender,e)=>{CurrentLayout.LegacyPalette=!CurrentLayout.LegacyPalette;Palette.Legacy=CurrentLayout.LegacyPalette;Palette.Solid=CurrentLayout.SolidCards;Save();placement.Invalidate();Rebuild();};
      context.Opening+=(sender,e)=>{legacy.Checked=CurrentLayout.LegacyPalette;};
      footer.BackColor=Bg;
      cards.Location=new Point(0,0); cards.Width=344; cards.Height=530; cards.AutoScroll=true; cards.FlowDirection=FlowDirection.TopDown; cards.WrapContents=false; cards.BackColor=Bg;
      form.Controls.AddRange(new Control[]{title,footer,cards});
      form.KeyDown+=(sender,e)=>{if(e.KeyCode!=Keys.Escape)return;e.Handled=true;foreach(var music in cards.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<MusicCardControl>())if(music.CancelSeek())return;if(menu.Visible){menu.Close();return;}if(dragInput!=null&&dragInput.Armed){dragInput.Cancel();return;}if(editing){CancelEditing();return;}Collapse();};
      form.GeometryContextChanged+=OnGeometryContextChanged;
      var linked=status as NoActivateForm;if(linked!=null)linked.GeometryContextChanged+=OnGeometryContextChanged;
      timer.Interval=16; timer.Tick+=(s,e)=>Tick(menu.Visible); timer.Start();
    }
    static string NameOf(string id) {return id=="autoloop"?"Autoloop":id=="weather"?"天气":id=="status"?"状态台":id=="music"?"网易云音乐":id=="clock"?"时间":id=="calendar"?"日历":"快捷入口";}
    static Layout Load(string path) { return LayoutStore.Load(path); }
    void Save() { if(editing)return; try { LayoutStore.Save(path,layout); } catch { MessageBox.Show("布局保存失败，请检查预览文件夹的写入权限。", "状态台预览"); } }
    void SetButton(Button b,string text,int x,int y,int w) { b.Text=text; b.SetBounds(x,y,w,29); b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderColor=Color.FromArgb(70,80,95); b.BackColor=Color.Transparent; b.ForeColor=Text; b.TabStop=true; footer.Controls.Add(b); }
    void BeginEditing(){if(editing)return;editSession=new LayoutEditSession(layout);editing=true;Rebuild();RecordPlacement("management-open");}
    void CancelEditing(){if(!editing)return;if(dragInput!=null)dragInput.Cancel();editSession.Cancel();editSession=null;editing=false;state.Pinned=layout.Pinned;Rebuild();RecordPlacement("management-cancel");}
    public bool CancelManagement(){if(!editing)return false;CancelEditing();return true;}
    void SaveEditing(){if(!editing)return;if(dragInput!=null)dragInput.Cancel();try{editSession.Save(path);}catch{MessageBox.Show("\u4FDD\u5B58\u5931\u8D25\uFF0C\u8349\u7A3F\u4ECD\u4FDD\u7559\u3002","\u5C0F\u7EC4\u4EF6");return;}editSession=null;editing=false;state.Pinned=layout.Pinned;Rebuild();RecordPlacement("management-save");}
    int FooterHeight {get{return ManagementBarGeometry.Create(baseColumnWidth,editing).Height;}}
    void UpdateHeader(){
      title.Visible=false;pin.Visible=false;add.Visible=true;edit.Visible=editing;close.Visible=editing;
      add.Plus=true;add.Text="+";add.AccessibleName=editing?"\u6DFB\u52A0\u5C0F\u7EC4\u4EF6":"\u7BA1\u7406\u5C0F\u7EC4\u4EF6";
      edit.Text="\u4FDD\u5B58";edit.AccessibleName=edit.Text;close.Text="\u53D6\u6D88";close.AccessibleName=close.Text;
      var bar=ManagementBarGeometry.Create(baseColumnWidth,editing);
      int x=editing?0:Math.Max(0,(baseColumnWidth-bar.Width)/2);
      footer.SetBounds(x,cards.Bottom+8,bar.Width,bar.Height);
      add.Bounds=bar.Add;if(editing){edit.Bounds=bar.Save;close.Bounds=bar.Cancel;}
    }
    void UpdateColumnExtent(){
      int content=WidgetGrid.Height(CurrentLayout.Cards,baseColumnWidth),reserve=FooterHeight+8;
      cards.Height=editing?Math.Max(1,columnCapacity-reserve):Math.Min(Math.Max(1,columnCapacity-reserve),content);form.Height=cards.Height+reserve;
      bool overflow=editing||content>cards.Height;form.Width=baseColumnWidth+(overflow?SystemInformation.VerticalScrollBarWidth:0);cards.Width=form.Width;
      UpdateHeader();
    }
    public static Rectangle WorkbenchBounds(Rectangle w) { int gap=Math.Min(32,Math.Max(0,w.Width/8)); int width=Math.Min(390,Math.Max(1,w.Width-gap)); int height=Math.Min(600,w.Height); return new Rectangle(w.Right-gap-width,w.Top+Math.Min(20,Math.Max(0,w.Height-height)),width,height); }
    public static Rectangle ColumnBounds(Rectangle area) { bool compact=area.Height<780;int top=area.Top+(compact?16:120)+(compact?74:424)+12;top=Math.Min(top,area.Bottom-48);int width=Math.Max(1,Math.Min(344,area.Width-64));return new Rectangle(Math.Max(area.Left,area.Right-50-width),top,width,Math.Max(1,area.Bottom-top-16)); }
    public static bool HoverContains(Point p,Rectangle first,Rectangle second) {if(first.Contains(p)||second.Contains(p))return true;int left=Math.Max(first.Left,second.Left),right=Math.Min(first.Right,second.Right);if(right<=left)return false;int top=Math.Min(first.Bottom,second.Bottom),bottom=Math.Max(first.Top,second.Top);return bottom>top&&new Rectangle(left,top,right-left,bottom-top).Contains(p);}
    public static bool IsExternalEdge(Point p, Rectangle screen, IEnumerable<Rectangle> otherScreens) {
      return screen.Contains(p) && p.X >= screen.Right-2
        && !otherScreens.Any(other => other.Contains(new Point(screen.Right,p.Y)));
    }
    public static bool EdgeBridgeContains(Point p, Rectangle screen, Rectangle first, Rectangle second) {
      var surfaces = new[]{first,second}.Where(r=>r.Width>0 && r.Height>0).ToArray();
      if(surfaces.Length==0 || !screen.Contains(p)) return false;
      int left=surfaces.Min(r=>r.Right), top=surfaces.Min(r=>r.Top), bottom=surfaces.Max(r=>r.Bottom);
      return p.X>=left && p.X<screen.Right && p.Y>=top && p.Y<bottom;
    }
    readonly PlacementKey placement=new PlacementKey();
    void OnGeometryContextChanged(object sender,EventArgs e){placement.Invalidate();}
    Rectangle StatusSurface(){return status==null||StatusHidden?Rectangle.Empty:CornerLayout.Body(TargetArea,StatusBodyHeight,StatusShadowMargin,SystemInformation.VerticalScrollBarWidth);}
    Rectangle AnchoredBounds(Screen s){return CornerLayout.Column(s.WorkingArea,StatusShadowMargin,SystemInformation.VerticalScrollBarWidth,StatusBodyHeight);}
    void ApplyCardOpacity(){double opacity=reveal.Amount;if(form.Opacity!=opacity)form.Opacity=opacity;}
    public void PrepareInitialLayout(Screen s){
      active=s;var bounds=AnchoredBounds(s);
      if(placement.Matches(s.DeviceName,s.WorkingArea,form.DeviceDpi)){
        // The transition owns location; resetting it here causes every-frame snapping.
        if(columnCapacity!=bounds.Height){columnCapacity=bounds.Height;UpdateColumnExtent();}
        ApplyCardOpacity();return;
      }
      Place(s);Rebuild();ApplyCardOpacity();placement.Commit(s.DeviceName,s.WorkingArea,form.DeviceDpi);
    }
    void Place(Screen s){var bounds=AnchoredBounds(s);form.Bounds=bounds;columnCapacity=bounds.Height;baseColumnWidth=bounds.Width;cards.Width=form.Width;cards.Height=Math.Max(1,columnCapacity-FooterHeight-8);}
    static int CardWidth(Card c){return WidgetGrid.Small(c)?166:344;}
    static int CardHeight(Card c) {if(WidgetGrid.Small(c))return 166;if(c.Id=="calendar")return 380;return c.Id=="clock"?380:c.Id=="music"?208:c.Id=="weather"?176:c.Id=="autoloop"?176:(c.Size=="小"?112:c.Size=="大"?192:132);}
    readonly object evidenceLock=new object();Rectangle evidenceStatus,evidenceColumn;long evidenceDue;bool evidencePending;
    static string JsonString(string value){return "\""+(value??"").Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
    static string JsonRect(Rectangle r){return "{\"x\":"+r.X+",\"y\":"+r.Y+",\"width\":"+r.Width+",\"height\":"+r.Height+"}";}
    void RecordPlacement(string phase){
      if(shuttingDown)return;var screen=active??Screen.PrimaryScreen;
      var entries=new List<string>();foreach(Control control in cards.Controls)entries.Add("{\"id\":"+JsonString(control.Tag as string)+",\"local\":"+JsonRect(control.Bounds)+",\"screen\":"+JsonRect(new Rectangle(form.Left+cards.Left+control.Left,form.Top+cards.Top+control.Top,control.Width,control.Height))+"}");
      string json="{\"candidate\":\"sidebar-merged-ready-20261106\",\"phase\":"+JsonString(phase)+",\"utc\":"+JsonString(DateTime.UtcNow.ToString("o"))+",\"units\":\"process-coordinate-space\",\"device\":"+JsonString(screen.DeviceName)+",\"screen\":"+JsonRect(screen.Bounds)+",\"workArea\":"+JsonRect(screen.WorkingArea)+",\"columnDpi\":"+form.DeviceDpi+",\"statusDpi\":"+(status==null?0:status.DeviceDpi)+",\"statusOuter\":"+JsonRect(status==null?Rectangle.Empty:status.Bounds)+",\"statusSurface\":"+JsonRect(StatusSurface())+",\"column\":"+JsonRect(form.Bounds)+",\"footer\":"+JsonRect(footer.Bounds)+",\"management\":"+editing.ToString().ToLowerInvariant()+",\"cards\":["+String.Join(",",entries)+"]}";
      json=json.Substring(0,json.Length-1)+",\"anchorPolicy\":\"workarea-top-right\",\"columnOpacity\":"+form.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"highlightEnabled\":"+HighlightEnabled.ToString().ToLowerInvariant()+",\"reduceMotion\":"+ReducedMotion.ToString().ToLowerInvariant()+",\"highlightSequence\":"+HighlightSequence+",\"statusDetailOpen\":"+StatusDetailOpen.ToString().ToLowerInvariant()+"}";
      string directory=Path.Combine(Path.GetDirectoryName(path),"logs");
      Task.Run(()=>{try{lock(evidenceLock){Directory.CreateDirectory(directory);string target=Path.Combine(directory,"placement-latest.json"),temp=target+".tmp";File.WriteAllText(temp,json);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}}catch{}});
    }
    void ObserveSettledPlacement(long now){if(!form.Visible)return;var surface=StatusSurface();if(surface!=evidenceStatus||form.Bounds!=evidenceColumn){evidenceStatus=surface;evidenceColumn=form.Bounds;evidencePending=true;evidenceDue=now+250;}if(evidencePending&&now>=evidenceDue){evidencePending=false;RecordPlacement("settled");}}
    void Tick(bool menuOpen) {
      if(shuttingDown)return;
      if(normalUpdate!=null){normalUpdate.Tick(editing||modal||NetworkDropdownVisible||dragging!=null||dragPreview!=null||menuOpen||(form.ContextMenuStrip!=null&&form.ContextMenuStrip.Visible)||(status!=null&&status.ContextMenuStrip!=null&&status.ContextMenuStrip.Visible),()=>LayoutStore.Save(path,layout),()=>{if(status!=null){status.Close();if(status.Visible)throw new InvalidOperationException("Normal status close was cancelled");}else{form.Close();if(form.Visible)throw new InvalidOperationException("Normal column close was cancelled");}});if(normalUpdate.Closing)return;}

      Point p=Cursor.Position; Screen s=Screen.FromPoint(p); long now=PreviewClock.NowMs; bool down=Native.ButtonsDown();
      PollLegacy(now);
      if(form.Visible)RefreshWeather();
      if(active!=null)active=Screen.AllScreens.FirstOrDefault(item=>item.DeviceName==active.DeviceName)??s;
      bool wasOpen=state.Open;
      bool externalEdge = IsExternalEdge(p,s.Bounds,Screen.AllScreens.Where(other=>other.DeviceName!=s.DeviceName).Select(other=>other.Bounds));
      bool edge=externalEdge && !down && now>=suppressedUntil;

      bool inside=(state.Open || reveal.Amount>0) && ((status!=null ? HoverContains(p,status.Bounds,form.Bounds) : form.Bounds.Contains(p)) || menuOpen || form.ContextMenuStrip.Visible || modal || editing);
      inside = inside || (state.Open && status!=null && status.ContextMenuStrip!=null && status.ContextMenuStrip.Visible);
      inside = inside || (state.Open && EdgeBridgeContains(p,(active ?? s).Bounds,status!=null?status.Bounds:Rectangle.Empty,form.Bounds));
      bool blocked=Native.Fullscreen(s) && !form.ContainsFocus;
      state.Tick(now,edge,inside,blocked);
      if(!blocked&&edgeWake.Tick(now,edge,wasOpen,state.Open,StatusHidden))EdgeRevealRequested=true;
      if(state.Open&&!wasOpen)active=s;
      // Status detail is part of the same hover region; it moves cards rather than dismissing them.
      bool show=state.Open&&!blocked;
      PresentReveal(show,active??s,now);
      if(mediaRefreshPolicy.Due(now,form.Visible,mediaRefreshing,DateTime.UtcNow<mediaFastUntil))RefreshMedia();
      bool enabled=!ReducedMotion&&!blocked&&!modal&&dragging==null&&!editing&&!reveal.Moving;
      HighlightEnabled=enabled;
      var surface=StatusSurface();var local=new PointF(p.X-surface.Left,p.Y-surface.Top);
      if(statusPointer.Tick(now,local,status!=null&&status.Visible&&surface.Contains(p),enabled&&status!=null&&status.Visible))HighlightSequence++;
      foreach(Control item in cards.Controls){var panel=item as WidgetPanel;if(panel!=null)panel.UpdatePointer(now,p,enabled&&form.Visible);}
      ObserveSettledPlacement(now);
    }
    void PresentReveal(bool show,Screen screen,long now){
      TransitionMetrics.MarkTick("column",now);
      var preparation=System.Diagnostics.Stopwatch.StartNew();
      if(show)PrepareInitialLayout(screen);
      if(show&&!form.Visible&&reveal.Amount==0){
        // Native creation/first paint must finish at zero alpha BEFORE the motion clock starts.
        var destination=AnchoredBounds(screen);
        form.Location=new Point(destination.Left+reveal.Offset(form.DeviceDpi),destination.Top);
        form.Opacity=0;form.Show();form.Update();
        now+=preparation.ElapsedMilliseconds;
      }
      reveal.Target(show,now,ReducedMotion);reveal.Advance(now);
      if(reveal.Moving)TransitionMetrics.MarkTick("reveal-transition",now);
      if(show||reveal.Amount>0){
        var destination=AnchoredBounds(screen);
        var next=new Point(destination.Left+reveal.Offset(form.DeviceDpi),destination.Top);
        if(form.Location!=next)form.Location=next;
        ApplyCardOpacity();
        if(!form.Visible)form.Show();
      }else if(form.Visible){form.Hide();if(editing){editSession.Cancel();editSession=null;editing=false;state.Pinned=layout.Pinned;placement.Invalidate();}}
      ObserveReveal(now);
    }
    public static string TestNativeStatusFlow(string evidenceDirectory){
      Directory.CreateDirectory(evidenceDirectory);
      var app=new App(Path.Combine(evidenceDirectory,"flow-fixture-layout.xml"));app.timer.Stop();
      int shows=0,hides=0;var samples=new List<string>();
      app.form.VisibleChanged+=(s,e)=>{if(app.form.Visible)shows++;else hides++;};
      try{
        var screen=Screen.PrimaryScreen;
        app.PrepareInitialLayout(screen);
        for(int i=0;i<25;i++){app.PresentReveal(true,screen,PreviewClock.NowMs);Application.DoEvents();System.Threading.Thread.Sleep(16);}
        int rebuilds=app.RebuildCount,cards=app.cards.Controls.Count;
        int[] heights={74,196,276,328,362,384,400,424,350,424,196,74,424,74,50,20,1,0,0,20,50,74,0,74};
        foreach(int height in heights){
          app.StatusHidden=height==0;app.StatusBodyHeight=height;app.StatusDetailOpen=height>74;
          app.PresentReveal(true,screen,PreviewClock.NowMs);Application.DoEvents();
          var expected=CornerLayout.Column(screen.WorkingArea,app.StatusShadowMargin,SystemInformation.VerticalScrollBarWidth,height);
          Native.RECT native;Native.GetWindowRect(app.form.Handle,out native);
          if(!app.form.Visible||app.reveal.Amount!=1||app.form.Opacity!=1||native.Top!=expected.Top||native.Bottom>screen.WorkingArea.Bottom-app.StatusShadowMargin||app.RebuildCount!=rebuilds||app.cards.Controls.Count!=cards||!app.cards.AutoScroll)throw new InvalidOperationException("Native status-flow geometry, visibility or control reuse failed: height="+height+" visible="+app.form.Visible+" amount="+app.reveal.Amount+" opacity="+app.form.Opacity+" top="+native.Top+" expected="+expected.Top+" bottom="+native.Bottom+" limit="+(screen.WorkingArea.Bottom-app.StatusShadowMargin)+" rebuilds="+app.RebuildCount+" prior="+rebuilds+" cards="+app.cards.Controls.Count+" priorCards="+cards);
          samples.Add("{\"statusHeight\":"+height+",\"columnTop\":"+native.Top+",\"columnHeight\":"+(native.Bottom-native.Top)+",\"capacity\":"+app.columnCapacity+"}");
        }
        app.columnCapacity=96;app.UpdateColumnExtent();Application.DoEvents();
        if(!app.cards.VerticalScroll.Visible||app.cards.Controls.Count!=cards||app.form.Height>96)throw new InvalidOperationException("Short-work-area scroll constraint failed");
        if(shows!=1||hides!=0)throw new InvalidOperationException("Status expansion dismissed the column");
        return "{\"schema\":1,\"shows\":1,\"hidesDuringStatusChanges\":0,\"layoutRebuildsDuringStatusChanges\":0,\"cardsPreserved\":true,\"shortViewportScrollVerified\":true,\"samples\":["+String.Join(",",samples)+"]}";
      }finally{app.Shutdown();}
    }
    public static string TestNativeReveal(string evidenceDirectory){
      Directory.CreateDirectory(evidenceDirectory);
      var app=new App(Path.Combine(evidenceDirectory,"fixture-layout.xml"));app.timer.Stop();
      int shows=0,hides=0,rebuilds=0;var trace=new List<string>();var watch=System.Diagnostics.Stopwatch.StartNew();
      app.form.VisibleChanged+=(s,e)=>{if(app.form.Visible)shows++;else hides++;};
      try{
        app.PrepareInitialLayout(Screen.PrimaryScreen);rebuilds=app.RebuildCount;
        bool monotonic=true;int previousX=Int32.MaxValue;
        for(int i=0;i<40;i++){
          long now=watch.ElapsedMilliseconds;bool open=now<350;
          app.PresentReveal(open,Screen.PrimaryScreen,now);
          if(open){if(app.form.Left>previousX)monotonic=false;previousX=app.form.Left;}
          trace.Add("{\"ms\":"+now+",\"visible\":"+app.form.Visible.ToString().ToLowerInvariant()+",\"x\":"+app.form.Left+",\"opacity\":"+app.form.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
          Application.DoEvents();System.Threading.Thread.Sleep(16);
        }
        if(shows!=1||hides!=1||app.form.Visible||!monotonic||app.RebuildCount!=rebuilds)throw new InvalidOperationException("Native reveal lifecycle or reuse failed");
        return "{\"schema\":1,\"shows\":"+shows+",\"hides\":"+hides+",\"layoutRebuildsDuringAnimation\":"+(app.RebuildCount-rebuilds)+",\"visibleAtEnd\":false,\"trace\":["+String.Join(",",trace)+"]}";
      }finally{app.Shutdown();}
    }
    bool revealWasMoving;int revealSamples;long revealStarted;
    readonly List<string> revealTrace=new List<string>();
    void ObserveReveal(long now){
      if(reveal.Moving&&!revealWasMoving){revealStarted=now;revealSamples=0;revealTrace.Clear();}
      if(reveal.Moving||revealWasMoving){revealSamples++;revealTrace.Add("{\"ms\":"+(now-revealStarted)+",\"amount\":"+reveal.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"x\":"+form.Left+",\"opacity\":"+form.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");}
      if(!reveal.Moving&&revealWasMoving){string json="{\"schema\":1,\"candidate\":\"sidebar-merged-ready-20261106\",\"samples\":"+revealSamples+",\"durationMs\":"+(now-revealStarted)+",\"open\":"+reveal.TargetOpen.ToString().ToLowerInvariant()+",\"trace\":["+String.Join(",",revealTrace)+"]}";Task.Run(()=>{try{File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"logs/reveal-latest.json"),json);}catch{}});}
      revealWasMoving=reveal.Moving;
    }
    public static bool SameMediaPresentation(Snapshot a,Snapshot b) {
      return a!=null&&b!=null&&a.SourceId==b.SourceId&&a.Title==b.Title&&a.Artist==b.Artist&&a.Album==b.Album&&a.Detail==b.Detail&&a.Playing==b.Playing&&a.CanToggle==b.CanToggle&&a.CanPrevious==b.CanPrevious&&a.CanNext==b.CanNext&&a.DurationSeconds==b.DurationSeconds&&a.PositionSeconds==b.PositionSeconds&&a.PositionTimestamp==b.PositionTimestamp
        && (ReferenceEquals(a.Cover,b.Cover)||(a.Cover!=null&&b.Cover!=null&&a.Cover.SequenceEqual(b.Cover)));
    }
    void OnMediaChanged(){if(!shuttingDown)mediaRefreshPolicy.Request();}
    async void RefreshMedia() {
      if(shuttingDown||form.IsDisposed)return;if(mediaRefreshing){mediaRefreshPolicy.Request();return;}
      mediaRefreshing=true; mediaChecked=DateTime.UtcNow;
      try {var snapshot=await mediaSource.ReadAsync();if(!shuttingDown&&!form.IsDisposed)ApplyMediaSnapshot(snapshot);}
      catch {if(!shuttingDown&&!form.IsDisposed)ApplyMediaSnapshot(new Snapshot { Detail="系统媒体不可用" });}
      finally {mediaRefreshing=false;mediaRefreshPolicy.Completed(PreviewClock.NowMs);}
    }
    void ApplyMediaSnapshot(Snapshot snapshot) {var visual=musicPresentation.Present(snapshot);if(mediaTrace!=null&&mediaTrace.Observed==0&&((mediaTrace.Action=="toggle"&&snapshot.Playing!=mediaBeforePlaying)||(mediaTrace.Action!="toggle"&&snapshot.Title+"\n"+snapshot.Artist!=mediaBeforeTrack)))mediaTrace.Observed=snapshot.ObservedTicks>0?snapshot.ObservedTicks:System.Diagnostics.Stopwatch.GetTimestamp(); RefreshLyrics(snapshot);
      if(hasMediaSnapshot&&SameMediaPresentation(media,snapshot)){media=snapshot;foreach(Control panel in cards.Controls)foreach(Control item in panel.Controls){var card=item as MusicCardControl;if(card!=null)card.Present(visual,artwork);}return;}
      bool sameCover=ReferenceEquals(media.Cover,snapshot.Cover)||(media.Cover!=null&&snapshot.Cover!=null&&media.Cover.SequenceEqual(snapshot.Cover));
      Image previous=artwork,next=artwork;
      if(!sameCover){next=null;if(snapshot.Cover!=null){try{using(var stream=new MemoryStream(snapshot.Cover))using(var image=Image.FromStream(stream))if(image.Width<=4096&&image.Height<=4096&&(long)image.Width*image.Height<=16777216)next=new Bitmap(image);}catch{}}}
      media=snapshot;hasMediaSnapshot=true;artwork=next;MediaPatchCount++;
      foreach(Control panel in cards.Controls){if((string)panel.Tag!="music")continue;
        foreach(Control item in panel.Controls){
          var musicCard=item as MusicCardControl;if(musicCard!=null){musicCard.Present(visual,artwork);musicCard.PresentLyrics(currentLyrics);continue;}
          var label=item as Label;
          if(label!=null&&(string)label.Tag=="media-track"){string text=String.IsNullOrEmpty(media.SourceId)?media.Detail:media.Title+"\n"+(String.IsNullOrEmpty(media.Artist)?media.Detail:media.Artist);if(label.Text!=text)label.Text=text;}
          if(label!=null&&(string)label.Tag=="media-cover")foreach(Control child in label.Controls){var picture=child as PictureBox;if(picture!=null){if(!ReferenceEquals(picture.Image,artwork))picture.Image=artwork;picture.Visible=artwork!=null;}}
          var button=item as MediaControlButton;if(button!=null){bool enabled=button.Action=="toggle"?media.CanToggle:button.Action=="previous"?media.CanPrevious:media.CanNext;bool changed=button.Playing!=media.Playing||button.Enabled!=enabled;button.Playing=media.Playing;button.Enabled=enabled;button.AccessibleName=button.Action=="toggle"?(media.Playing?"暂停":"播放"):button.Action=="previous"?"上一首":"下一首";if(changed)button.Invalidate();}
        }
      }
      if(mediaTrace!=null&&mediaTrace.Observed>0&&mediaTrace.Painted==0){mediaTrace.Painted=System.Diagnostics.Stopwatch.GetTimestamp();RecordMediaTrace();}if(previous!=null&&!ReferenceEquals(previous,next))previous.Dispose();
    }
    void RecordMediaTrace(){var trace=mediaTrace;if(trace==null)return;var text=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{utc=DateTimeOffset.UtcNow.ToString("o"),action=trace.Action,outcome=trace.Outcome,clickToDispatchMs=trace.ClickToDispatch,dispatchToAckMs=trace.DispatchToAck,clickToObservedMs=trace.ClickToObserved,observedToUiMs=trace.ObservedToUi,realStateObserved=trace.Observed>0,songMetadataLogged=false});Task.Run(()=>{try{var dir=Path.Combine(Path.GetDirectoryName(path),"logs");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"media-control-latest.json"),text);}catch{}});}
    void PresentLyrics(LyricResult value){currentLyrics=value;foreach(Control panel in cards.Controls)foreach(Control item in panel.Controls){var music=item as MusicCardControl;if(music!=null)music.PresentLyrics(value);}}
    async void RefreshLyrics(Snapshot song){if(shuttingDown)return;string key=CurrentLayout.Cards.Any(c=>c.Id=="music")?LyricMatcher.Key(song):"";if(key==lyricsKey&&(lyricsPending||DateTime.UtcNow<lyricsRetryAt))return;lyricsKey=key;int sequence=++lyricsSequence;if(lyricsCancellation!=null){lyricsCancellation.Cancel();lyricsCancellation.Dispose();}lyricsCancellation=new System.Threading.CancellationTokenSource();var token=lyricsCancellation.Token;lyricsPending=true;lyricsRetryAt=DateTime.MaxValue;PresentLyrics(new LyricResult(!lyricsSource.Enabled?"disabled":key.Length==0?"no-track":"loading"));if(!lyricsSource.Enabled||key.Length==0){lyricsPending=false;return;}try{var result=await lyricsSource.ReadAsync(song,token);if(!shuttingDown&&sequence==lyricsSequence&&!form.IsDisposed){PresentLyrics(result);lyricsRetryAt=result.State=="ready"||result.State=="plain"||result.State=="instrumental"?DateTime.MaxValue:result.State=="unavailable"?DateTime.UtcNow.AddSeconds(60):DateTime.UtcNow.AddMinutes(15);}}catch(OperationCanceledException){}catch{if(!shuttingDown&&sequence==lyricsSequence){PresentLyrics(new LyricResult("unavailable"));lyricsRetryAt=DateTime.UtcNow.AddSeconds(60);}}finally{if(sequence==lyricsSequence)lyricsPending=false;}}
    IWeatherProvider weatherProvider;WeatherSnapshot weatherSnapshot=new WeatherSnapshot();bool weatherPending;DateTime weatherChecked=DateTime.MinValue;
    void RecordWeatherStatus(WeatherSnapshot data,int presented){try{var now=DateTimeOffset.UtcNow;string json=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{schema=1,utc=now.ToString("o"),state=data.EffectiveState(now),failure=data.FailureCode,httpStatus=data.HttpStatus,hasTemperature=data.Temperature.HasValue,presentedCards=presented});string file=Path.Combine(Path.GetDirectoryName(path),"logs","weather-status-latest.json");Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,json,new System.Text.UTF8Encoding(false));}catch{}}
    void ShowWeatherResult(WeatherSnapshot data){weatherSnapshot=data;int presented=0;foreach(Control panel in cards.Controls)foreach(Control child in panel.Controls){var weather=child as WeatherCardControl;if(weather!=null){weather.Present(data);presented++;}}var now=DateTimeOffset.UtcNow;var delay=WeatherRefreshSchedule.Delay(data,now);if(delay>TimeSpan.FromDays(1))delay=TimeSpan.FromDays(1);weatherChecked=DateTime.UtcNow.Add(delay).AddMinutes(-2);RecordWeatherStatus(data,presented);}
    async void RefreshWeather(){if(shuttingDown||weatherPending||(DateTime.UtcNow-weatherChecked).TotalMinutes<2)return;var card=CurrentLayout.Cards.FirstOrDefault(c=>c.Id=="weather");if(card==null)return;weatherPending=true;weatherChecked=DateTime.UtcNow;try{var data=await WeatherSource(card).ReadAsync(WeatherCity(card),System.Threading.CancellationToken.None);if(!shuttingDown)ShowWeatherResult(data);}catch{if(!shuttingDown)ShowWeatherResult(new WeatherSnapshot{City=WeatherCity(card),State="unavailable"});}finally{weatherPending=false;}}
    string WeatherCity(Card card){var live=weatherProvider as OpenMeteoWeatherProvider;return String.IsNullOrWhiteSpace(card.CityName)&&live!=null?live.Location.Name:card.CityName;}
    IWeatherProvider WeatherSource(Card card){var live=weatherProvider as OpenMeteoWeatherProvider;if(live!=null&&card.WeatherLatitude.HasValue&&card.WeatherLongitude.HasValue){var place=new WeatherPlace{Name=card.CityName,Latitude=card.WeatherLatitude.Value,Longitude=card.WeatherLongitude.Value};if(place.Valid)return live.ForPlace(place);}return weatherProvider;}
    void OpenWeatherDetails(Card card){if(modal||editing)return;modal=true;try{var source=WeatherSource(card);var city=WeatherCity(card);using(var detail=new WeatherDetailForm(city,source,weatherSnapshot.City==city?weatherSnapshot:new WeatherSnapshot{City=city,State=WeatherSnapshot.ValidCity(city)?"not-connected":"unconfigured"})){detail.HomeTextChanged+=cityName=>{var copy=LayoutEditSession.Clone(layout);var chosen=copy.Cards.First(c=>c.Id=="weather");chosen.CityName=cityName;chosen.WeatherLatitude=null;chosen.WeatherLongitude=null;LayoutStore.Save(path,copy);layout.Cards=copy.Cards;weatherSnapshot=new WeatherSnapshot{City=cityName,State="not-connected"};weatherChecked=DateTime.MinValue;Rebuild();};detail.HomeChanged+=(place,data)=>{var copy=LayoutEditSession.Clone(layout);var chosen=copy.Cards.First(c=>c.Id=="weather");chosen.CityName=place.Name;chosen.WeatherLatitude=place.Latitude;chosen.WeatherLongitude=place.Longitude;LayoutStore.Save(path,copy);layout.Cards=copy.Cards;var live=source as OpenMeteoWeatherProvider;if(live!=null)weatherProvider=live.ForPlace(place);weatherSnapshot=data;weatherChecked=DateTime.UtcNow;Rebuild();};detail.ShowDialog(form);}}finally{modal=false;}}
    void ChooseWeatherCity(Card card){if(modal||editing)return;modal=true;try{using(var dialog=new Form{Text="选择天气城市",ClientSize=new Size(350,150),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})using(var input=new TextBox{Text=card.CityName,MaxLength=60,Bounds=new Rectangle(18,43,314,27)})using(var label=new Label{Text="只填写城市；不会读取精确位置或自动联网",Bounds=new Rectangle(18,14,314,24)})using(var error=new Label{Bounds=new Rectangle(18,74,314,20),ForeColor=Color.Firebrick})using(var save=new Button{Text="保存",Bounds=new Rectangle(176,108,74,28)})using(var cancel=new Button{Text="取消",Bounds=new Rectangle(258,108,74,28),DialogResult=DialogResult.Cancel}){dialog.Controls.AddRange(new Control[]{label,input,error,save,cancel});dialog.CancelButton=cancel;dialog.AcceptButton=save;save.Click+=(sender,e)=>{if(!WeatherSnapshot.ValidCity(input.Text.Trim())){error.Text="请输入有效的城市名称";return;}dialog.DialogResult=DialogResult.OK;};if(dialog.ShowDialog(form)==DialogResult.OK){card.CityName=input.Text.Trim();weatherSnapshot=new WeatherSnapshot{City=card.CityName,State="not-connected"};weatherChecked=DateTime.MinValue;Save();Rebuild();}}}finally{modal=false;}}
    Point lastDragPoint=new Point(Int32.MinValue/2,Int32.MinValue/2);
    DragPreview dragPreview;int dragOvers,dragDrops;
    void EndDragPreview(){if(dragPreview!=null){cards.Controls.Remove(dragPreview);dragPreview.Dispose();dragPreview=null;}}
    CardDragInput dragInput;List<Card> dragOriginal;DragInsertionPlan dragPlan;int dragReorders;Point dragGrab,dragPointer;int dragDpi;
    Dictionary<string,Rectangle> VisualDragPlan(){var p=WidgetGrid.Arrange(CurrentLayout.Cards,baseColumnWidth);var offset=cards.AutoScrollPosition;return p.ToDictionary(x=>x.Key,x=>{var r=x.Value;r.Offset(offset);return r;});}
    void BindDrag(Control panel,string id){if(dragInput==null)dragInput=new CardDragInput(BeginCardDrag,MoveCardDrag,FinishCardDrag);dragInput.Bind(panel,id);}
    void BeginCardDrag(string id,Point screen){if(!editing||modal||shuttingDown)throw new InvalidOperationException("Drag requires edit mode");var panel=cards.Controls.Cast<Control>().First(c=>String.Equals(c.Tag as string,id,StringComparison.Ordinal));dragOriginal=new List<Card>(CurrentLayout.Cards);dragging=id;dragOvers=dragDrops=0;dragGrab=panel.PointToClient(screen);dragPlan=new DragInsertionPlan(CurrentLayout.Cards,id,baseColumnWidth,dragGrab);dragReorders=0;dragPointer=cards.PointToClient(screen);lastDragPoint=dragPointer;dragDpi=form.DeviceDpi;StartDragPreview(id);}
    void MoveCardDrag(Point screen){if(dragging==null)return;if(!editing||form.DeviceDpi!=dragDpi){dragInput.Cancel();return;}dragPointer=cards.PointToClient(screen);if(cards.ClientRectangle.Contains(dragPointer)){int max=Math.Max(0,WidgetGrid.Height(CurrentLayout.Cards,baseColumnWidth)-cards.ClientSize.Height),scroll=-cards.AutoScrollPosition.Y;if(dragPointer.Y<28)scroll=Math.Max(0,scroll-16);else if(dragPointer.Y>cards.ClientSize.Height-28)scroll=Math.Min(max,scroll+16);if(scroll!=-cards.AutoScrollPosition.Y)cards.AutoScrollPosition=new Point(0,scroll);if(Math.Abs(dragPointer.X-lastDragPoint.X)+Math.Abs(dragPointer.Y-lastDragPoint.Y)>=6){lastDragPoint=dragPointer;var offset=cards.AutoScrollPosition;var logical=new Point(dragPointer.X-offset.X,dragPointer.Y-offset.Y);dragOvers++;if(dragPlan!=null&&dragPlan.Update(logical)){dragPlan.Apply(CurrentLayout);dragReorders++;}}}if(dragPreview!=null)dragPreview.Present(VisualDragPlan(),dragPointer);}
    void FinishCardDrag(bool commit){if(dragging==null)return;bool accepted=commit&&cards.ClientRectangle.Contains(dragPointer);string id=dragging;if(!accepted&&dragOriginal!=null)CurrentLayout.Cards=dragOriginal;else dragDrops++;dragOriginal=null;dragging=null;dragPlan=null;var finishing=dragPreview;if(accepted&&finishing!=null&&!ReducedMotion&&!shuttingDown&&!form.IsDisposed){finishing.Settled+=(sender,e)=>{if(ReferenceEquals(dragPreview,finishing)){EndDragPreview();if(!shuttingDown&&!form.IsDisposed)ReflowCards();}};finishing.Release(VisualDragPlan());}else{EndDragPreview();if(!shuttingDown&&!form.IsDisposed)ReflowCards();}string result=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{source=id,committedToDraft=accepted,overEvents=dragOvers,reorderChanges=dragReorders,dropEvents=dragDrops,scope="stable insertion geometry and hysteresis; layout persists only on Save"});Task.Run(()=>{try{var folder=Path.Combine(Path.GetDirectoryName(path),"logs");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"drag-gesture-latest.json"),result);}catch{}});}

    void StartDragPreview(string id){EndDragPreview();dragPreview=new DragPreview(cards.Controls.Cast<Control>().ToArray(),id){Bounds=cards.ClientRectangle,BackColor=cards.BackColor,ReducedMotion=ReducedMotion};dragPreview.SetGrab(dragging=="fixture"?new Point(60,18):dragGrab);cards.Controls.Add(dragPreview);dragPreview.BringToFront();dragPreview.Present(VisualDragPlan(),dragging=="fixture"?new Point(80,100):dragPointer);}
    void ReflowCards(){if(dragPreview!=null){dragPreview.Present(VisualDragPlan(),dragging=="fixture"?new Point(80,100):dragPointer);return;}var watch=System.Diagnostics.Stopwatch.StartNew();var plan=WidgetGrid.Arrange(CurrentLayout.Cards,baseColumnWidth);var scroll=cards.AutoScrollPosition;cards.SuspendLayout();foreach(Control item in cards.Controls){Rectangle bounds;if(plan.TryGetValue(item.Tag as string,out bounds)){bounds.Offset(scroll);if(item.Bounds!=bounds)item.Bounds=bounds;}}var extent=new Size(baseColumnWidth,WidgetGrid.Height(CurrentLayout.Cards,baseColumnWidth));if(cards.AutoScrollMinSize!=extent)cards.AutoScrollMinSize=extent;cards.ResumeLayout();if(dragging==null)UpdateColumnExtent();TransitionMetrics.Record("grid-layout",watch.Elapsed.TotalMilliseconds);}
    void Rebuild() {
        EndDragPreview();
      Palette.Legacy=CurrentLayout.LegacyPalette;
      RebuildCount++; UpdateHeader(); form.SuspendLayout(); cards.SuspendLayout(); while(cards.Controls.Count>0) { var old=cards.Controls[0];
        foreach(Control child in old.Controls) { var clock=child as ClockCalendarControl; if(clock!=null) { clockSelected=clock.SelectedDate; clockDisplay=clock.DisplayMonth; } }
        cards.Controls.RemoveAt(0); old.Dispose(); }
      UpdateColumnExtent();
      foreach(Card c in CurrentLayout.Cards) {
        var panel=new WidgetPanel { Width=CardWidth(c), Height=CardHeight(c), BackColor=Panel, Margin=new Padding(0,0,0,12), AllowDrop=editing, Tag=c.Id };
        var head=new Label { BackColor=Color.Transparent, Text=(c.Id=="music"?"网易云":NameOf(c.Id)), ForeColor=Text, Font=new Font("Microsoft YaHei UI",13,FontStyle.Bold,GraphicsUnit.Pixel), AutoSize=false, Location=new Point(14,7), Size=new Size(panel.Width-(editing?90:28),25), Tag=c.Id, Cursor=editing?Cursors.SizeAll:Cursors.Default };
        panel.Controls.Add(head);
        var body=new Label { BackColor=Color.Transparent, ForeColor=Muted, Font=new Font("Microsoft YaHei UI",9), Location=new Point(14,45), Size=new Size(panel.Width-28,panel.Height-52), Text=c.Id=="status"?"离线预览 · 数据不可用\n\nGPU / 显存    —\nCodex / Claude    —":c.Id=="music"?"系统媒体尚未接入\n\n播放信息和控制不可用":String.IsNullOrEmpty(c.ShortcutPath)?"添加你常用的应用":"已配置："+Path.GetFileName(c.ShortcutPath) };
        panel.Controls.Add(body);
        if(c.Id=="clock"||c.Id=="calendar") {
          body.Visible=false; head.Visible=false;
          var mini=new MiniClockCalendar(c.SizeMode!="small"){Editing=editing,Dock=DockStyle.Fill,Enabled=!editing};
          mini.RestoreView(c.SelectedDate,c.DisplayMonth);mini.ViewChanged+=(sender,e)=>{c.SelectedDate=mini.SelectedDate.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);c.DisplayMonth=mini.DisplayMonth.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);if(!editing)Save();};panel.Controls.Add(mini);mini.SendToBack();
          if(editing) head.BringToFront();
        }
        if(c.Id=="music") {
          body.Visible=false;head.Visible=false;
          var musicCard=new MusicCardControl{ReduceActivationMotion=()=>ReducedMotion,Square=c.MusicMode=="square",Editing=editing,Dock=DockStyle.Fill};musicCard.Present(musicPresentation.Present(media),artwork);musicCard.PresentLyrics(currentLyrics);
          musicCard.ActivationRequested+=(sender,e)=>{try{if(!NeteaseWindow.Open())musicCard.ReportActivationFailure();}catch{musicCard.ReportActivationFailure();}};
          musicCard.SeekRequested+=async(seconds,identity)=>{musicCard.SetBusy(true);mediaFastUntil=DateTime.UtcNow.AddSeconds(4);try{bool observed=await mediaSource.SeekAsync(seconds,identity);if(!shuttingDown&&!form.IsDisposed){RefreshMedia();if(!observed)musicCard.AccessibleDescription="播放器未确认跳转；保留真实播放位置。";}}finally{if(!musicCard.IsDisposed)musicCard.SetBusy(false);}};
          musicCard.CommandRequested+=async action=>{long click=System.Diagnostics.Stopwatch.GetTimestamp();musicCard.SetBusy(true);mediaBeforePlaying=media.Playing;mediaBeforeTrack=media.Title+"\n"+media.Artist;mediaFastUntil=DateTime.UtcNow.AddSeconds(3);try{var command=Task.Run(()=>mediaSource.ControlAsync(action,click));RefreshMedia();bool accepted=await command;mediaTrace=mediaSource.LastControlTrace;if(!shuttingDown&&!form.IsDisposed){RefreshMedia();RecordMediaTrace();if(!accepted)musicCard.AccessibleDescription="\u64cd\u4f5c\u672a\u786e\u8ba4\uff0c\u8bf7\u67e5\u770b\u64ad\u653e\u5668\u72b6\u6001\u3002";}}finally{if(!musicCard.IsDisposed)musicCard.SetBusy(false);}};
          panel.Controls.Add(musicCard);if(editing)head.BringToFront();
        }
        if(c.Id=="weather"){
          body.Visible=false;head.Visible=false;var weatherCard=new WeatherCardControl{Square=c.WeatherMode=="square",Editing=editing,Dock=DockStyle.Fill,Enabled=!editing};weatherCard.Present(weatherSnapshot.City==WeatherCity(c)?weatherSnapshot:new WeatherSnapshot{City=WeatherCity(c),State=WeatherSnapshot.ValidCity(WeatherCity(c))?"not-connected":"unconfigured"});weatherCard.ChooseCity+=(sender,e)=>OpenWeatherDetails(c);weatherCard.DetailRequested+=(sender,e)=>OpenWeatherDetails(c);panel.Controls.Add(weatherCard);if(editing)head.BringToFront();
        }
        if(c.Id=="autoloop"){body.Visible=false;head.Visible=false;var autoCard=new AutoloopSidebarControl{Dock=DockStyle.Fill,Enabled=!editing};panel.Controls.Add(autoCard);if(editing)head.BringToFront();}
        if(c.Id=="shortcuts") {
          var action=new GlassActionButton { Text=editing?"选择应用":"打开应用", Location=new Point(14,panel.Height-37), Size=new Size(100,27), FlatStyle=FlatStyle.Flat, ForeColor=Text, BackColor=Color.Transparent };
          action.Enabled=editing || !String.IsNullOrEmpty(c.ShortcutPath);
          action.Click+=(s,e)=> { if(editing) { modal=true; try { using(var picker=new OpenFileDialog { Filter="应用程序或快捷方式|*.exe;*.lnk", Title="选择快捷入口" }) { if(picker.ShowDialog(form)==DialogResult.OK) { c.ShortcutPath=picker.FileName; Save(); Rebuild(); } } } finally { modal=false; } } else { try { string ext=Path.GetExtension(c.ShortcutPath).ToLowerInvariant(); if(ext!=".exe" && ext!=".lnk") throw new InvalidOperationException(); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c.ShortcutPath){UseShellExecute=true}); } catch { MessageBox.Show("无法打开该应用，请在编辑模式重新选择。","快捷入口"); } } };
          panel.Controls.Add(action); action.BringToFront();
        }
        if(editing){var size=new GlassActionButton {Text="↔",AccessibleName=c.Id=="autoloop"?(c.AutoloopMode=="square"?"切换为 Autoloop 长条":"切换为 Autoloop 方块"):c.Id=="music"?(c.MusicMode=="square"?"切换为长条":"切换为小方块"):c.Id=="weather"?(c.WeatherMode=="square"?"切换为天气长条":"切换为天气小方块"):(c.SizeMode=="small"?"展开为月历":"收起为表盘"),Bounds=new Rectangle(panel.Width-66,7,28,25)};size.Click+=(sender,e)=>{if(c.Id=="autoloop")c.AutoloopMode=c.AutoloopMode=="square"?"strip":"square";else if(c.Id=="music")c.MusicMode=c.MusicMode=="square"?"strip":"square";else if(c.Id=="weather")c.WeatherMode=c.WeatherMode=="square"?"strip":"square";else c.SizeMode=c.SizeMode=="small"?"large":"small";Rebuild();};var del=new GlassActionButton {Text="×",AccessibleName="删除小组件",Bounds=new Rectangle(panel.Width-34,7,28,25)};del.Click+=(sender,e)=>{CurrentLayout.Remove(c.Id);Rebuild();};if(c.Id!="shortcuts")panel.Controls.Add(size);else size.Dispose();panel.Controls.Add(del);if(!size.IsDisposed)size.BringToFront();del.BringToFront();}
        if(editing)BindDrag(panel,c.Id);cards.Controls.Add(panel);
      }
      cards.ResumeLayout();ReflowCards();UpdateHeader();form.ResumeLayout();
    }
    public static int SelfTest() {
      var s=new HoverState(); s.Tick(0,true,false,false); s.Tick(249,true,false,false); if(s.Open) return 1; s.Tick(250,true,false,false); if(!s.Open) return 2;
      s.Tick(300,false,false,false); s.Tick(900,false,true,false); if(!s.Open) return 3; s.Tick(1000,false,false,false); s.Tick(1650,false,false,false); if(s.Open) return 4;
      s.Open=true; s.Pinned=true; s.Tick(2000,false,false,false); s.Tick(4000,false,false,false); if(!s.Open) return 5; s.Pinned=false; s.Tick(4100,false,false,true); if(s.Open) return 6;
      var l=Layout.Default(); l.Cards.Add(new Card{Id="status",Size="bad"}); l.Cards.Add(new Card{Id="unknown"}); l.Normalize(); if(l.Cards.Count!=3) return 7;
      using(var m=new MemoryStream()) { new XmlSerializer(typeof(Layout)).Serialize(m,l); m.Position=0; var loaded=(Layout)new XmlSerializer(typeof(Layout)).Deserialize(m); loaded.Normalize(); if(loaded.Cards.Count!=3 || loaded.Cards[0].Id!="music") return 8; }
      var interrupted=new HoverState(); interrupted.Tick(0,true,false,false); interrupted.Tick(200,false,false,false); interrupted.Tick(220,true,false,false); interrupted.Tick(400,true,false,false); if(interrupted.Open) return 9; interrupted.Tick(470,true,false,false); if(!interrupted.Open) return 10;
      interrupted.Tick(500,false,false,false); interrupted.Tick(800,false,true,false); interrupted.Tick(1450,false,true,false); if(!interrupted.Open) return 11;
      foreach(var area in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1280,0,1280,1024),new Rectangle(1920,-300,1024,768)}) { var r=WorkbenchBounds(area); if(!area.Contains(r) || r.Right>area.Right-32) return 12; }
      var pinned=new HoverState{Open=true,Pinned=true}; pinned.Tick(0,false,false,true); if(pinned.Open) return 13;
      var screen=new Rectangle(-1280,0,1280,1024); if(!Native.CoversScreen(screen,screen,false) || Native.CoversScreen(screen,screen,true) || Native.CoversScreen(new Rectangle(-1280,0,1280,984),screen,false)) return 14;
      string f=Path.GetTempFileName(); try { File.WriteAllText(f,"broken xml"); if(Load(f).Cards.Count!=3) return 15; } finally { File.Delete(f); }
      l.Cards[0].MusicMode="square";
      using(var m=new MemoryStream()) { new XmlSerializer(typeof(Layout)).Serialize(m,l); m.Position=0; var restored=(Layout)new XmlSerializer(typeof(Layout)).Deserialize(m); restored.Normalize(); if(restored.Cards[0].MusicMode!="square") return 16; }
      l.Cards[0].MusicMode="invalid"; l.Normalize(); if(l.Cards[0].MusicMode!="strip") return 17;
      var column=ColumnBounds(new Rectangle(0,0,2560,1392));if(column.X!=2166 || column.Y!=556 || column.Width!=344) return 19;
      foreach(var area in new[]{new Rectangle(0,0,1280,520),new Rectangle(-1280,0,1280,720),new Rectangle(0,0,2560,1392)})if(!area.Contains(ColumnBounds(area)))return 20;
      if(!HoverContains(new Point(20,105),new Rectangle(0,0,100,100),new Rectangle(10,112,100,100))||HoverContains(new Point(5,150),new Rectangle(0,0,100,100),new Rectangle(10,112,100,100)))return 21;
      if(!NeteaseSource.IsNetease("cloudmusic.exe") || NeteaseSource.IsNetease("spotify.exe") || NeteaseSource.IsNetease("fake-cloudmusic.exe") || NeteaseSource.IsNetease("browser.exe")) return 18;
      return 0;
    }
    public static void RenderIndependentWidget(string folder,bool calendar,int width){
      using(var panel=new WidgetPanel{Size=new Size(width,380),BackColor=Palette.Base})using(var control=new MiniClockCalendar(calendar){Dock=DockStyle.Fill})using(var image=new Bitmap(width,380)){panel.Controls.Add(control);panel.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,calendar?"large-calendar.png":"large-clock.png"));}
    }
    public static void RunDesktopPreview(string folder){
      var app=new App(Path.Combine(folder,"desktop-preview-layout.xml"));app.timer.Stop();if(!File.Exists(Path.Combine(folder,"desktop-preview-layout.xml")))app.layout.Cards=new List<Card>{new Card{Id="clock",SizeMode="small"},new Card{Id="music",MusicMode="square"},new Card{Id="shortcuts"}};
      using(var host=new Form{Text="Sidebar project interaction preview",ClientSize=new Size(410,850),StartPosition=FormStartPosition.CenterScreen,BackColor=Palette.Base}){
        app.form.TopMost=false;app.form.TopLevel=false;app.form.TransparencyKey=Color.Empty;app.form.BackColor=Palette.Base;app.cards.BackColor=Palette.Base;app.footer.BackColor=Palette.Base;app.form.Location=new Point(20,20);app.baseColumnWidth=344;app.columnCapacity=790;app.Rebuild();host.Controls.Add(app.form);app.form.Show();var lifetime=new System.Windows.Forms.Timer{Interval=300000};lifetime.Tick+=(sender,e)=>{lifetime.Stop();host.Close();};host.Shown+=(sender,e)=>lifetime.Start();host.FormClosed+=(sender,e)=>lifetime.Dispose();host.FormClosed+=(sender,e)=>app.Shutdown();Application.Run(host);
      }
    }
    public static string TestNativeGrid(string folder){
      Directory.CreateDirectory(folder);var app=new App(Path.Combine(folder,"native-grid-layout.xml"));app.timer.Stop();
      try{app.layout.Cards=new List<Card>{new Card{Id="clock",SizeMode="small"},new Card{Id="calendar",SizeMode="small"},new Card{Id="music",MusicMode="strip"}};
      app.PrepareInitialLayout(Screen.PrimaryScreen);app.form.Show();Application.DoEvents();
      using(var image=new Bitmap(app.form.Width,app.form.Height)){app.form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.MakeTransparent(Bg);using(var canvas=new Bitmap(image.Width,image.Height))using(var graphics=Graphics.FromImage(canvas)){graphics.Clear(Color.FromArgb(24,24,26));graphics.DrawImageUnscaled(image,0,0);canvas.Save(Path.Combine(folder,"native-grid.png"));}}
      app.BeginEditing();app.dragging="fixture";var captureWatch=System.Diagnostics.Stopwatch.StartNew();app.StartDragPreview("clock");captureWatch.Stop();int rebuilds=app.RebuildCount;var times=new List<double>();
      for(int i=0;i<60;i++){app.CurrentLayout.Move(i%2==0?"music":"clock",i%2==0?"clock":"music");var watch=System.Diagnostics.Stopwatch.StartNew();app.ReflowCards();Application.DoEvents();times.Add(watch.Elapsed.TotalMilliseconds);var bounds=app.cards.Controls.Cast<Control>().Where(v=>v.Tag is string).Select(v=>v.Bounds).ToArray();for(int a=0;a<bounds.Length;a++)for(int b=a+1;b<bounds.Length;b++)if(bounds[a].IntersectsWith(bounds[b]))throw new InvalidOperationException("Native preview overlap");}
      if(app.RebuildCount!=rebuilds)throw new InvalidOperationException("Drag preview rebuilt controls");var dropWatch=System.Diagnostics.Stopwatch.StartNew();app.dragging=null;app.EndDragPreview();app.ReflowCards();Application.DoEvents();dropWatch.Stop();app.CancelEditing();if(app.layout.Cards[0].Id!="clock")throw new InvalidOperationException("Native cancel changed original order");times.Sort();return "{\"previewUpdates\":60,\"controlRebuildsDuringPreview\":0,\"snapshotCaptureMs\":"+captureWatch.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"dropLayoutMs\":"+dropWatch.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"p95Ms\":"+times[(int)(times.Count*.95)].ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"scope\":\"owned snapshot preview paint plus message processing; excludes actual OLE input and PowerShell status timings\"}";
      }finally{app.Shutdown();}
    }
    public static void RenderCheck(string destination,string mode) {
      var app=new App(Path.Combine(Path.GetTempPath(),"column-render-no-layout.xml"));app.timer.Stop();app.CurrentLayout.Cards=Layout.Default().Cards;app.CurrentLayout.Cards[0].MusicMode=mode;if(mode.StartsWith("autoloop-")){app.CurrentLayout.Cards=new List<Card>{new Card{Id="autoloop",AutoloopMode=mode.Substring(9)},new Card{Id="clock",SizeMode="small"},new Card{Id="music",MusicMode="strip"},new Card{Id="weather",WeatherMode="square"}};}app.Place(Screen.PrimaryScreen);app.Rebuild();
      app.form.Location=new Point(-12000,-12000);app.form.Show();
      if(mode.Contains("pointer"))foreach(Control control in app.cards.Controls){var panel=control as WidgetPanel;if(panel!=null)for(int i=0;i<30;i++)panel.UpdatePointer(i*40,panel.PointToScreen(new Point(44,70)),true);}
      using(var bitmap=new Bitmap(app.form.Width,app.form.Height)) {app.form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.MakeTransparent(Bg);using(var canvas=new Bitmap(bitmap.Width+32,bitmap.Height+32))using(var g=Graphics.FromImage(canvas)){g.Clear(Color.FromArgb(20,23,29));g.DrawImageUnscaled(bitmap,16,16);canvas.Save(destination,System.Drawing.Imaging.ImageFormat.Png);}}
      app.Shutdown();
    }
  }
}
