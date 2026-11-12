using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IslandIntegrated {
  sealed class AutoloopDetailButton : Button {
    readonly Font labelFont=new Font("Microsoft YaHei UI",14f,FontStyle.Regular,GraphicsUnit.Pixel);
    bool hovered,pressed;
    public AutoloopDetailButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleRole=AccessibleRole.PushButton;Height=36;BackColor=Color.Transparent;}
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;if(!GlassComposition.Active)g.Clear(Palette.Base);var r=new RectangleF(.5f,.5f,Math.Max(1,Width-1),Math.Max(1,Height-1));using(var shape=GlassSurface.Path(r,24))using(var fill=new SolidBrush(Color.FromArgb(!Enabled?26:pressed?112:hovered?78:42,Palette.Top)))using(var edge=new Pen(Color.FromArgb(65,Palette.Accent),1)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}using(var brush=new SolidBrush(Enabled?Palette.Text:Color.FromArgb(123,Palette.Secondary)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter})g.DrawString(Text,labelFont,brush,ClientRectangle,format);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-4,-4),Palette.Text,Palette.Base);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width>0&&Height>0){using(var path=GlassSurface.Path(new RectangleF(0,0,Width,Height),24)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}}
    protected override void Dispose(bool disposing){if(disposing)labelFont.Dispose();base.Dispose(disposing);}
  }

  public sealed class AutoloopDetailView : Form {
    Func<string,string,string,CancellationToken,Task<AutoloopActionResult>> answerAsync;
    public Func<string,string,string,CancellationToken,Task<AutoloopActionResult>> AnswerAsync {get{return answerAsync;}set{answerAsync=value;if(bridgeSnapshot!=null)UpdateBridge(bridgeSnapshot);}}
    public Func<CancellationToken,Task<AutoloopSnapshot>> RefreshAsync {get;set;}
    public Func<CancellationToken,Task<AutoloopSnapshotData>> ReadProjectSnapshotsAsync {get;set;}
    AutoloopActionResult priorSubmission;
    public AutoloopActionResult PriorSubmission {get{return priorSubmission;}set{priorSubmission=value;RestorePriorSubmission();}}
    public bool ReduceMotion {get;set;}
    readonly AutoloopSnapshot bridgeSnapshot;
    readonly Rectangle? requestedWorkingArea;
    readonly FlowLayoutPanel content=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(20,12,20,20),BackColor=Color.Transparent};
    readonly Label connection=new Label{AutoSize=true,MaximumSize=new Size(306,0)};
    readonly Label problem=new Label{AutoSize=true,MaximumSize=new Size(306,0)};
    readonly Label projectSummary=new Label{AutoSize=true,MaximumSize=new Size(306,0)};
    readonly Label audiobookSummary=new Label{AutoSize=true,MaximumSize=new Size(306,0)};
    readonly FlowLayoutPanel choices=new FlowLayoutPanel{AutoSize=true,MaximumSize=new Size(306,0),FlowDirection=FlowDirection.TopDown,WrapContents=false};
    readonly Label actionState=new Label{AutoSize=true,MaximumSize=new Size(306,0),Visible=false};
    readonly AutoloopDetailButton refreshButton=new AutoloopDetailButton{AutoSize=true,Text="刷新快照",Margin=new Padding(0,16,0,0)};
    readonly AutoloopDetailButton closeButton=new AutoloopDetailButton{Text="关闭",Dock=DockStyle.Right,Width=64,Margin=new Padding(0)};
    readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer{Interval=15};
    readonly AnimationTimerPrecision animationPrecision=new AnimationTimerPrecision();
    readonly StopwatchClock clock=new StopwatchClock();
    CancellationTokenSource lifetime=new CancellationTokenSource();
    readonly Dictionary<string,AutoloopActionResult> attempted=new Dictionary<string,AutoloopActionResult>();
    readonly HashSet<string> retryAllowed=new HashSet<string>();
    bool closing,submitted,directClose,fadeOut,answerInFlight; long animationStart; double animationFrom;
    public AutoloopDetailView(AutoloopSnapshot snapshot):this(snapshot,null) { }
    public AutoloopDetailView(AutoloopSnapshot snapshot,Rectangle? workingArea) {
      bridgeSnapshot=snapshot;
      requestedWorkingArea=workingArea;var initialArea=workingArea??Screen.PrimaryScreen.WorkingArea;
      FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;ClientSize=DetailSizeForWorkingArea(initialArea);Text="Autoloop 详情";BackColor=Palette.Base;
      AccessibleName="Autoloop 详情";Controls.Add(content);var topbar=new Panel{Dock=DockStyle.Top,Height=42,BackColor=Color.Transparent};topbar.Controls.Add(closeButton);Controls.Add(topbar);topbar.BringToFront();closeButton.Click+=(s,e)=>RequestClose();KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;RequestClose();}};Font=new Font("Microsoft YaHei UI",14f,FontStyle.Regular,GraphicsUnit.Pixel);content.Controls.Add(Heading("Autoloop"));
      connection.ForeColor=Palette.Secondary;problem.ForeColor=Palette.Text;projectSummary.ForeColor=Palette.Secondary;audiobookSummary.ForeColor=Palette.Secondary;
      content.Controls.Add(connection);content.Controls.Add(Section("当前问题"));content.Controls.Add(problem);content.Controls.Add(choices);content.Controls.Add(actionState);content.Controls.Add(Section("项目概览"));content.Controls.Add(projectSummary);content.Controls.Add(Section("有声书观察"));content.Controls.Add(audiobookSummary);content.Controls.Add(refreshButton);
      refreshButton.Click+=async(s,e)=>await RefreshAll();animation.Tick+=Animate;
      UpdateBridge(snapshot);PresentProjects(null);
      Shown+=async(s,e)=>{var area=requestedWorkingArea??Screen.FromControl(Owner??this).WorkingArea;var ownerBounds=Owner==null?Rectangle.Empty:Owner.Bounds;int ownerWidth=Owner==null?0:Owner.ClientSize.Width;Location=BoundsInWorkingArea(area,ownerBounds,ownerWidth,Owner!=null,Size).Location;Opacity=Reduced?1:0;if(!Reduced){animationFrom=0;animationStart=clock.Now;animationPrecision.SetActive(true);animation.Start();}await LoadProjectsOnce();};
      FormClosing+=(s,e)=>{if(!directClose&&!Reduced){e.Cancel=true;BeginClose();return;}closing=true;animation.Stop();animationPrecision.SetActive(false);lifetime.Cancel();};
    }
    internal static Size DetailSizeForWorkingArea(Rectangle area){
      return new Size(
        Math.Max(1,Math.Min(CornerLayout.Width,area.Width-32)),
        Math.Max(1,Math.Min(600,area.Height-32)));
    }
    internal static Rectangle BoundsInWorkingArea(Rectangle area,Rectangle ownerBounds,int ownerWidth,bool hasOwner,Size size){int minX=area.Left+16,minY=area.Top+16,maxX=Math.Max(minX,area.Right-size.Width-16),maxY=Math.Max(minY,area.Bottom-size.Height-16);int desiredRight=hasOwner?ownerBounds.Left+Math.Min(CornerLayout.Width,ownerWidth):area.Right-16;int desiredTop=hasOwner?ownerBounds.Top:area.Top+16;int x=Math.Max(minX,Math.Min(desiredRight-size.Width,maxX));int y=Math.Max(minY,Math.Min(desiredTop,maxY));return new Rectangle(x,y,size.Width,size.Height);}
    bool Reduced {get{return ReduceMotion||SystemInformation.HighContrast||!SystemInformation.UIEffectsEnabled;}}
    void RestorePriorSubmission(){if(priorSubmission==null||bridgeSnapshot==null||String.IsNullOrEmpty(bridgeSnapshot.QuestionId)||String.IsNullOrEmpty(bridgeSnapshot.CardFingerprint))return;attempted[QuestionKey(bridgeSnapshot)]=priorSubmission;UpdateBridge(bridgeSnapshot);}
    void Animate(object sender,EventArgs e){double duration=fadeOut?320:220;double t=Math.Min(1,(clock.Now-animationStart)/duration);double eased=1-Math.Pow(1-t,3);Opacity=fadeOut?animationFrom*(1-eased):animationFrom+(1-animationFrom)*eased;if(t>=1){animation.Stop();animationPrecision.SetActive(false);if(fadeOut){directClose=true;Close();}}}
    void RequestClose(){if(closing)return;if(Reduced){directClose=true;Close();return;}BeginClose();}
    void BeginClose(){if(fadeOut)return;fadeOut=true;animationFrom=Opacity;animationStart=clock.Now;animationPrecision.SetActive(true);animation.Start();}
    static Label Heading(string value){return new Label{Text=value,AutoSize=true,Font=new Font("Microsoft YaHei UI",20,FontStyle.Bold,GraphicsUnit.Pixel),ForeColor=Palette.Text,Margin=new Padding(0,0,0,14)};}
    static Label Section(string value){return new Label{Text=value,AutoSize=true,Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold,GraphicsUnit.Pixel),ForeColor=Palette.Text,Margin=new Padding(0,16,0,6)};}
    async Task LoadProjectsOnce(){if(ReadProjectSnapshotsAsync==null)return;try{var data=await ReadProjectSnapshotsAsync(lifetime.Token);if(!closing&&!IsDisposed)PresentProjects(data??new AutoloopSnapshotData());}catch(OperationCanceledException){}catch{if(!closing&&!IsDisposed){projectSummary.Text="项目快照首次读取失败，状态未知";audiobookSummary.Text="有声书观察首次读取失败，状态未知";}}}
    static string Fresh(DateTimeOffset? time,bool expired){if(!time.HasValue)return "时间未知";return expired?"已过期 · "+time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"观测于 "+time.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");}
    void UpdateBridge(AutoloopSnapshot value,bool manuallyRefreshed=false){string state=value==null?"未连接":value.State=="connected"&&AutoloopSidebarControl.IsFresh(value,DateTimeOffset.UtcNow)?"连接快照新鲜":value.State=="connected"||value.State=="stale"?"连接数据已过期":"未连接或不可用";connection.Text=state+(value!=null&&value.ObservedAt.HasValue?" · "+value.ObservedAt.Value.ToLocalTime().ToString("HH:mm:ss"):"");
      bool hasQuestion=value!=null&&!String.IsNullOrWhiteSpace(value.QuestionId);problem.Text=hasQuestion?((value.TaskTitle??"Autoloop 问题")+Environment.NewLine+(value.QuestionText??"")):(value==null?"没有可用连接快照":value.Summary??"当前没有待处理问题");
      foreach(Control old in choices.Controls.Cast<Control>().ToArray()){choices.Controls.Remove(old);old.Dispose();}submitted=false;string key=hasQuestion?QuestionKey(value):"";AutoloopActionResult prior;if(hasQuestion&&attempted.TryGetValue(key,out prior)){if(manuallyRefreshed&&prior.State!="confirmed"&&AutoloopSidebarControl.IsFresh(value,DateTimeOffset.UtcNow))retryAllowed.Add(key);actionState.Visible=true;actionState.Text=(prior.State=="confirmed"?"已确认":prior.State=="rejected"?"已拒绝":"结果未知")+" · "+(prior.Message??"")+(retryAllowed.Contains(key)?"；刷新仍显示此问题，可手动重试":"；此问题不会自动重试");actionState.ForeColor=prior.State=="confirmed"?Palette.Accent:Palette.Secondary;}
      else actionState.Visible=false;
      if(hasQuestion&&value.Options!=null)foreach(var option in value.Options){if(option==null||String.IsNullOrEmpty(option.Id))continue;var selected=option;bool retry=retryAllowed.Contains(key)&&attempted.ContainsKey(key)&&attempted[key].State!="confirmed";var button=new AutoloopDetailButton{AutoSize=true,MaximumSize=new Size(290,0),Text=option.Label??option.Id,AccessibleName="回答："+(option.Label??option.Id),Enabled=AnswerAsync!=null&&!answerInFlight&&(!attempted.ContainsKey(key)||retry)&&AutoloopSidebarControl.IsFresh(value,DateTimeOffset.UtcNow)};button.Click+=async(s,e)=>await Submit(value,selected);choices.Controls.Add(button);}
    }
    static string QuestionKey(AutoloopSnapshot value){return (value.QuestionId??"")+"\u001f"+(value.CardFingerprint??"");}
    async Task Submit(AutoloopSnapshot snapshot,AutoloopOption option){string key=QuestionKey(snapshot);bool retry=retryAllowed.Contains(key)&&attempted.ContainsKey(key)&&attempted[key].State!="confirmed";if(submitted||answerInFlight||(attempted.ContainsKey(key)&&!retry)||AnswerAsync==null||!AutoloopSidebarControl.IsFresh(snapshot,DateTimeOffset.UtcNow))return;submitted=true;answerInFlight=true;refreshButton.Enabled=false;retryAllowed.Remove(key);foreach(Control c in choices.Controls)c.Enabled=false;actionState.Visible=true;actionState.Text="正在提交，等待确认…";var result=new AutoloopActionResult();
      try{try{result=await AnswerAsync(snapshot.QuestionId,option.Id,snapshot.CardFingerprint,lifetime.Token)??new AutoloopActionResult();}catch(OperationCanceledException){result=new AutoloopActionResult{State="unknown",Message="提交已中断，结果未知"};}catch{result=new AutoloopActionResult{State="unknown",Message="提交结果未知，请刷新查看"};}
        attempted[key]=result;if(closing||IsDisposed)return;string state=result.State=="confirmed"?"已确认":result.State=="rejected"?"已拒绝":"结果未知";actionState.Text=state+" · "+(result.Message??"请刷新核对")+(result.State=="confirmed"?"":"；刷新核对后才能手动重试");actionState.ForeColor=result.State=="confirmed"?Palette.Accent:Palette.Secondary;
      }finally{answerInFlight=false;if(!closing&&!IsDisposed)refreshButton.Enabled=true;}
    }
    void PresentProjects(AutoloopSnapshotData data){if(data==null){projectSummary.Text="正在读取只读快照…";audiobookSummary.Text="正在读取只读观察…";return;}
      projectSummary.Text="进度：unknown · 聊天活动：unknown"+Environment.NewLine+"项目快照："+(data.State=="unknown"?"未知":Fresh(data.ObservedAt,data.Expired))+Environment.NewLine+"Hub 在线状态：未由此快照证明"+Environment.NewLine+data.Notice+Environment.NewLine+String.Join(Environment.NewLine,data.Projects.Select(x=>x.Name+" · "+(x.DirectoryState=="a"?"目录可见":x.DirectoryState=="m"?"目录缺失":"目录未知")+" · "+x.LatestDocumentDate));
      audiobookSummary.Text="序列状态："+(data.AudiobookState=="available"?Fresh(data.AudiobookObservedAt,data.AudiobookExpired):"未知")+Environment.NewLine+data.AudiobookNotice+Environment.NewLine+(data.Books.HasValue?"书目 "+data.Books+" · 计划章节 "+data.PlannedChapters+" · 尝试目录 "+data.AttemptDirectories+Environment.NewLine+data.StageSummary:"书目、章节与阶段标记未知");
    }
    async Task RefreshAll(){if(answerInFlight||RefreshAsync==null&&ReadProjectSnapshotsAsync==null)return;refreshButton.Enabled=false;foreach(Control c in choices.Controls)c.Enabled=false;actionState.Visible=true;actionState.Text="正在刷新；当前选择暂不可提交";try{if(RefreshAsync!=null){var current=await RefreshAsync(lifetime.Token);if(!closing&&!IsDisposed){if(current==null)throw new InvalidOperationException();UpdateBridge(current,true);}}if(ReadProjectSnapshotsAsync!=null){var data=await ReadProjectSnapshotsAsync(lifetime.Token);if(!closing&&!IsDisposed)PresentProjects(data??new AutoloopSnapshotData());}if(!closing&&!IsDisposed&&attempted.Count==0)actionState.Visible=false;}catch(OperationCanceledException){}catch{if(!closing&&!IsDisposed){actionState.Visible=true;actionState.Text="刷新失败；原问题身份保留且不可提交，请重试";actionState.ForeColor=Palette.Secondary;projectSummary.Text="刷新失败，保留上次快照显示";}}finally{if(!closing&&!IsDisposed&&!answerInFlight)refreshButton.Enabled=true;}}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Color.Transparent);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var path=GlassSurface.Path(new RectangleF(0,0,Width-1,Height-1),24)){GlassSurface.Paint(e.Graphics,new RectangleF(0,0,Width,Height),24,false);using(var pen=new Pen(Color.FromArgb(55,Palette.Text),1))e.Graphics.DrawPath(pen,path);}}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width>0&&Height>0){using(var path=GlassSurface.Path(new RectangleF(0,0,Width,Height),24)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}}
    protected override void Dispose(bool disposing){if(disposing){animation.Stop();animation.Dispose();animationPrecision.Dispose();if(lifetime!=null){lifetime.Dispose();lifetime=null;}}base.Dispose(disposing);}
  }

  internal sealed class StopwatchClock { readonly System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();public long Now {get{return watch.ElapsedMilliseconds;}} }
}
