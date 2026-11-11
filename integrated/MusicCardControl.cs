using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.Globalization;using System.Windows.Forms;using IslandMedia;
namespace IslandIntegrated {
 public static class PhoneGlass {
  // Cached 26px cover sample with bounded low-resolution Gaussian blur scaled to about 26 output pixels. No desktop capture, location or network.
  static double[] Kernel(double sigma){int radius=Math.Max(1,(int)Math.Ceiling(3*sigma));var kernel=new double[radius*2+1];double total=0;for(int i=-radius;i<=radius;i++){double weight=Math.Exp(-(i*i)/(2*sigma*sigma));kernel[i+radius]=weight;total+=weight;}for(int i=0;i<kernel.Length;i++)kernel[i]/=total;return kernel;}
  internal static Bitmap BlurSample(Bitmap sample,Size destination){int width=sample.Width,height=sample.Height,count=width*height;double sigmaX=Math.Max(.35,Math.Min(6,26.0*26/Math.Max(1,destination.Width+48))),sigmaY=Math.Max(.35,Math.Min(6,26.0*26/Math.Max(1,destination.Height+48)));var kx=Kernel(sigmaX);var ky=Kernel(sigmaY);var source=new double[count*4];var temp=new double[count*4];var filtered=new double[count*4];var output=new Bitmap(width,height);for(int y=0;y<height;y++)for(int x=0;x<width;x++){Color c=sample.GetPixel(x,y);int p=(y*width+x)*4;double a=c.A/255.0;source[p]=c.R*a;source[p+1]=c.G*a;source[p+2]=c.B*a;source[p+3]=c.A;}
   int rx=kx.Length/2,ry=ky.Length/2;for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=(y*width+x)*4;for(int ch=0;ch<4;ch++){double v=0;for(int k=-rx;k<=rx;k++){int xx=Math.Max(0,Math.Min(width-1,x+k));v+=source[(y*width+xx)*4+ch]*kx[k+rx];}temp[p+ch]=v;}}
   for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=(y*width+x)*4;for(int ch=0;ch<4;ch++){double v=0;for(int k=-ry;k<=ry;k++){int yy=Math.Max(0,Math.Min(height-1,y+k));v+=temp[(yy*width+x)*4+ch]*ky[k+ry];}filtered[p+ch]=v;}double alpha=filtered[p+3];int a=(int)Math.Round(Math.Max(0,Math.Min(255,alpha)));int r=alpha>0?(int)Math.Round(filtered[p]*255/alpha):0,gg=alpha>0?(int)Math.Round(filtered[p+1]*255/alpha):0,b=alpha>0?(int)Math.Round(filtered[p+2]*255/alpha):0;output.SetPixel(x,y,Color.FromArgb(a,Math.Max(0,Math.Min(255,r)),Math.Max(0,Math.Min(255,gg)),Math.Max(0,Math.Min(255,b))));}
   return output;}
  public static Bitmap Material(Size size,Image cover){
    if(cover==null||Palette.Solid||Palette.Legacy||SystemInformation.HighContrast)return GlassSurface.CardBackground(size);
    var image=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height));
    using(var sample=new Bitmap(26,26)){
      using(var g=Graphics.FromImage(sample)){
        g.InterpolationMode=InterpolationMode.HighQualityBilinear;
        float scale=Math.Max(26f/cover.Width,26f/cover.Height);
        float width=cover.Width*scale,height=cover.Height*scale;
        g.DrawImage(cover,(26-width)/2,(26-height)/2,width,height);
      }
      using(var blur=BlurSample(sample,size))using(var expanded=new Bitmap(size.Width+48,size.Height+48)){
        using(var g=Graphics.FromImage(expanded)){
          g.InterpolationMode=InterpolationMode.HighQualityBicubic;
          g.DrawImage(blur,new Rectangle(0,0,expanded.Width,expanded.Height));
        }
        using(var g=Graphics.FromImage(image))using(var body=GlassSurface.Path(new RectangleF(0,0,size.Width,size.Height),24))
        using(var texture=new TextureBrush(expanded,WrapMode.Clamp)){
          g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
          texture.TranslateTransform(-24,-24);g.FillPath(texture,body);
          using(var tint=new SolidBrush(Color.FromArgb(140,20,22,27)))g.FillPath(tint,body);
          StatusMaterial.Finish(g,new RectangleF(0,0,size.Width,size.Height),24,false);
        }
      }
    }
    return image;
  }

 }
 public sealed class MusicGeometry {
  public Rectangle Cover,Title,Artist,Lyrics,LyricsRefresh,Progress,Times,Elapsed,Duration,Availability,Previous,Toggle,Next;public float Scale;
  static Rectangle R(float s,float x,float y,float w,float h){return Rectangle.Round(new RectangleF(s*x,s*y,s*w,s*h));}
  public static MusicGeometry Create(int width,int height,bool square,bool editing){float s=Math.Max(.4f,Math.Min(width/(square?166f:344f),height/(square?166f:208f)));var g=new MusicGeometry{Scale=s};
   if(square){g.Cover=R(s,14,editing?46:14,44,44);g.Title=R(s,68,editing?47:17,width/s-81,21);g.Artist=R(s,68,editing?72:43,width/s-81,20);g.Progress=R(s,14,editing?121:85,width/s-28,4);g.Times=R(s,14,editing?129:94,width/s-28,16);g.Previous=R(s,14,117,38,38);g.Toggle=R(s,width/(2*s)-21,115,42,42);g.Next=R(s,width/s-52,117,38,38);}
   else{g.Cover=R(s,18,editing?43:18,68,68);g.Title=R(s,104,editing?45:21,width/s-122,26);g.Artist=R(s,104,editing?76:53,width/s-122,23);g.Progress=R(s,18,editing?145:110,width/s-36,4);g.Times=R(s,18,editing?157:121,width/s-36,19);float center=width/(2*s);g.Previous=R(s,center-104,147,42,42);g.Toggle=R(s,center-26,142,52,52);g.Next=R(s,center+62,147,42,42);}
   float lyricX=square?14:18,lyricY=editing?(square?99:113):(square?65:90),lyricW=width/s-(square?28:36),refreshSize=24,refreshGap=4;
   g.Lyrics=R(s,lyricX,lyricY,Math.Max(1,lyricW-refreshSize-refreshGap),editing&&!square?20:17);
   g.LyricsRefresh=R(s,width/s-(square?14:18)-refreshSize,lyricY-5,refreshSize,refreshSize);
   int slot=(int)Math.Round((square?50:60)*s);g.Elapsed=new Rectangle(g.Times.Left,g.Times.Top,slot,g.Times.Height);g.Duration=new Rectangle(g.Times.Right-slot,g.Times.Top,slot,g.Times.Height);g.Availability=new Rectangle(g.Elapsed.Right+2,g.Times.Top,Math.Max(1,g.Duration.Left-g.Elapsed.Right-4),g.Times.Height);return g;
  }
 }
 public static class MusicTimeline {
  public static double? Duration(Snapshot s){return s!=null&&s.DurationSeconds.HasValue&&s.DurationSeconds.Value>0&&s.DurationSeconds.Value<=86400&&!Double.IsNaN(s.DurationSeconds.Value)&&!Double.IsInfinity(s.DurationSeconds.Value)?s.DurationSeconds:null;}
  public static string DurationLabel(Snapshot s){var d=Duration(s);return d.HasValue?Time(d.Value):"--:--";}
  public static string PositionLabel(Snapshot s,DateTimeOffset now){var p=Position(s,now);return p.HasValue?Time(p.Value):"--:--";}
  // The timeline timestamp is the origin of Position, not a connectivity heartbeat.
  // Fresh native reads confirm playback separately. Missing observation data keeps
  // the conservative legacy bound for fixtures/older producers.
  public static bool CanAdvance(Snapshot s,DateTimeOffset now){
   if(s==null||!s.Playing||!s.PositionTimestamp.HasValue)return false;
   double age=(now-s.PositionTimestamp.Value).TotalSeconds;
   if(age<0||age>86400)return false;
   var observed=s.PlaybackObservedAt??s.PositionTimestamp;
   double heartbeat=(now-observed.Value).TotalSeconds;
   return heartbeat>=0&&heartbeat<=15;
  }
  public static double? Position(Snapshot s,DateTimeOffset now){
   if(s==null||!s.PositionSeconds.HasValue||s.PositionSeconds<0||s.PositionSeconds>86400||(Duration(s).HasValue&&s.PositionSeconds>Duration(s).Value)||Double.IsNaN(s.PositionSeconds.Value)||Double.IsInfinity(s.PositionSeconds.Value))return null;
   double value=s.PositionSeconds.Value;if(CanAdvance(s,now))value+=(now-s.PositionTimestamp.Value).TotalSeconds;
   return Math.Max(0,Math.Min(Duration(s)??86400,value));
  }
  public static double? LyricPosition(Snapshot s,DateTimeOffset now){var p=Position(s,now);return !p.HasValue||s.Playing&&!CanAdvance(s,now)?null:p;}
  public static string Time(double value){if(Double.IsNaN(value)||Double.IsInfinity(value)||value<0||value>86400)return "--:--";var t=TimeSpan.FromSeconds(value);return t.TotalHours>=1?((int)t.TotalHours)+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00"):((int)t.TotalMinutes)+":"+t.Seconds.ToString("00");}
 }

 public sealed class MusicControlButton:Button {
  public string Command;public bool Playing,Primary,RefreshAction,InProgress;bool hovered,pressed;
  public MusicControlButton(){SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;FlatAppearance.BorderSize=0;BackColor=Color.Transparent;Text="";TabStop=true;}
  protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
  protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&Enabled){pressed=true;Invalidate();}base.OnMouseDown(e);}
  protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
  protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture&&pressed){pressed=false;Invalidate();}base.OnMouseCaptureChanged(e);}
  protected override void OnKeyDown(KeyEventArgs e){if((e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)&&Enabled){pressed=true;Invalidate();}base.OnKeyDown(e);}
  protected override void OnKeyUp(KeyEventArgs e){if(pressed){pressed=false;Invalidate();}base.OnKeyUp(e);}
  // ButtonBase's themed/background path must never paint the media surface.
  // Compose in a new pixel-space buffer, then submit once using the native dirty
  // clip. This isolates inherited parent transforms, DPI and partial WM_PAINTs.
  protected override void OnPaintBackground(PaintEventArgs e){}
  Bitmap buttonFrame;
  protected override void OnPaint(PaintEventArgs e){if(Width<1||Height<1)return;
   if(buttonFrame==null||buttonFrame.Width!=Width||buttonFrame.Height!=Height){if(buttonFrame!=null)buttonFrame.Dispose();buttonFrame=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);}
   using(var g=Graphics.FromImage(buttonFrame)){g.PageUnit=GraphicsUnit.Pixel;g.Clear(Color.Transparent);var card=Parent as MusicCardControl;if(card!=null)card.PaintButtonBackdrop(g,Bounds);PaintGlyph(g);}
   var saved=e.Graphics.Save();try{e.Graphics.PageUnit=GraphicsUnit.Pixel;e.Graphics.PageScale=1;GlassComposition.DrawImageReplacing(e.Graphics,buttonFrame,0,0);}finally{e.Graphics.Restore(saved);}
  }
  protected override void OnSizeChanged(EventArgs e){if(buttonFrame!=null){buttonFrame.Dispose();buttonFrame=null;}base.OnSizeChanged(e);}
  protected override void Dispose(bool disposing){if(disposing&&buttonFrame!=null){buttonFrame.Dispose();buttonFrame=null;}base.Dispose(disposing);}
  void PaintGlyph(Graphics g){g.SmoothingMode=SmoothingMode.AntiAlias;float size=Math.Min(Width,Height),cx=Width/2f,cy=Height/2f,k=RefreshAction?size/24f:size/44f;
   if(size>5){var circle=new RectangleF(cx-size/2+2,cy-size/2+2,size-4,size-4);int a=Enabled?255:55;if(Primary){using(var shadow=new SolidBrush(Color.FromArgb(40,0,0,0)))g.FillEllipse(shadow,circle.X,circle.Y+1,circle.Width,circle.Height);using(var grad=new LinearGradientBrush(circle,Color.FromArgb(a,255,255,255),Color.FromArgb(a,pressed?145:166,pressed?155:183,pressed?171:205),LinearGradientMode.Vertical))g.FillEllipse(grad,circle);using(var edge=new Pen(Color.FromArgb(Enabled?170:20,255,255,255),1))g.DrawEllipse(edge,circle);if(circle.Width>10&&circle.Height>10){var shineState=g.Save();try{using(var mask=new GraphicsPath()){mask.AddEllipse(circle);g.SetClip(mask,CombineMode.Intersect);var top=new RectangleF(circle.X+3,circle.Y+2,circle.Width-6,circle.Height*.38f);using(var shine=new LinearGradientBrush(top,Color.FromArgb(Enabled?85:10,255,255,255),Color.FromArgb(0,255,255,255),LinearGradientMode.Vertical))g.FillEllipse(shine,top);}}finally{g.Restore(shineState);}}if(circle.Width>8&&circle.Height>8)using(var inner=new Pen(Color.FromArgb(Enabled?80:12,255,255,255),1))g.DrawArc(inner,circle.X+3,circle.Y+3,circle.Width-6,circle.Height-6,205,130);using(var lower=new Pen(Color.FromArgb(Enabled?65:10,45,60,78),1))g.DrawArc(lower,circle.X+1,circle.Y+1,circle.Width-2,circle.Height-2,30,120);}else{using(var grad=new LinearGradientBrush(circle,Color.FromArgb(Enabled?(hovered?50:28):9,255,255,255),Color.FromArgb(Enabled?(pressed?9:5):2,255,255,255),LinearGradientMode.Vertical))g.FillEllipse(grad,circle);using(var edge=new Pen(Color.FromArgb(Enabled?(hovered?75:40):12,255,255,255),1))g.DrawEllipse(edge,circle);}if(pressed&&Enabled)cy+=1*k;}
   Color color=!Enabled?Color.FromArgb(105,Palette.Secondary):Primary?Color.FromArgb(30,32,36):Palette.Text;using(var b=new SolidBrush(color))using(var p=new Pen(color,2*k)){if(RefreshAction){p.Width=Math.Max(1.2f,1.8f*k);p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;if(InProgress)g.DrawArc(p,cx-5*k,cy-5*k,10*k,10*k,35,270);else{g.DrawArc(p,cx-5*k,cy-5*k,10*k,10*k,45,285);g.FillPolygon(b,new[]{new PointF(cx+4.7f*k,cy-5.5f*k),new PointF(cx+5.2f*k,cy+0.5f*k),new PointF(cx+0.2f*k,cy-2.1f*k)});}}else if(Command=="toggle"&&Playing){g.FillRectangle(b,cx-6*k,cy-8*k,4*k,16*k);g.FillRectangle(b,cx+2*k,cy-8*k,4*k,16*k);}else{bool back=Command=="previous";float direction=back?-1:1;float gx=Command=="toggle"?cx:cx-direction*2.5f*k;g.FillPolygon(b,new[]{new PointF(gx-5*direction*k,cy-8*k),new PointF(gx-5*direction*k,cy+8*k),new PointF(gx+7*direction*k,cy)});if(Command!="toggle")g.DrawLine(p,gx+10*direction*k,cy-8*k,gx+10*direction*k,cy+8*k);}}
   if(Focused&&ShowFocusCues)using(var p=new Pen(Palette.Text,1)){p.DashStyle=DashStyle.Dot;g.DrawEllipse(p,2,2,Width-5,Height-5);}
  }
 }
 public sealed class MusicActivationPulse {
  public bool Active{get;private set;}public double Intensity{get;private set;}double began,next;bool fired,reduced;
  public bool Begin(double now,bool reducedMotion){if(Active||now<next)return false;began=now;next=now+380;fired=false;reduced=reducedMotion;Active=true;Intensity=0;return true;}
  public bool Step(double now){if(!Active)return false;double t=Math.Max(0,now-began);bool dispatch=!fired&&(reduced||t>=180);if(dispatch)fired=true;Intensity=reduced?0:Math.Sin(Math.PI*Math.Min(1,t/320));if(reduced||t>=320){Active=false;Intensity=0;}return dispatch;}
  public void Cancel(){Active=false;Intensity=0;fired=true;}
 }

 public sealed class MusicCardControl:Control {
  Snapshot snapshot=new Snapshot();LyricResult lyrics=new LyricResult("disabled");string clockPresentation="";Image cover;Bitmap material;readonly Timer clock=new Timer();readonly ToolTip lyricRefreshToolTip=new ToolTip();readonly Font big=new Font("Microsoft YaHei UI",16,FontStyle.Bold,GraphicsUnit.Pixel),smallTitle=new Font("Microsoft YaHei UI",16,FontStyle.Bold,GraphicsUnit.Pixel),artist=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel),smallArtist=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel),time=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel);
  bool lyricRefreshAvailable=true,lyricRefreshInProgress;
  public bool Square,Editing,Busy;public event Action<string> CommandRequested;public event EventHandler ActivationRequested;public event EventHandler LyricRefreshRequested;public Func<DateTimeOffset> Clock=()=>DateTimeOffset.UtcNow;
  public bool LyricRefreshAvailable {get{return lyricRefreshAvailable;}set{if(lyricRefreshAvailable==value)return;lyricRefreshAvailable=value;UpdateLyricRefreshButton();}}
  public bool LyricRefreshInProgress {get{return lyricRefreshInProgress;}set{if(lyricRefreshInProgress==value)return;lyricRefreshInProgress=value;UpdateLyricRefreshButton();}}
  public readonly MusicControlButton Previous=new MusicControlButton{Command="previous",AccessibleName="上一首"},Toggle=new MusicControlButton{Command="toggle",Primary=true,AccessibleName="播放"},Next=new MusicControlButton{Command="next",AccessibleName="下一首"},LyricsRefresh=new MusicControlButton{Command="lyrics-refresh",RefreshAction=true,AccessibleName="刷新歌词",AccessibleRole=AccessibleRole.PushButton,TabStop=true};
  public MusicCardControl(){activationTimer.Tick+=(s,e)=>StepActivation();TabStop=true;SetStyle(ControlStyles.Selectable,true);SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.Transparent;AccessibleName="网易云音乐";Controls.Add(Previous);Controls.Add(Toggle);Controls.Add(Next);Controls.Add(LyricsRefresh);lyricRefreshToolTip.SetToolTip(LyricsRefresh,"刷新歌词");LyricsRefresh.Click+=(s,e)=>{if(!Editing&&LyricRefreshAvailable&&!LyricRefreshInProgress){var handler=LyricRefreshRequested;if(handler!=null)handler(this,EventArgs.Empty);}};foreach(var button in new[]{Previous,Toggle,Next}){var item=button;item.Click+=(s,e)=>{if(!Editing&&!Busy&&item.Enabled){var action=CommandRequested;if(action!=null)action(item.Command);}};}UpdateLyricRefreshButton();clock.Tick+=(s,e)=>RefreshPlaybackFrame();}
  void UpdateLyricRefreshButton(){if(LyricsRefresh==null)return;LyricsRefresh.Enabled=LyricRefreshAvailable&&!LyricRefreshInProgress;LyricsRefresh.InProgress=LyricRefreshInProgress;LyricsRefresh.AccessibleName=LyricRefreshInProgress?"正在刷新歌词":"刷新歌词";LyricsRefresh.AccessibleDescription=LyricRefreshInProgress?"歌词刷新正在进行":"手动刷新当前歌曲歌词";lyricRefreshToolTip.SetToolTip(LyricsRefresh,LyricRefreshInProgress?"正在刷新歌词":LyricRefreshAvailable?"刷新歌词":"歌词刷新暂不可用");LyricsRefresh.Invalidate();}
  double lastFramePosition=Double.NaN;bool playbackClosing;
  void UpdatePlaybackClock(){if(clock==null||playbackClosing)return;bool run=Visible&&!Editing&&snapshot.Playing&&MusicTimeline.LyricPosition(snapshot,Clock()).HasValue;clock.Interval=ReduceActivationMotion()?250:100;if(run){if(!clock.Enabled)clock.Start();}else clock.Stop();}
  void RefreshPlaybackFrame(){if(playbackClosing||!Visible||Editing||!snapshot.Playing){UpdatePlaybackClock();return;}var now=Clock();if(!MusicTimeline.LyricPosition(snapshot,now).HasValue){UpdatePlaybackClock();return;}var position=seekDragging?(double?)seekPreview:MusicTimeline.Position(snapshot,now);string presentation=(seekDragging?MusicTimeline.Time(seekPreview):MusicTimeline.PositionLabel(snapshot,now))+"|"+LrcText.Display(lyrics,MusicTimeline.LyricPosition(snapshot,now));if(presentation!=clockPresentation){clockPresentation=presentation;lastFramePosition=position??Double.NaN;Invalidate();return;}var duration=MusicTimeline.Duration(snapshot);int width=Geometry.Progress.Width;if(position.HasValue&&duration.HasValue&&width>0&&(Double.IsNaN(lastFramePosition)||(int)(position.Value*width/duration.Value)!=(int)(lastFramePosition*width/duration.Value))){lastFramePosition=position.Value;Invalidate(Rectangle.Inflate(Geometry.Progress,2,2));}if(!position.HasValue)UpdatePlaybackClock();}
  void PaintProgress(Graphics g,MusicGeometry geometry,DateTimeOffset now){using(var track=new SolidBrush(Color.FromArgb(38,255,255,255)))using(var path=GlassSurface.Path(geometry.Progress,2))g.FillPath(track,path);var p=seekDragging?(double?)seekPreview:MusicTimeline.Position(snapshot,now);if(p.HasValue&&MusicTimeline.Duration(snapshot).HasValue){float width=(float)(geometry.Progress.Width*p.Value/MusicTimeline.Duration(snapshot).Value);if(width>=1)using(var fill=new SolidBrush(Color.FromArgb(221,245,247,250)))using(var path=GlassSurface.Path(new RectangleF(geometry.Progress.X,geometry.Progress.Y,width,geometry.Progress.Height),2))g.FillPath(fill,path);}}

  public MusicGeometry Geometry {get{return MusicGeometry.Create(Width,Height,Square,Editing);}}
  static string Line(string s){return (s??"").Replace('\r',' ').Replace('\n',' ').Replace('\t',' ').Trim();}
  public void Present(Snapshot value,Image artwork){
   value=value??new Snapshot();if(seekDragging&&NeteaseSource.TrackKey(value)!=seekTrack)CancelSeek();var old=snapshot;
   bool materialChanged=!ReferenceEquals(cover,artwork);bool contentChanged=materialChanged||old.SourceId!=value.SourceId||old.Title!=value.Title||old.Artist!=value.Artist||old.Album!=value.Album||old.Detail!=value.Detail;
   bool timelineChanged=old.PositionSeconds!=value.PositionSeconds||old.PositionTimestamp!=value.PositionTimestamp||old.DurationSeconds!=value.DurationSeconds||old.Playing!=value.Playing;
   if(materialChanged){cover=artwork;ClearMaterial();}snapshot=value;
   AccessibleDescription=Line(snapshot.Title)+" - "+Line(snapshot.Artist)+"; "+MusicTimeline.PositionLabel(snapshot,Clock())+" / "+MusicTimeline.DurationLabel(snapshot)+"; "+LrcText.Display(lyrics,MusicTimeline.LyricPosition(snapshot,Clock()));UpdateButtons();
   if(contentChanged||timelineChanged){lastFramePosition=Double.NaN;Invalidate();}UpdatePlaybackClock();
  }
  public void PresentLyrics(LyricResult value){if(ReferenceEquals(lyrics,value))return;lyrics=value??new LyricResult("disabled");AccessibleDescription=Line(snapshot.Title)+" - "+Line(snapshot.Artist)+"; "+LrcText.Display(lyrics,MusicTimeline.LyricPosition(snapshot,Clock()));clockPresentation="";Invalidate();}
  public void SetBusy(bool value){Busy=value;UpdateButtons();}
  public Func<bool> ReduceActivationMotion=()=>SystemInformation.HighContrast||!SystemInformation.UIEffectsEnabled;
  public Func<double> ActivationClock=()=>System.Diagnostics.Stopwatch.GetTimestamp()*1000d/System.Diagnostics.Stopwatch.Frequency;
  readonly MusicActivationPulse activationPulse=new MusicActivationPulse();readonly Timer activationTimer=new Timer{Interval=16};
  public bool RequestActivation(){if(Editing||Busy||IsDisposed||!Visible||!activationPulse.Begin(ActivationClock(),ReduceActivationMotion()))return false;StepActivation();if(activationPulse.Active)activationTimer.Start();return true;}
  void StepActivation(){if(IsDisposed||Editing||!Visible){activationPulse.Cancel();activationTimer.Stop();return;}bool dispatch=activationPulse.Step(ActivationClock());Invalidate(Rectangle.Union(Rectangle.Inflate(Geometry.Cover,4,4),Geometry.Artist));if(!activationPulse.Active)activationTimer.Stop();if(dispatch){var h=ActivationRequested;if(h!=null)try{h(this,EventArgs.Empty);}catch{ReportActivationFailure();}}}
  void PaintActivation(Graphics g){if(activationPulse.Intensity<=0)return;var r=Rectangle.Inflate(Geometry.Cover,2,2);using(var path=GlassSurface.Path(r,Math.Min(24f,Math.Min(Geometry.Cover.Width,Geometry.Cover.Height)/2f)))using(var pen=new Pen(Color.FromArgb((int)(160*activationPulse.Intensity),245,248,255),1.5f))g.DrawPath(pen,path);}
  protected override void OnVisibleChanged(EventArgs e){if(clock!=null)clock.Stop();if(!Visible&&activationTimer!=null){activationPulse.Cancel();activationTimer.Stop();}base.OnVisibleChanged(e);if(playbackClosing||!Visible)return;if(IsHandleCreated){try{BeginInvoke(new Action(UpdatePlaybackClock));}catch(InvalidOperationException){}}else UpdatePlaybackClock();}

  bool seekDragging,suppressActivation;double seekPreview;string seekTrack;public bool IsSeeking{get{return seekDragging;}}
  public event Action<double,string> SeekRequested;
  Rectangle SeekHit {get{var r=Geometry.Progress;r.Inflate(4,8);return r;}}
  bool SeekReady {get{return !Editing&&!Busy&&!String.IsNullOrEmpty(snapshot.SourceId)&&snapshot.CanSeek&&MusicTimeline.Duration(snapshot).HasValue;}}
  public bool BodyHit(Point p){var g=Geometry;return ClientRectangle.Contains(p)&&!g.Previous.Contains(p)&&!g.Toggle.Contains(p)&&!g.Next.Contains(p)&&!g.LyricsRefresh.Contains(p)&&!SeekHit.Contains(p)&&!g.Times.Contains(p);}
  double SeekPoint(Point p){var g=Geometry;return Math.Max(0,Math.Min(1,(p.X-g.Progress.Left)/(double)Math.Max(1,g.Progress.Width)))*MusicTimeline.Duration(snapshot).GetValueOrDefault();}
  void InvalidateTimeline(){Invalidate(Rectangle.Union(Rectangle.Inflate(Geometry.Progress,5,9),Geometry.Times));}
  public bool CancelSeek(){if(!seekDragging)return false;seekDragging=false;suppressActivation=true;Capture=false;InvalidateTimeline();return true;}
  public void ReportActivationFailure(){AccessibleDescription="未能唤起现有网易云窗口；没有重复启动播放器。";}
  protected override void OnMouseDown(MouseEventArgs e){suppressActivation=false;base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||Editing)return;if(SeekHit.Contains(e.Location)||Geometry.Times.Contains(e.Location)){suppressActivation=true;if(SeekReady&&SeekHit.Contains(e.Location)){seekDragging=true;seekTrack=NeteaseSource.TrackKey(snapshot);seekPreview=SeekPoint(e.Location);Capture=true;Focus();InvalidateTimeline();}}}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(seekDragging){if((e.Button&MouseButtons.Left)==0){CancelSeek();return;}seekPreview=SeekPoint(e.Location);InvalidateTimeline();}else Cursor=SeekReady&&SeekHit.Contains(e.Location)?Cursors.Hand:Cursors.Default;}
  protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(seekDragging&&!Capture)CancelSeek();}
  protected override void OnMouseUp(MouseEventArgs e){if(Editing){base.OnMouseUp(e);return;}if(e.Button==MouseButtons.Left&&(seekDragging||suppressActivation||SeekHit.Contains(e.Location)||Geometry.Times.Contains(e.Location))){bool send=seekDragging&&ClientRectangle.Contains(e.Location)&&SeekReady&&NeteaseSource.TrackKey(snapshot)==seekTrack;double value=seekPreview;string track=seekTrack;seekDragging=false;suppressActivation=false;Capture=false;InvalidateTimeline();base.OnMouseUp(e);if(send){var action=SeekRequested;if(action!=null)action(value,track);}return;}base.OnMouseUp(e);if(e.Button==MouseButtons.Left&&BodyHit(e.Location))RequestActivation();}
  protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Escape&&CancelSeek()){e.Handled=true;e.SuppressKeyPress=true;return;}base.OnKeyDown(e);}

  void UpdateButtons(){var g=Geometry;if(Previous.Bounds!=g.Previous)Previous.Bounds=g.Previous;if(Toggle.Bounds!=g.Toggle)Toggle.Bounds=g.Toggle;if(Next.Bounds!=g.Next)Next.Bounds=g.Next;if(LyricsRefresh.Bounds!=g.LyricsRefresh)LyricsRefresh.Bounds=g.LyricsRefresh;Previous.Visible=Toggle.Visible=Next.Visible=!Editing;LyricsRefresh.Visible=!Editing;bool connected=!String.IsNullOrEmpty(snapshot.SourceId);Previous.Enabled=connected&&snapshot.CanPrevious;Toggle.Enabled=connected&&snapshot.CanToggle;Next.Enabled=connected&&snapshot.CanNext;if(Toggle.Playing!=snapshot.Playing){Toggle.Playing=snapshot.Playing;Toggle.Invalidate();}Toggle.AccessibleName=snapshot.Playing?"暂停":"播放";}
  void ClearMaterial(){if(material!=null){material.Dispose();material=null;}}
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ClearMaterial();if(Toggle!=null)UpdateButtons();}
  Bitmap Material(){return PhoneGlass.Material(Size,cover);}
  internal void PaintButtonBackdrop(Graphics g,Rectangle bounds){
   if(material==null)material=Material();GlassComposition.DrawImageReplacing(g,material,-bounds.X,-bounds.Y);var panel=Parent as WidgetPanel;if(panel==null)return;var saved=g.Save();try{g.TranslateTransform(-bounds.X,-bounds.Y);panel.PaintMaterialLight(g);}finally{g.Restore(saved);}
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<2||Height<2)return;var g=e.Graphics;var panel=Parent as WidgetPanel;if(material==null)material=Material();GlassComposition.DrawImageReplacing(g,material,0,0);if(panel!=null)panel.PaintMaterialLight(g);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;var m=Geometry;var progressClip=Rectangle.Inflate(m.Progress,2,2);if(progressClip.Contains(e.ClipRectangle)){PaintProgress(g,m,Clock());return;}if(Editing)EditCardChrome.Paint(g,"网易云",Width,m.Scale);
   float coverRadius=Math.Min(24f,Math.Min(m.Cover.Width,m.Cover.Height)/2f);using(var clip=GlassSurface.Path(m.Cover,coverRadius)){var saved=g.Save();g.SetClip(clip,CombineMode.Intersect);if(cover!=null){float ratio=Math.Max(m.Cover.Width/(float)cover.Width,m.Cover.Height/(float)cover.Height);float w=cover.Width*ratio,h=cover.Height*ratio;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(cover,m.Cover.X+(m.Cover.Width-w)/2,m.Cover.Y+(m.Cover.Height-h)/2,w,h);}else{using(var b=new SolidBrush(Color.FromArgb(36,255,255,255)))g.FillRectangle(b,m.Cover);using(var p=new Pen(Palette.Secondary,1.7f)){float x=m.Cover.X+m.Cover.Width*.57f,y=m.Cover.Y+m.Cover.Height*.28f,h=m.Cover.Height*.38f;g.DrawLine(p,x,y,x,y+h);g.DrawLine(p,x,y,x+8,y-2);g.DrawEllipse(p,x-9,y+h-2,9,6);}}g.Restore(saved);}
   PaintActivation(g);
   using(var text=new SolidBrush(Palette.Text))using(var muted=new SolidBrush(Palette.Secondary))using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter}){string title=String.IsNullOrEmpty(snapshot.SourceId)?"网易云音乐":Line(snapshot.Title);string by=activationPulse.Active?"正在打开网易云音乐…":String.IsNullOrEmpty(snapshot.SourceId)?Line(snapshot.Detail):Line(snapshot.Artist);g.DrawString(title,Square?smallTitle:big,text,m.Title,format);g.DrawString(by,Square?smallArtist:artist,muted,m.Artist,format);
    using(var lyricFont=new Font("Microsoft YaHei UI",11.5f*m.Scale,FontStyle.Regular,GraphicsUnit.Pixel))using(var lyricFormat=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter,LineAlignment=StringAlignment.Center})g.DrawString(LrcText.Display(lyrics,MusicTimeline.LyricPosition(snapshot,Clock())),lyricFont,muted,m.Lyrics,lyricFormat);
    PaintProgress(g,m,Clock());var position=seekDragging?(double?)seekPreview:MusicTimeline.Position(snapshot,Clock());
    using(var timelineFont=new Font("Microsoft YaHei UI",12*m.Scale,FontStyle.Regular,GraphicsUnit.Pixel))using(var left=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,LineAlignment=StringAlignment.Center})using(var right=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Center})using(var center=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter}){
     g.DrawString(seekDragging?MusicTimeline.Time(seekPreview):MusicTimeline.PositionLabel(snapshot,Clock()),timelineFont,text,m.Elapsed,left);g.DrawString(MusicTimeline.DurationLabel(snapshot),timelineFont,text,m.Duration,right);
     if(seekDragging)g.DrawString("预览",timelineFont,muted,m.Availability,center);else if(!position.HasValue)g.DrawString(String.IsNullOrEmpty(snapshot.SourceId)?(Square?"\u65ad\u5f00":"\u672a\u8fde\u63a5"):Square?"\u6682\u65e0":"\u64ad\u653e\u5668\u672a\u63d0\u4f9b\u8fdb\u5ea6",timelineFont,muted,m.Availability,center);
    }
   }
  }
  protected override void Dispose(bool disposing){if(disposing){playbackClosing=true;activationPulse.Cancel();activationTimer.Stop();activationTimer.Dispose();clock.Stop();clock.Dispose();lyricRefreshToolTip.Dispose();ClearMaterial();big.Dispose();smallTitle.Dispose();artist.Dispose();smallArtist.Dispose();time.Dispose();}base.Dispose(disposing);}
 }
}
