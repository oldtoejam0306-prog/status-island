using System;using System.Linq;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
namespace IslandIntegrated {
 public sealed class PopupSpring {
  public double Amount{get;private set;}public double Velocity{get;private set;}public bool Moving{get;private set;}public bool Opening{get;private set;}double last;
  public void Target(bool open,double now,bool reduced){Opening=open;last=now;if(reduced){Amount=open?1:0;Velocity=0;Moving=false;}else Moving=true;}
  public bool Step(double now){if(!Moving)return false;double dt=Math.Max(0,Math.Min(.05,(now-last)/1000));last=now;double target=Opening?1:0;while(dt>0){double h=Math.Min(.008,dt);Velocity+=(460*(target-Amount)-29*Velocity)*h;Amount+=Velocity*h;dt-=h;}if(Math.Abs(target-Amount)<.002&&Math.Abs(Velocity)<.025){Amount=target;Velocity=0;Moving=false;}return true;}
 }
 public sealed class RouteDropdown:ToolStripDropDownMenu {
  sealed class Skin:ToolStripProfessionalRenderer,IDisposable {
   Bitmap material;public Skin(){RoundedEdges=false;}
   public void Dispose(){if(material!=null){material.Dispose();material=null;}}
   protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){var size=e.ToolStrip.Size;if(material==null||material.Size!=size){Dispose();material=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height));using(var g=Graphics.FromImage(material)){g.Clear(Palette.Base);GlassSurface.Paint(g,new RectangleF(0,0,size.Width-1,size.Height-1),16,true);}}e.Graphics.DrawImageUnscaled(material,0,0);}
   protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=GlassSurface.Path(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),16))using(var pen=new Pen(Color.FromArgb(45,Palette.Text),1))e.Graphics.DrawPath(pen,p);}
   protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){var item=e.Item as ToolStripMenuItem;int alpha=e.Item.Selected&&e.Item.Enabled?35:item!=null&&item.Checked?17:0;if(alpha==0)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=GlassSurface.Path(new RectangleF(4,2,e.Item.Width-8,e.Item.Height-4),11))using(var b=new SolidBrush(Color.FromArgb(alpha,Palette.Text)))e.Graphics.FillPath(b,p);}
   protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Palette.Text,1.7f)){pen.StartCap=pen.EndCap=LineCap.Round;float x=e.ImageRectangle.Left+3,y=e.ImageRectangle.Top+e.ImageRectangle.Height/2f;e.Graphics.DrawLine(pen,x,y,x+4,y+4);e.Graphics.DrawLine(pen,x+4,y+4,x+11,y-4);}}
   protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,e.Item.Enabled?Palette.Text:Palette.Secondary,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
   protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
  }
  readonly NetworkReadonlySnapshot data;readonly Func<DateTimeOffset> clock;readonly bool selectionEnabled;readonly Skin skin=new Skin();readonly Timer animation=new Timer{Interval=15};readonly PopupSpring motion=new PopupSpring();readonly Font ownedFont;Point anchor;bool forceClose;ToolStripDropDownCloseReason closeReason;
  public Func<bool> ReduceMotion=()=>SystemInformation.HighContrast||!SystemInformation.UIEffectsEnabled;
  public Func<double> AnimationClock=()=>System.Diagnostics.Stopwatch.GetTimestamp()*1000d/System.Diagnostics.Stopwatch.Frequency;
  public bool MotionActive{get{return motion.Moving;}}public bool Dismissing{get{return Visible&&!motion.Opening;}}
  public event Action<string,string> SelectionRequested;
  public RouteDropdown(NetworkReadonlySnapshot value,int width,bool canSelect=false,Func<DateTimeOffset> utc=null){data=value??new NetworkReadonlySnapshot();clock=utc??(()=>DateTimeOffset.UtcNow);selectionEnabled=canSelect;AutoClose=true;ShowImageMargin=false;ShowCheckMargin=true;ShowItemToolTips=true;Renderer=skin;BackColor=Palette.Base;ForeColor=Palette.Text;ownedFont=new Font("Microsoft YaHei UI",10,FontStyle.Regular);Font=ownedFont;Padding=new Padding(6,8,6,8);MinimumSize=new Size(Math.Max(220,width),0);MaximumSize=new Size(Math.Max(220,width),360);AccessibleName="线路节点";animation.Tick+=(s,e)=>Advance();
   if(data.CanPreview(clock())){foreach(string name in data.Choices.Take(128)){if(String.IsNullOrWhiteSpace(name)||name.Any(Char.IsControl))continue;var item=new ToolStripMenuItem(name){AutoSize=false,Size=new Size(Math.Max(208,width-12),40),Checked=name==data.Current,CheckOnClick=false,Enabled=selectionEnabled||name==data.Current,ToolTipText=selectionEnabled?name:name+" · 只读，尚未启用线路切换"};string exact=name;item.Click+=(s,e)=>{if(!selectionEnabled||!data.CanPreview(clock())||exact==data.Current)return;var request=SelectionRequested;if(request!=null)request(data.Group,exact);};Items.Add(item);}}
   if(Items.Count==0)Items.Add(new ToolStripMenuItem(data.RouteLabel(clock())){Enabled=false,AutoSize=false,Size=new Size(Math.Max(208,width-12),40)});
   var test=new ToolStripMenuItem("测试网站耗时（Google / Microsoft / Apple）"){AutoSize=false,Size=new Size(Math.Max(208,width-12),40),ToolTipText="按现有系统网络规则访问三个网站；结果不是指定节点的延迟"};test.Click+=(s,e)=>WebsiteLatency.Start();Items.Add(test);
  }
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<2||Height<2)return;using(var p=GlassSurface.Path(new RectangleF(0,0,Width,Height),16)){var old=Region;Region=new Region(p);if(old!=null)old.Dispose();}}
  protected override void OnOpening(System.ComponentModel.CancelEventArgs e){Opacity=ReduceMotion()?1:.01;base.OnOpening(e);}
  protected override void OnOpened(EventArgs e){base.OnOpened(e);anchor=Location;motion.Target(true,AnimationClock(),ReduceMotion());Advance();if(motion.Moving)animation.Start();}
  protected override void OnClosing(ToolStripDropDownClosingEventArgs e){base.OnClosing(e);if(e.Cancel||forceClose||ReduceMotion())return;e.Cancel=true;closeReason=e.CloseReason;if(!motion.Opening&&motion.Moving)return;motion.Target(false,AnimationClock(),false);animation.Start();}
  protected override void OnClosed(ToolStripDropDownClosedEventArgs e){animation.Stop();Opacity=1;Location=anchor;base.OnClosed(e);}
  void Advance(){motion.Step(AnimationClock());double amount=Math.Max(0,Math.Min(1,motion.Amount));Opacity=Math.Max(.01,amount);var location=new Point(anchor.X,anchor.Y+(int)Math.Round(12*(1-motion.Amount)));if(Location!=location)Location=location;if(!motion.Moving){animation.Stop();if(!motion.Opening&&Visible){forceClose=true;try{Close(closeReason);}finally{forceClose=false;}}}}
  public void Toggle(){if(Dismissing){motion.Target(true,AnimationClock(),ReduceMotion());Advance();if(motion.Moving)animation.Start();}else Close();}
  public void ShowAt(Control owner,Rectangle row){var point=owner.PointToScreen(new Point(row.Left,row.Bottom+6));var area=Screen.FromControl(owner).WorkingArea;point.X=Math.Max(area.Left,Math.Min(point.X,area.Right-PreferredSize.Width));if(point.Y+PreferredSize.Height>area.Bottom)point.Y=owner.PointToScreen(new Point(row.Left,row.Top)).Y-PreferredSize.Height-6;point.Y=Math.Max(area.Top,Math.Min(point.Y,area.Bottom-PreferredSize.Height-14));Show(point);}
  protected override void Dispose(bool disposing){if(disposing){forceClose=true;animation.Stop();animation.Dispose();skin.Dispose();base.Dispose(disposing);ownedFont.Dispose();return;}base.Dispose(disposing);}
 }
}
