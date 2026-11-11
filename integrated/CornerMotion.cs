using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace IslandIntegrated {
 public static class Palette {
  public static bool Legacy;public static bool Solid;public static string Theme="tide";
  public static string ValidTheme(string value){return value=="pine"||value=="iris"||value=="amber"?value:"tide";}
  static Color Pick(Color tide,Color pine,Color iris,Color amber){switch(ValidTheme(Theme)){case "pine":return pine;case "iris":return iris;case "amber":return amber;default:return tide;}}
  public static Color Base {get{return Legacy?Color.FromArgb(18,29,43):Pick(Color.FromArgb(28,47,62),Color.FromArgb(25,53,44),Color.FromArgb(49,37,60),Color.FromArgb(55,43,35));}}
  public static Color Top {get{return Legacy?Color.FromArgb(34,52,72):Pick(Color.FromArgb(48,74,92),Color.FromArgb(45,79,65),Color.FromArgb(73,57,88),Color.FromArgb(82,65,53));}}
  public static Color Face {get{return Pick(Color.FromArgb(142,188,220),Color.FromArgb(139,199,162),Color.FromArgb(190,157,218),Color.FromArgb(214,179,131));}}
  public static Color Text {get{return Pick(Color.FromArgb(238,245,247),Color.FromArgb(238,245,247),Color.FromArgb(244,238,249),Color.FromArgb(247,239,230));}}
  public static Color Secondary {get{return Pick(Color.FromArgb(195,211,223),Color.FromArgb(195,216,207),Color.FromArgb(207,194,220),Color.FromArgb(219,201,182));}}
  public static Color Accent {get{return Legacy?Color.FromArgb(107,229,160):Pick(Color.FromArgb(107,174,232),Color.FromArgb(106,187,154),Color.FromArgb(186,147,216),Color.FromArgb(213,165,117));}}
  public static Color SelectedText {get{return Color.FromArgb(20,30,35);}}

 }
 public sealed class StatusHeightMotion {
  double from,target;long started;int duration;public double Value {get;private set;}
  public StatusHeightMotion(double initial){Value=from=target=initial;}
  public bool Moving {get{return duration>0;}}
  void Advance(long now){if(duration==0){Value=target;return;}double t=Math.Max(0,Math.Min(1,(now-started)/(double)duration));double eased=target<from?t*t*(3-2*t):1-Math.Pow(1-t,3);Value=from+(target-from)*eased;if(t>=1){Value=target;duration=0;}}
  public double Step(double goal,long now,bool reduced){Advance(now);if(reduced){Value=from=target=goal;duration=0;return Value;}if(goal!=target){from=Value;target=goal;started=now;duration=goal>Value?220:320;}return Value;}
 }
 public static class CornerLayout {
  public const int Width=360,CompactHeight=80,Gap=24;
  public static Rectangle Body(Rectangle area,int height,int shadow,int scroll){
   int width=Math.Min(Width,Math.Max(1,area.Width-2*shadow));int inset=shadow+Math.Max(0,scroll);
   return new Rectangle(Math.Max(area.Left,area.Right-inset-width),area.Top+shadow,width,height);
  }
  public static int MaximumStatusHeight(Rectangle area,int shadow){
   return Math.Max(CompactHeight,area.Height-2*shadow-Gap-96);
  }
  public static Rectangle Column(Rectangle area,int shadow,int scroll){return Column(area,shadow,scroll,CompactHeight);}
  public static Rectangle Column(Rectangle area,int shadow,int scroll,int statusHeight){
   var bar=Body(area,Math.Min(Math.Max(0,statusHeight),MaximumStatusHeight(area,shadow)),shadow,scroll);
   int top=bar.Bottom+(int)Math.Round(Gap*Math.Min(1,Math.Max(0,statusHeight)/(double)CompactHeight));
   return new Rectangle(bar.Left,top,bar.Width,Math.Max(1,area.Bottom-top-shadow));
  }
 }
 public sealed class StatusDockState {
  public bool Hidden {get;private set;}public void Toggle(){Hidden=!Hidden;}public bool HandleMode(int allocatedHeight){return Hidden||allocatedHeight<CornerLayout.CompactHeight;}
  public static Size HandleSize(int dpi){float scale=Math.Max(.75f,Math.Min(3,dpi/96f));return new Size((int)Math.Round(28*scale),(int)Math.Round(56*scale));}
  public static Rectangle HandleBody(Rectangle area){return HandleBody(area,96);}
  public static Rectangle HandleBody(Rectangle area,int dpi){var size=HandleSize(dpi);int w=Math.Max(1,Math.Min(size.Width,area.Width)),h=Math.Max(1,Math.Min(size.Height,area.Height));return new Rectangle(area.Right-w,Math.Max(area.Top,Math.Min(area.Top+(int)Math.Round(96*dpi/96f),area.Bottom-h)),w,h);}
  public static Rectangle CollapseButton(int width){return new Rectangle(Math.Max(0,width-32),7,24,24);}
  public static Bitmap HandleRaster(int margin){return HandleRaster(margin,96,0);}
  public static Bitmap HandleRaster(int margin,int dpi,float hover){var size=HandleSize(dpi);float s=size.Height/56f;var image=new Bitmap(size.Width+2*margin,size.Height+2*margin);using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;using(var hit=new SolidBrush(Color.FromArgb(2,255,255,255)))g.FillRectangle(hit,margin,margin,size.Width,size.Height);float w=(9+5*Math.Max(0,Math.Min(1,hover)))*s;var r=new RectangleF(margin+size.Width-w,margin+5*s,w,size.Height-10*s);using(var path=GlassSurface.Path(r,7*s))using(var fill=new SolidBrush(Color.FromArgb((int)(150+40*hover),48,51,57)))using(var edge=new Pen(Color.FromArgb((int)(65+65*hover),244,246,250),.7f*s)){g.FillPath(fill,path);g.DrawPath(edge,path);}using(var p=new Pen(Color.FromArgb((int)(150+80*hover),245,247,250),1.5f*s)){p.StartCap=p.EndCap=LineCap.Round;float x=r.Left+r.Width*.55f,y=r.Top+r.Height*.5f;g.DrawLines(p,new[]{new PointF(x+1.5f*s,y-4*s),new PointF(x-1.5f*s,y),new PointF(x+1.5f*s,y+4*s)});}}return image;}
  public static void PaintCollapse(Graphics g,int width,int margin){var r=CollapseButton(width);r.Offset(margin,margin);using(var p=new Pen(Color.FromArgb(185,244,246,250),1.4f)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLines(p,new[]{new PointF(r.Left+9,r.Top+8),new PointF(r.Left+13,r.Top+12),new PointF(r.Left+9,r.Top+16)});}}
 }
 public sealed class PointerLight {
  long last=-33;public float X {get;private set;}public float Y {get;private set;}public float Alpha {get;private set;}
  public bool Tick(long now,PointF point,bool inside,bool enabled){
   if(!enabled){bool changed=Alpha>0;Alpha=0;last=now;return changed;}
   if(now-last<33)return false;last=now;
   float x=X,y=Y,a=Alpha,target=inside?1:0;
   if(inside){if(Alpha==0){X=point.X;Y=point.Y;}else{X+=(point.X-X)*.45f;Y+=(point.Y-Y)*.45f;}if(Math.Abs(point.X-X)<.35f)X=point.X;if(Math.Abs(point.Y-Y)<.35f)Y=point.Y;}
   Alpha+=(target-Alpha)*.42f;if(Math.Abs(target-Alpha)<.005f)Alpha=target;
   return X!=x||Y!=y||Alpha!=a;
  }
 }
 public sealed class HighlightClock {
  long last=-100,start;bool running;
  public double Phase {get;private set;}
  public bool Tick(long now,bool visible,bool reduced,bool busy){
   if(!visible||reduced||busy){running=false;Phase=0;last=now;return false;}
   if(!running){running=true;start=now;last=now-100;}
   if(now-last<100)return false;last=now;Phase=((now-start)%10000)/10000.0;return true;
  }
 }
 public sealed class StatusFrameCache : IDisposable {
  Bitmap image,composited;readonly int margin;public int BaseUpdates {get;private set;}public int Compositions {get;private set;}
  public StatusFrameCache(int shadow){margin=shadow;}
  public bool MatchesSize(Size expected){return image!=null&&image.Size==expected;}
  public void Update(Bitmap source){var next=(Bitmap)source.Clone();if(image!=null)image.Dispose();image=next;BaseUpdates++;}
  public Bitmap Compose(double phase,bool animated){if(image==null)return null;var next=(Bitmap)image.Clone();float height=image.Height-2*margin,radius=Math.Min(height/2,24);if(animated)using(var g=Graphics.FromImage(next))GlassSurface.PaintShimmer(g,new RectangleF(margin,margin,image.Width-2*margin,height),radius,phase);Compositions++;return next;}
  public Bitmap ComposePointer(PointerLight light){
   if(image==null)return null;
   if(composited==null||composited.Size!=image.Size){if(composited!=null)composited.Dispose();composited=new Bitmap(image.Width,image.Height);}
   using(var g=Graphics.FromImage(composited)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);g.CompositingMode=CompositingMode.SourceOver;float h=image.Height-2*margin,radius=Math.Min(h/2,24);GlassSurface.PaintPointer(g,new RectangleF(margin,margin,image.Width-2*margin,h),radius,light);}
   Compositions++;return composited; // Borrowed buffer; callers must NOT Dispose it.
  }
  public void Dispose(){if(image!=null){image.Dispose();image=null;}if(composited!=null){composited.Dispose();composited=null;}}
 }
}
