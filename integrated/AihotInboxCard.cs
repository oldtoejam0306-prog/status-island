using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace IslandIntegrated {
  sealed class AihotHitLabel : Label {
    public AihotHitLabel() { SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint,true); }
    protected override void OnPaint(PaintEventArgs e) { }
  }
  sealed class AihotHitButton : Button {
    public AihotHitButton() {
      SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
    }
    protected override void OnPaint(PaintEventArgs e) { }
  }
  public sealed class AihotSidebarControl : Control {
    internal readonly AihotMonitor monitor;
    readonly Label title,summary,status;
    readonly Button refresh,pause,inspect,read,expand;
    readonly Label[] rowLabels=new Label[3];
    readonly Button[] rowReads=new Button[3];
    readonly Action<string> openUrl;
    internal AihotItem displayedItem;
    internal List<AihotItem> displayedItems=new List<AihotItem>();
    bool expanded;
    public bool Expanded {get{return expanded;}set{SetExpanded(value);}}
    public int PreferredCardHeight {get;private set;}
    public event EventHandler ViewChanged;
    bool readError;
    int scrollOffset;
    internal int ScrollOffset {get{return scrollOffset;}}
    AihotCardOverlay overlay;
    bool overlayMode,disposed;
    internal bool RefreshHovered,RefreshPressed,PauseHovered,PausePressed,ModeHovered,ModePressed;
    string visualFeedback="";
    public string DisplayedStatus {get{return String.IsNullOrEmpty(visualFeedback)?monitor.Status:visualFeedback;}}
    public AihotSidebarControl(AihotMonitor m) : this(m,null) { }
    internal AihotSidebarControl(AihotMonitor m,Action<string> opener) {
      openUrl=opener;monitor=m;PreferredCardHeight=AihotLayout.CardHeight;Size=new Size(AihotLayout.CardWidth,PreferredCardHeight);SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;ForeColor=Palette.Text;Font=new Font("Microsoft YaHei UI",9f);DoubleBuffered=true;
      title=LabelAt("AIHOT助手",24,9,160,24,14f,true);summary=LabelAt("正在载入热点",20,42,320,48,12f,false);status=LabelAt("正在连接 AIHOT",20,104,320,20,9f,false);
      refresh=ButtonAt("立即检查",0,0,104);pause=ButtonAt("暂停提醒",0,0,104);expand=ButtonAt("未读列表",0,0,104);inspect=ButtonAt("打开 AIHOT",232,8,104);read=ButtonAt("已读",0,0,52);
      for(int i=0;i<3;i++){int row=i;rowLabels[i]=LabelAt("",0,0,200,22,12f,false);rowReads[i]=ButtonAt("已读",0,0,52);rowLabels[i].Click+=(s,e)=>OpenVisibleItem(row);rowReads[i].Click+=(s,e)=>MarkRead(VisibleItem(row));rowLabels[i].Cursor=Cursors.Hand;rowReads[i].Cursor=Cursors.Hand;}
      refresh.Click+=(s,e)=>RefreshNow();pause.Click+=(s,e)=>TogglePause();inspect.Click+=(s,e)=>OpenHomepage();read.Click+=(s,e)=>MarkRead(displayedItem);expand.Click+=(s,e)=>SetExpanded(!Expanded);title.Click+=(s,e)=>OpenHomepage();summary.Click+=(s,e)=>OpenDisplayedItem();title.Cursor=Cursors.Hand;summary.Cursor=Cursors.Hand;
      HookActionButton(refresh,0);HookActionButton(pause,1);HookActionButton(expand,2);
      foreach(Control c in Controls)c.MouseWheel+=OnChildMouseWheel;
      monitor.Changed+=OnChanged;ParentChanged+=(s,e)=>SyncOverlay();LocationChanged+=(s,e)=>SyncOverlay();SizeChanged+=(s,e)=>{LayoutHitControls();SyncOverlay();};VisibleChanged+=(s,e)=>SyncOverlay();EnabledChanged+=(s,e)=>SyncOverlay();Disposed+=(s,e)=>{disposed=true;StopOverlay();};RefreshView();
    }
    Label LabelAt(string text,int x,int y,int w,int h,float size,bool bold){var l=new AihotHitLabel{Text=text,AccessibleName=text,Left=x,Top=y,Width=w,Height=h,ForeColor=Palette.Text,BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),AutoEllipsis=true};Controls.Add(l);return l;}
    Button ButtonAt(string text,int x,int y,int w){var b=new AihotHitButton{Text=text,AccessibleName=text,Left=x,Top=y,Width=w,Height=32,FlatStyle=FlatStyle.Flat,ForeColor=Palette.Text,BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",9f)};b.FlatAppearance.BorderSize=0;Controls.Add(b);return b;}
    void OpenHomepage(){visualFeedback=AihotNavigation.OpenHomepage(openUrl)?"":"无法打开默认浏览器，请检查 Windows 浏览器设置后重试。";RefreshPresentation();}
    void OpenDisplayedItem(){OpenItem(displayedItem);}
    AihotItem VisibleItem(int index){int at=scrollOffset+index;return at>=0&&at<displayedItems.Count?displayedItems[at]:null;}
    void OpenVisibleItem(int index){OpenItem(VisibleItem(index));}
    void OpenItem(AihotItem item){if(item==null)return;bool opened=AihotNavigation.OpenItem(item.AihotUrl,openUrl);visualFeedback=opened?"":"无法打开默认浏览器，请检查 Windows 浏览器设置后重试。";if(opened)readError=!monitor.MarkRead(item);RefreshView();}
    public void ExpandUnread(){SetExpanded(true);}
    void SetExpanded(bool value){if(expanded==value)return;expanded=value;if(!expanded)scrollOffset=0;UpdatePreferredHeight(false);RefreshView();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
    internal void ScrollUnread(int delta){int capacity=Expanded?3:1;int max=Math.Max(0,displayedItems.Count-capacity);int next=Math.Max(0,Math.Min(max,scrollOffset+delta));if(next==scrollOffset)return;scrollOffset=next;RefreshView();}
    void UpdatePreferredHeight(bool notify=true){int next=AihotLayout.InboxHeight(monitor.UnreadCount,Expanded);if(next==PreferredCardHeight)return;PreferredCardHeight=next;if(notify&&ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
    void MarkRead(AihotItem item){readError=!monitor.MarkRead(item);visualFeedback=readError?"已读状态保存失败，请重试":"";RefreshView();}
    void RefreshNow(){visualFeedback="";monitor.Refresh();RefreshPresentation();}
    internal void SetRefreshVisualState(bool hovered,bool pressed){
      pressed=hovered&&pressed&&!monitor.IsRefreshing;
      if(RefreshHovered==hovered&&RefreshPressed==pressed)return;
      RefreshHovered=hovered;RefreshPressed=pressed;RefreshPresentation();
    }
    void HookActionButton(Button button,int index){button.Cursor=Cursors.Hand;button.MouseEnter+=(s,e)=>SetActionVisualState(index,true,false);button.MouseLeave+=(s,e)=>SetActionVisualState(index,false,false);button.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)SetActionVisualState(index,true,true);};button.MouseUp+=(s,e)=>SetActionVisualState(index,button.ClientRectangle.Contains(e.Location),false);button.MouseCaptureChanged+=(s,e)=>{if(!button.Capture&&ActionPressed(index))SetActionVisualState(index,ActionHovered(index),false);};}
    void OnChildMouseWheel(object sender,MouseEventArgs e){ScrollUnread(-Math.Sign(e.Delta));var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;}
    bool ActionHovered(int index){return index==0?RefreshHovered:index==1?PauseHovered:ModeHovered;}
    bool ActionPressed(int index){return index==0?RefreshPressed:index==1?PausePressed:ModePressed;}
    internal void SetActionVisualState(int index,bool hovered,bool pressed){if(index==0){pressed=hovered&&pressed&&!monitor.IsRefreshing;if(RefreshHovered==hovered&&RefreshPressed==pressed)return;RefreshHovered=hovered;RefreshPressed=pressed;}else if(index==1){if(PauseHovered==hovered&&PausePressed==pressed)return;PauseHovered=hovered;PausePressed=pressed;}else{if(ModeHovered==hovered&&ModePressed==pressed)return;ModeHovered=hovered;ModePressed=pressed;}RefreshPresentation();}
    internal void UpdateActionVisuals(Point p,bool pressed){for(int i=0;i<3;i++){bool hit=AihotLayout.ButtonHit(Width,i).Contains(p);SetActionVisualState(i,hit,hit&&pressed);}}
    internal bool IsInteractiveAt(Point p){if(AihotLayout.TitleHit(Width).Contains(p)||AihotLayout.OpenHit(Width).Contains(p)||AihotLayout.ButtonHit(Width,0).Contains(p)||AihotLayout.ButtonHit(Width,1).Contains(p)||AihotLayout.ButtonHit(Width,2).Contains(p))return true;if(Expanded){for(int i=0;i<Math.Min(3,Math.Max(0,displayedItems.Count-scrollOffset));i++)if(AihotLayout.UnreadStoryHit(Width,i).Contains(p)||AihotLayout.UnreadReadHit(Width,i).Contains(p))return true;}else if(displayedItem!=null&&(AihotLayout.StoryHit(Width).Contains(p)||AihotLayout.CompactReadHit(Width).Contains(p)))return true;return false;}
    void TogglePause(){visualFeedback="";monitor.SetPaused(!monitor.Paused);RefreshPresentation();}
    void RefreshPresentation(){
      bool checking=monitor.IsRefreshing;
      refresh.Text=checking?"检查中…":"立即检查";
      refresh.AccessibleName=refresh.Text;
      refresh.Enabled=!checking;
      if(overlay!=null)overlay.MarkDirty();fallbackKey="";Invalidate();if(status!=null){status.Text=DisplayedStatus;status.AccessibleName=DisplayedStatus;}if(pause!=null){pause.Text=monitor.Paused?"恢复提醒":"暂停提醒";pause.AccessibleName=pause.Text;}if(summary!=null)summary.AccessibleName=summary.Text;}
    internal void HandleFrameClick(Point p){if(AihotLayout.RefreshHit(Width,Height).Contains(p)){if(!monitor.IsRefreshing)RefreshNow();return;}if(AihotLayout.PauseHit(Width,Height).Contains(p)){TogglePause();return;}if(AihotLayout.ExpandHit(Width,Height).Contains(p)){SetExpanded(!Expanded);return;}if(AihotLayout.OpenHit(Width).Contains(p)){OpenHomepage();return;}if(Expanded){int count=Math.Min(3,Math.Max(0,displayedItems.Count-scrollOffset));for(int i=0;i<count;i++){if(AihotLayout.UnreadReadHit(Width,i).Contains(p)){MarkRead(VisibleItem(i));return;}if(AihotLayout.UnreadStoryHit(Width,i).Contains(p)){OpenVisibleItem(i);return;}}}else{if(AihotLayout.CompactReadHit(Width).Contains(p)){MarkRead(displayedItem);return;}if(AihotLayout.StoryHit(Width).Contains(p))OpenDisplayedItem();}}
    static bool IsLayeredHost(NoActivateForm host){return host!=null&&host.TopLevel&&!host.PerPixelLayered&&host.TransparencyKey.ToArgb()==Color.Fuchsia.ToArgb();}
    void SyncOverlay(){if(disposed)return;var host=FindForm() as NoActivateForm;bool use=IsLayeredHost(host)&&Visible&&Enabled&&Parent!=null;if(!use){bool wasOverlay=overlayMode;overlayMode=false;StopOverlay();ShowFallback(true);if(wasOverlay)Invalidate();return;}overlayMode=true;ShowFallback(false);if(overlay==null){overlay=new AihotCardOverlay(this);overlay.SetOwner(host);overlay.Synchronize();return;}overlay.SetOwner(host);overlay.Synchronize();}
    void LayoutHitControls(){
      if(title==null)return;
      title.Bounds=BodyHit(AihotLayout.TitleHit(Width));
      summary.Bounds=BodyHit(AihotLayout.StoryHit(Width));
      inspect.Bounds=BodyHit(AihotLayout.OpenHit(Width));
      refresh.Bounds=BodyHit(AihotLayout.RefreshHit(Width,Height));
      pause.Bounds=BodyHit(AihotLayout.PauseHit(Width,Height));
      read.Bounds=BodyHit(AihotLayout.CompactReadHit(Width));
      expand.Bounds=BodyHit(AihotLayout.ExpandHit(Width,Height));
      for(int i=0;i<3;i++){rowLabels[i].Bounds=BodyHit(AihotLayout.UnreadStoryHit(Width,i));rowReads[i].Bounds=BodyHit(AihotLayout.UnreadReadHit(Width,i));}
      // Status text is rendered by the card and receives no independent action.
      status.Visible=false;
    }
    static Rectangle BodyHit(Rectangle value){value.Offset(-AihotLayout.Margin,-AihotLayout.Margin);return value;}
    void ShowFallback(bool value){foreach(Control child in Controls)child.Visible=false;LayoutHitControls();if(!value)return;title.Visible=true;inspect.Visible=true;refresh.Visible=true;pause.Visible=true;expand.Visible=true;if(Expanded){for(int i=0;i<3;i++){bool show=i<Math.Max(0,Math.Min(3,displayedItems.Count-scrollOffset));rowLabels[i].Visible=show;rowReads[i].Visible=show;}}else{summary.Visible=displayedItems.Count>0;read.Visible=displayedItems.Count>0;}}
    void StopOverlay(){if(overlay!=null){var old=overlay;overlay=null;try{old.Close();}catch{}old.Dispose();}}
    public string PresentationError {get;private set;}
    Bitmap fallbackFrame;string fallbackKey="";
    internal void LayeredPushFailed(int error){PresentationError="layered update failed ("+error+")";overlayMode=false;StopOverlay();ShowFallback(true);Invalidate();}
    internal void LayeredPushSucceeded(){PresentationError=null;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(overlayMode?Color.Fuchsia:Palette.Base);}
    internal string CurrentCardStatus(){return DisplayedStatus;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(overlayMode)return;bool contentOnly=GlassComposition.Active&&Parent is WidgetPanel;string state=CurrentCardStatus();bool paused=monitor.Paused;string key=contentOnly+"|"+Width+"|"+Height+"|"+String.Join(";",displayedItems.Select(x=>(x.Id??"")+"|"+(x.Revision??"")+"|"+(x.Title??"")))+"|"+state+"|"+paused+"|"+monitor.IsRefreshing+"|"+Expanded+"|"+scrollOffset+"|"+readError+"|"+RefreshHovered+"|"+RefreshPressed+"|"+PauseHovered+"|"+PausePressed+"|"+ModeHovered+"|"+ModePressed+"|"+Palette.Theme+"|"+Palette.Solid+"|"+Palette.Legacy+"|"+SystemInformation.HighContrast;if(fallbackFrame==null||key!=fallbackKey){if(fallbackFrame!=null)fallbackFrame.Dispose();fallbackFrame=AihotRenderer.RenderInboxCard(Width,Height,displayedItems,state,paused,monitor.IsRefreshing,Expanded,readError,!contentOnly,scrollOffset,RefreshHovered,RefreshPressed,PauseHovered,PausePressed,ModeHovered,ModePressed);fallbackKey=key;}if(contentOnly)e.Graphics.DrawImageUnscaled(fallbackFrame,-AihotLayout.Margin,-AihotLayout.Margin);else GlassComposition.DrawImageReplacing(e.Graphics,fallbackFrame,-AihotLayout.Margin,-AihotLayout.Margin);}
    void OnChanged(object s,EventArgs e){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((MethodInvoker)RefreshView);return;}RefreshView();}
    void RefreshView(){displayedItems=monitor.UnreadItems??new List<AihotItem>();int capacity=Expanded?3:1;scrollOffset=Math.Max(0,Math.Min(scrollOffset,Math.Max(0,displayedItems.Count-capacity)));displayedItem=displayedItems.Count==0?null:displayedItems[scrollOffset];title.Text="AIHOT助手";summary.Text=displayedItem==null?"暂时没有新消息":displayedItem.Title;status.Text=DisplayedStatus;inspect.Text="打开 AIHOT";refresh.Text=monitor.IsRefreshing?"检查中…":"立即检查";pause.Text=monitor.Paused?"恢复提醒":"暂停提醒";expand.Text=Expanded?"返回摘要":"未读列表";expand.AccessibleName=expand.Text;pause.AccessibleName=pause.Text;read.AccessibleName="已读";summary.AccessibleName=displayedItem==null?"暂无未读消息":"打开未读新闻："+displayedItem.Title;for(int i=0;i<3;i++){var item=VisibleItem(i);rowLabels[i].Text=item==null?"":item.Title;rowLabels[i].AccessibleName=item==null?"":"打开未读新闻："+item.Title;rowReads[i].AccessibleName=item==null?"":"已读："+item.Title;}AccessibleName=readError?"AIHOT 未读消息；已读状态保存失败，请重试":"AIHOT 未读消息，共 "+displayedItems.Count+" 条";UpdatePreferredHeight();LayoutHitControls();if(!overlayMode)ShowFallback(true);RefreshPresentation();}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Left)HandleFrameClick(new Point(e.X+AihotLayout.Margin,e.Y+AihotLayout.Margin));}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);ScrollUnread(-Math.Sign(e.Delta));}
    protected override void Dispose(bool disposing){if(disposing){disposed=true;monitor.Changed-=OnChanged;StopOverlay();if(fallbackFrame!=null){fallbackFrame.Dispose();fallbackFrame=null;}}base.Dispose(disposing);}
    public static void RenderEvidence(string directory) {
      Directory.CreateDirectory(directory);
      var examples=new List<AihotItem>{
        new AihotItem{Id="example-1",Revision="r1",Rank=6,Title="示例：多家模型厂商更新智能体工具调用能力",Summary="示例摘要：多家厂商公布工具调用与任务编排更新，重点改进长任务执行和失败恢复。",SourceName="示例来源",SourceCount=8,LatestAt=new DateTime(2026,11,12,10,42,0,DateTimeKind.Local)},
        new AihotItem{Id="example-2",Revision="r1",Rank=4,Title="示例：开源推理模型发布新版本",Summary="示例摘要：新版本调整推理速度与上下文处理，社区正在比较公开基准结果。",SourceName="示例来源",SourceCount=5,LatestAt=new DateTime(2026,11,12,10,31,0,DateTimeKind.Local)},
        new AihotItem{Id="example-3",Revision="r1",Rank=7,Title="示例：企业推出本地知识库助手",Summary="示例摘要：产品增加本地索引与权限控制，支持团队在内网检索资料。",SourceName="示例来源",SourceCount=3,LatestAt=new DateTime(2026,11,12,10,18,0,DateTimeKind.Local)},
        new AihotItem{Id="example-4",Revision="r1",Rank=9,Title="示例：研究团队分享视觉模型评测",Summary="示例摘要：研究对视觉问答与图表理解任务做了公开测试。",SourceName="示例来源",SourceCount=2,LatestAt=new DateTime(2026,11,12,10,2,0,DateTimeKind.Local)},
        new AihotItem{Id="example-5",Revision="r1",Rank=2,Title="示例：AI 搜索服务扩展多语言摘要",Summary="示例摘要：新增语言支持并优化引用来源显示。",SourceName="示例来源",SourceCount=2,LatestAt=new DateTime(2026,11,12,9,55,0,DateTimeKind.Local)},
        new AihotItem{Id="example-6",Revision="r1",Rank=10,Title="示例：开发者大会开放报名",Summary="示例摘要：活动议程包含模型服务、数据工具与应用案例。",SourceName="示例来源",SourceCount=1,LatestAt=new DateTime(2026,11,12,9,41,0,DateTimeKind.Local)}
      };
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,examples,"最近检查 10:45",false,false,false,false,true))bmp.Save(Path.Combine(directory,"compact-default.png"));
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,examples,"最近检查 10:45",false,false,true,false,true))bmp.Save(Path.Combine(directory,"compact-list.png"));
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,new List<AihotItem>(),"网络连接失败 · 稍后重试",false,false,false,false,true))bmp.Save(Path.Combine(directory,"compact-empty.png"));
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,examples,"最近检查 10:45",false,false,false,false,true,0,true,false,true,false,true,false))bmp.Save(Path.Combine(directory,"buttons-hover.png"));
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,examples,"最近检查 10:45",false,false,false,false,true,0,false,true,false,true,false,true))bmp.Save(Path.Combine(directory,"buttons-pressed.png"));
      using(var bmp=AihotRenderer.RenderInboxCard(AihotLayout.CardWidth,AihotLayout.CardHeight,examples,"正在检查 AIHOT…",false,true,false,false,true,0,false,false,false,false,false,false))bmp.Save(Path.Combine(directory,"buttons-checking.png"));
      File.WriteAllText(Path.Combine(directory,"EXAMPLES.txt"),"Illustrative fixture content only; not live AIHOT data. The newest example is labelled hot list rank 6 to emphasize recency ordering.\r\n");
    }
  }
  internal sealed class AihotCardOverlay : NoActivateForm {
    readonly AihotSidebarControl card;
    readonly System.Windows.Forms.Timer sync;
    Form owner;
    ScrollableControl scrollParent;
    Bitmap baseFrame, clippedFrame;
    string contentKey="", pushKey="";
    bool closed,forceRender=true;
    public int LastPushError {get;private set;}
    public bool LastPushSucceeded {get;private set;}
    public string LastClip {get;private set;}
    public AihotCardOverlay(AihotSidebarControl value) {
      card=value;
      PerPixelLayered=true;
      FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;
      TopMost=true;
      BackColor=Color.Fuchsia;
      sync=new System.Windows.Forms.Timer { Interval=16 };
      sync.Tick+=(s,e)=>Synchronize();
      GeometryContextChanged+=OnContext;
      LayeredSurfaceChanged+=OnContext;
      FormClosed+=(s,e)=>Cleanup();
    }
    public void SetOwner(Form value) {
      if(owner==value)return;
      UnhookOwner();
      owner=value;
      if(owner==null)return;
      ContextMenuStrip=owner.ContextMenuStrip;
      owner.LocationChanged+=OnGeometryChanged;
      owner.SizeChanged+=OnGeometryChanged;
      owner.VisibleChanged+=OnOwnerVisibility;
      owner.EnabledChanged+=OnOwnerVisibility;
      for(Control c=card.Parent;c!=null&&c!=owner;c=c.Parent) {
        var candidate=c as ScrollableControl;
        if(candidate!=null&&candidate.AutoScroll) {
          scrollParent=candidate;
          scrollParent.Scroll+=OnScroll;
          break;
        }
      }
    }
    void UnhookOwner() {
      if(owner!=null) {
        owner.LocationChanged-=OnGeometryChanged;
        owner.SizeChanged-=OnGeometryChanged;
        owner.VisibleChanged-=OnOwnerVisibility;
        owner.EnabledChanged-=OnOwnerVisibility;
      }
      if(scrollParent!=null) {
        scrollParent.Scroll-=OnScroll;
        scrollParent=null;
      }
    }
    void OnGeometryChanged(object sender,EventArgs e) { Synchronize(); }
    void OnScroll(object sender,ScrollEventArgs e) { Synchronize(); }
    void OnOwnerVisibility(object sender,EventArgs e) {
      if(owner==null||!owner.Visible||!owner.Enabled) {
        sync.Stop();
        if(Visible)Hide();
        return;
      }
      if(Visible)sync.Start();
      Synchronize();
    }
    void OnContext(object sender,EventArgs e) { forceRender=true;Synchronize(); }
    public void MarkDirty() { forceRender=true;Synchronize(); }
    Rectangle VisibleClip(Point loc,Size size) {
      Rectangle clip=Rectangle.Intersect(new Rectangle(loc,size),SystemInformation.VirtualScreen);
      if(scrollParent!=null) {
        if(!scrollParent.Visible)return Rectangle.Empty;
        clip=Rectangle.Intersect(clip,scrollParent.RectangleToScreen(scrollParent.ClientRectangle));
      }
      if(owner==null||!owner.Visible||!owner.Enabled)return Rectangle.Empty;
      clip=Rectangle.Intersect(clip,owner.RectangleToScreen(owner.ClientRectangle));
      return clip;
    }
    static string ItemKey(AihotItem item) {
      if(item==null)return "";
      return (item.Id??"")+"|"+(item.Revision??"")+"|"+(item.Title??"")+"|"+(item.Summary??"")+"|"+(item.SourceName??"")+"|"+(item.AihotUrl??"");
    }
    void ReplaceClippedFrame(Rectangle clip,Point loc) {
      if(clippedFrame!=null) { clippedFrame.Dispose();clippedFrame=null; }
      if(baseFrame==null)return;
      clippedFrame=(Bitmap)baseFrame.Clone();
      Rectangle screenClip=Rectangle.Intersect(clip,new Rectangle(loc,clippedFrame.Size));
      Rectangle localClip=new Rectangle(screenClip.X-loc.X,screenClip.Y-loc.Y,Math.Max(0,screenClip.Width),Math.Max(0,screenClip.Height));
      using(var g=Graphics.FromImage(clippedFrame)) {
        g.CompositingMode=CompositingMode.SourceCopy;
        using(var outside=new Region(new Rectangle(Point.Empty,clippedFrame.Size)))
        using(var transparent=new SolidBrush(Color.Transparent)) {
          outside.Exclude(localClip);
          g.SetClip(outside,CombineMode.Replace);
          g.FillRectangle(transparent,0,0,clippedFrame.Width,clippedFrame.Height);
        }
      }
    }
    void Cleanup() {
      if(closed)return;
      closed=true;
      sync.Stop();
      sync.Dispose();
      UnhookOwner();
      if(clippedFrame!=null) { clippedFrame.Dispose();clippedFrame=null; }
      if(baseFrame!=null) { baseFrame.Dispose();baseFrame=null; }
    }
    public void Synchronize() {
      if(closed||card.IsDisposed||owner==null)return;
      bool should=card.Visible&&card.Enabled&&owner.Visible&&owner.Enabled;
      if(!should) { sync.Stop();if(Visible)Hide();return; }
      Point cardLocation=card.PointToScreen(Point.Empty);
      Point loc=new Point(cardLocation.X-AihotLayout.Margin,cardLocation.Y-AihotLayout.Margin);
      Size size=new Size(card.Width+2*AihotLayout.Margin,card.Height+2*AihotLayout.Margin);
      Rectangle bounds=new Rectangle(loc,size);
      if(Bounds!=bounds)Bounds=bounds;
      Rectangle clip=VisibleClip(loc,size);
      byte alpha=(byte)Math.Max(0,Math.Min(255,(int)Math.Round(owner.Opacity*255)));
      string status=card.CurrentCardStatus();
      bool paused=card.monitor.Paused;
      string itemsKey=String.Join(";",card.displayedItems.Select(ItemKey));
      string nextContentKey=Palette.Theme+"|"+Palette.Solid+"|"+Palette.Legacy+"|"+SystemInformation.HighContrast+"|"+itemsKey+"|"+status+"|"+paused+"|"+card.monitor.IsRefreshing+"|"+card.Expanded+"|"+card.ScrollOffset+"|"+card.RefreshHovered+"|"+card.RefreshPressed+"|"+card.PauseHovered+"|"+card.PausePressed+"|"+card.ModeHovered+"|"+card.ModePressed;
      if(forceRender||baseFrame==null||contentKey!=nextContentKey) {
        if(baseFrame!=null)baseFrame.Dispose();
        baseFrame=AihotRenderer.RenderInboxCard(card.Width,card.Height,card.displayedItems,status,paused,card.monitor.IsRefreshing,card.Expanded,false,true,card.ScrollOffset,card.RefreshHovered,card.RefreshPressed,card.PauseHovered,card.PausePressed,card.ModeHovered,card.ModePressed);
        contentKey=nextContentKey;
        forceRender=false;
        pushKey="";
      }
      string nextPushKey=loc+"|"+clip+"|"+alpha+"|"+contentKey;
      if(nextPushKey==pushKey&&Visible)return;
      pushKey=nextPushKey;
      LastClip=clip.ToString();
      ReplaceClippedFrame(clip,loc);
      if(!IsHandleCreated) { IntPtr unused=Handle; }
      LastPushSucceeded=clippedFrame!=null&&GlassWin.Push(Handle,clippedFrame,loc,alpha);
      LastPushError=LastPushSucceeded?0:GlassWin.LastPushError;
      if(!LastPushSucceeded) { card.LayeredPushFailed(LastPushError);return; }
      card.LayeredPushSucceeded();
      if(!Visible)Show(owner);
      sync.Start();
    }
    protected override void OnMouseClick(MouseEventArgs e) {
      base.OnMouseClick(e);
      if(e.Button==MouseButtons.Left)card.HandleFrameClick(e.Location);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);
      if(e.Button==MouseButtons.Left)card.UpdateActionVisuals(e.Location,true);
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);
      card.UpdateActionVisuals(e.Location,false);
    }
    protected override void OnMouseLeave(EventArgs e) {
      base.OnMouseLeave(e);card.UpdateActionVisuals(Point.Empty,false);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      card.UpdateActionVisuals(e.Location,(e.Button&MouseButtons.Left)!=0);
      Cursor=card.IsInteractiveAt(e.Location)?Cursors.Hand:Cursors.Default;
    }
    protected override void OnMouseWheel(MouseEventArgs e) {
      base.OnMouseWheel(e);
      card.ScrollUnread(-Math.Sign(e.Delta));
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { Cleanup();base.OnFormClosed(e); }
    protected override void Dispose(bool disposing) { if(disposing)Cleanup();base.Dispose(disposing); }
  }
}
