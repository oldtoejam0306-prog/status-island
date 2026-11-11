using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using IslandIntegrated;

// Bounded bitmap/layout checks for the AIHOT glass card. These checks do not
// exercise a native layered window, DPI scaling, or multi-monitor placement.
static class AihotGlassTests {
  static int assertions;
  sealed class SmokeHost : NoActivateForm { protected override bool ShowWithoutActivation { get { return true; } } }
  static void Check(bool value,string message) { assertions++; if(!value)throw new Exception(message); }
  static bool Intersects(Rectangle a,Rectangle b) { return a.IntersectsWith(b); }
  static AihotItem Fixture(string title) { return new AihotItem{Id="synthetic",Rank=1,Title=title,SourceName="示例来源",AihotUrl="https://aihot.news/"}; }
  static void CheckRectInside(Rectangle r,Rectangle body,string name) { Check(body.Contains(r),name+" stays in body: "+r); }
  static void CheckNoOverlap(Rectangle a,Rectangle b,string name) { Check(!Intersects(a,b),name+" do not overlap: "+a+" / "+b); }

  static void CheckCardMaterial(string theme) {
    Palette.Theme=theme; Palette.Solid=false; Palette.Legacy=false;
    using(var card=AihotRenderer.RenderCard(360,176,Fixture("示例：多地发布城市更新计划，人工智能与新能源产业协同发展"),"刚刚更新",false))
    using(var material=GlassSurface.StatusBackground(360,176,16,24)) {
      Check(card.Size==new Size(392,208),theme+" card frame is 392x208");
      Check(card.GetPixel(0,0).A==0,theme+" frame corner remains fully transparent");
      Color body=card.GetPixel(350,180);
      Check(body.A>0&&body.A<255,theme+" normal body remains semitransparent");
      Check(card.GetPixel(20,100).ToArgb()==material.GetPixel(20,100).ToArgb(),theme+" renderer uses shared StatusBackground material");
    }
  }
  static void CheckGeometry(int width) {
    Rectangle body=AihotLayout.CardBody(width,176);
    Check(body==new Rectangle(16,16,width,176),"card body includes the 16px frame margin");
    Rectangle title=AihotLayout.TitleHit(width),open=AihotLayout.OpenHit(width),story=AihotLayout.StoryHit(width);
    Rectangle refresh=AihotLayout.RefreshHit(width),pause=AihotLayout.PauseHit(width);
    CheckRectInside(title,body,"title hit region");CheckRectInside(open,body,"open hit region");
    CheckRectInside(story,body,"story hit region");CheckRectInside(refresh,body,"refresh hit region");CheckRectInside(pause,body,"pause hit region");
    CheckNoOverlap(open,story,"open and headline regions");CheckNoOverlap(refresh,pause,"action hit regions");
    Check(!Intersects(story,refresh)&&!Intersects(story,pause),"headline region clears actions for long Chinese titles");
  }
  static void CheckNotification() {
    Palette.Theme="tide";Palette.Solid=false;Palette.Legacy=false;
    string title="示例：多地发布城市更新计划，人工智能与新能源产业协同发展，相关部门公布了面向未来产业的专项支持政策，并将在本季度启动重点项目申报与资金安排工作。";
    int margin=AihotLayout.Margin;
    Rectangle body=AihotLayout.NotificationBody(margin),story=AihotLayout.NotificationStoryBounds(margin),close=AihotLayout.NotificationCloseHit(margin);
    CheckRectInside(story,body,"notification story bounds");CheckRectInside(close,body,"notification close hit region");
    CheckNoOverlap(story,close,"notification story and close hit regions");
    using(var note=AihotRenderer.RenderNotification(Fixture(title),""))
    using(var material=GlassSurface.StatusBackground(AihotLayout.CardWidth,AihotLayout.NoticeHeight,margin,AihotLayout.Radius)) {
      Check(note.Size==new Size(392,112),"notification frame is 392x112 around the fixed 360x80 body");
      Check(note.GetPixel(0,0).A==0,"notification shadow corner remains transparent");
      Check(body==new Rectangle(16,16,360,80),"notification body preserves the compact 360x80 size");
      int secondLineTop;
      using(var g=Graphics.FromImage(note))using(var font=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel))
        secondLineTop=story.Top+(int)Math.Ceiling(font.GetHeight(g));
      int changed=0;
      for(int y=secondLineTop;y<story.Bottom;y++)for(int x=story.Left;x<story.Right;x++)
        if(note.GetPixel(x,y).ToArgb()!=material.GetPixel(x,y).ToArgb())changed++;
      Check(changed>10,"empty-feedback long Chinese notification headline draws glyph pixels on its second line (changed="+changed+")");
    }
  }
  static void CheckSolidFallback() {
    string prior=Palette.Theme;bool oldSolid=Palette.Solid,oldLegacy=Palette.Legacy;
    try {
      Palette.Theme="tide";Palette.Solid=true;Palette.Legacy=false;
      using(var card=AihotRenderer.RenderCard(360,176,null,"离线",false)) {
        Check(card.GetPixel(350,180).A==255,"Solid fallback body is opaque");
        Check(card.GetPixel(350,180).ToArgb()==Palette.Base.ToArgb(),"Solid fallback uses the selected palette base");
      }
    } finally { Palette.Theme=prior;Palette.Solid=oldSolid;Palette.Legacy=oldLegacy; }
  }
  static void CheckNavigationFailureCopy() {
    string dir=Path.Combine(Path.GetTempPath(),"AihotGlassFeedback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
    using(var monitor=AihotMonitor.CreateRenderFixture(dir)) {
      var ctor=typeof(AihotSidebarControl).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[]{typeof(AihotMonitor),typeof(Action<string>)},null);
      using(var card=(AihotSidebarControl)ctor.Invoke(new object[]{monitor,new Action<string>(delegate{throw new InvalidOperationException("synthetic browser failure");})})) {
        var button=(Button)Field(card,"inspect");button.PerformClick();
        string displayed=card.DisplayedStatus;string status=((Control)Field(card,"status")).Text;
        Check(displayed.Contains("无法打开默认浏览器")&&status==displayed,"browser launch error is shared by renderer status and accessible text (DisplayedStatus="+displayed+", status.Text="+status+")");
      }
    }
    try{Directory.Delete(dir,true);}catch{}
  }
  static object Field(object value,string name) { return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value); }
  static void Pump(int milliseconds) { var until=DateTime.UtcNow.AddMilliseconds(milliseconds);do{Application.DoEvents();Thread.Sleep(5);}while(DateTime.UtcNow<until);Application.DoEvents(); }
  static void CheckNativeTransfers() {
    string dir=Path.Combine(Path.GetTempPath(),"AihotGlassSmoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
    using(var monitor=AihotMonitor.CreateRenderFixture(dir)) using(var host=new SmokeHost()) {
      host.TopLevel=true;host.FormBorderStyle=FormBorderStyle.None;host.ShowInTaskbar=false;host.StartPosition=FormStartPosition.Manual;host.Location=new Point(30,30);host.Size=new Size(330,230);host.Opacity=0;host.BackColor=Color.Fuchsia;host.TransparencyKey=Color.Fuchsia;
      var scroll=new Panel{Left=8,Top=8,Width=270,Height=160,AutoScroll=true,AutoScrollMinSize=new Size(620,420),BackColor=Color.Fuchsia};host.Controls.Add(scroll);
      var card=new AihotSidebarControl(monitor){Location=new Point(0,0)};scroll.Controls.Add(card);host.Show();Pump(80);
      object overlay=Field(card,"overlay");Check(overlay!=null,"real card overlay is constructed on a layered host");
      Form overlayForm=overlay as Form;Check(overlayForm!=null&&overlayForm.IsHandleCreated,"offscreen native overlay has an HWND");
      Check((GlassWin.GetWindowLong(overlayForm.Handle,-20)&0x00080000)!=0,"card overlay HWND has WS_EX_LAYERED");
      string clipBefore=Convert.ToString(overlay.GetType().GetProperty("LastClip").GetValue(overlay,null));
      Check(!String.IsNullOrEmpty(clipBefore),"card overlay records its viewport clip");
      Check(Convert.ToBoolean(overlay.GetType().GetProperty("LastPushSucceeded").GetValue(overlay,null)),"card overlay bitmap transfer succeeded (offscreen host opacity zero)");
      using(var bitmap=new Bitmap(card.Width,card.Height)){card.DrawToBitmap(bitmap,new Rectangle(Point.Empty,card.Size));Check(bitmap.GetPixel(card.Width/2,card.Height/2).ToArgb()==Color.Fuchsia.ToArgb(),"overlay mode leaves no opaque WinForms underlay");}
      Bitmap frame=(Bitmap)Field(overlay,"clippedFrame");Check(frame!=null&&frame.Size==new Size(392,208),"native overlay retains the expected full frame");
      Check(frame.GetPixel(380,100).A==0,"bitmap pixels outside the narrow scroll viewport are transparent");
      Point posBefore=scroll.AutoScrollPosition;scroll.AutoScrollPosition=new Point(80,50);scroll.PerformLayout();Pump(100);Point posAfter=scroll.AutoScrollPosition;
      string clipAfter=Convert.ToString(overlay.GetType().GetProperty("LastClip").GetValue(overlay,null));
      Check(clipBefore!=clipAfter,"scroll movement updates native overlay clipping (before="+clipBefore+", after="+clipAfter+", scroll="+posBefore+"->"+posAfter+")");
      host.Hide();Pump(40);Check(!overlayForm.Visible,"hiding the owner hides its native overlay");
      card.Dispose();Pump(20);Check(overlayForm.IsDisposed,"disposing the card closes and cleans up the native overlay");
    }
    try{Directory.Delete(dir,true);}catch{}
  }
  static void CheckNotificationNativeTransfer() {
    var type=typeof(AihotMonitor).Assembly.GetType("IslandIntegrated.AihotPopupForm",true);
    var ctor=type.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[]{typeof(AihotItem),typeof(Action<string>)},null);
    var popup=(Form)ctor.Invoke(new object[]{Fixture("示例热点通知"),new Action<string>(delegate{})});
    try {
      popup.TopMost=false;popup.Location=new Point(-30000,-30000);
      type.GetMethod("ShowLayered",BindingFlags.Instance|BindingFlags.Public).Invoke(popup,null);Pump(20);
      Check((GlassWin.GetWindowLong(popup.Handle,-20)&0x00080000)!=0,"notification HWND has WS_EX_LAYERED");
      Check(Convert.ToBoolean(type.GetProperty("LastPushSucceeded").GetValue(popup,null)),"offscreen notification native transfer succeeded");
      Bitmap frame=(Bitmap)Field(popup,"frame");Check(frame!=null&&frame.Size==new Size(392,112),"notification transfers its 392x112 shared-renderer frame");
    } finally { popup.Close();popup.Dispose();Pump(10); }
  }
  static void SaveComposite(Bitmap source,string path,Color background) {
    using(var output=new Bitmap(source.Width,source.Height)) using(var g=Graphics.FromImage(output)) {
      g.Clear(background);g.DrawImage(source,0,0);output.Save(path,ImageFormat.Png);
    }
  }
  static void SaveChecker(Bitmap source,string path) {
    using(var output=new Bitmap(source.Width,source.Height)) using(var g=Graphics.FromImage(output)) {
      const int tile=16;for(int y=0;y<source.Height;y+=tile)for(int x=0;x<source.Width;x+=tile) {
        using(var b=new SolidBrush(((x/tile+y/tile)&1)==0?Color.FromArgb(235,238,242):Color.FromArgb(45,50,58)))g.FillRectangle(b,x,y,tile,tile);
      }
      g.DrawImage(source,0,0);output.Save(path,ImageFormat.Png);
    }
  }
  static void Export(string directory) {
    Directory.CreateDirectory(directory);
    string[] themes={"tide","pine","iris","amber"};
    string longTitle="示例：多地发布城市更新计划，人工智能与新能源产业协同发展，相关部门公布了面向未来产业的专项支持政策，并将在本季度启动重点项目申报与资金安排工作。";
    string[] cardPaths=new string[themes.Length];
    foreach(string theme in themes) {
      Palette.Theme=theme;Palette.Solid=false;Palette.Legacy=false;
      string transparent=Path.Combine(directory,"aihot-glass-"+theme+"-transparent.png");cardPaths[Array.IndexOf(themes,theme)]=transparent;
      using(var card=AihotRenderer.RenderCard(360,176,Fixture(longTitle),"示例数据 · 刚刚更新",false)) {
        card.Save(transparent,ImageFormat.Png);
        SaveComposite(card,Path.Combine(directory,"aihot-glass-"+theme+"-light.png"),Color.FromArgb(240,244,247));
        SaveComposite(card,Path.Combine(directory,"aihot-glass-"+theme+"-dark.png"),Color.FromArgb(22,27,34));
        SaveChecker(card,Path.Combine(directory,"aihot-glass-"+theme+"-checker.png"));
      }
    }
    Palette.Theme="iris";
    using(var reference=GlassSurface.StatusBackground(360,176,16,24))
      reference.Save(Path.Combine(directory,"aihot-StatusBackground-reference-iris.png"),ImageFormat.Png);
    using(var current=AihotRenderer.RenderCard(360,176,Fixture(longTitle),"示例数据 · 当前主题 iris",false)) {
      current.Save(Path.Combine(directory,"aihot-glass-current-theme-iris-transparent.png"),ImageFormat.Png);
      SaveComposite(current,Path.Combine(directory,"aihot-glass-current-theme-iris-light.png"),Color.FromArgb(240,244,247));
      SaveComposite(current,Path.Combine(directory,"aihot-glass-current-theme-iris-dark.png"),Color.FromArgb(22,27,34));
      SaveChecker(current,Path.Combine(directory,"aihot-glass-current-theme-iris-checker.png"));
    }
    using(var sheet=new Bitmap(824,472)) using(var g=Graphics.FromImage(sheet)) {
      g.Clear(Color.FromArgb(27,32,39));using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.White)) {
        for(int i=0;i<cardPaths.Length;i++) { using(var card=(Bitmap)Image.FromFile(cardPaths[i])) { int x=16+(i%2)*404,y=32+(i/2)*216;g.DrawString(themes[i],font,brush,x,y-18);g.DrawImage(card,x,y); } }
      }
      sheet.Save(Path.Combine(directory,"aihot-glass-4themes-contactsheet.png"),ImageFormat.Png);
    }
    using(var sheet=new Bitmap(1200,280)) using(var g=Graphics.FromImage(sheet)) {
      g.Clear(Color.FromArgb(27,32,39));using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.White)) {
        string[] labels={"加载中 · 示例状态","暂停提醒 · 示例状态","网络错误 · 示例状态"};
        string[] states={"正在加载", "网络错误", "网络不可用"};bool[] paused={false,true,false};
        for(int i=0;i<states.Length;i++) { using(var card=AihotRenderer.RenderCard(360,176,Fixture(longTitle),states[i],paused[i])) { int x=12+i*396;g.DrawString(labels[i],font,brush,x,8);g.DrawImage(card,x,28); } }
      }
      sheet.Save(Path.Combine(directory,"aihot-glass-iris-states.png"),ImageFormat.Png);
    }
    Palette.Theme="iris";
    using(var note=AihotRenderer.RenderNotification(Fixture(longTitle),""))
      note.Save(Path.Combine(directory,"aihot-notification-long-headline.png"),ImageFormat.Png);
    using(var note=AihotRenderer.RenderNotification(Fixture(longTitle),"示例反馈：热点提醒已更新"))
      note.Save(Path.Combine(directory,"aihot-notification-feedback.png"),ImageFormat.Png);
    string fixtureDir=Path.Combine(Path.GetTempPath(),"AihotFallbackExport-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixtureDir);
    using(var monitor=AihotMonitor.CreateRenderFixture(fixtureDir)) using(var host=new Form()) using(var card=new AihotSidebarControl(monitor)) {
      host.TopLevel=true;host.ShowInTaskbar=false;host.FormBorderStyle=FormBorderStyle.None;host.Controls.Add(card);
      using(var fallback=new Bitmap(card.Width,card.Height)) {
        card.DrawToBitmap(fallback,new Rectangle(Point.Empty,card.Size));
        Check(Field(card,"overlay")==null,"ordinary WinForms parent exercises the control fallback without a native overlay");
        Check(fallback.GetPixel(350,170).A==255,"actual control fallback has an opaque backing under shared renderer alpha");
        fallback.Save(Path.Combine(directory,"aihot-control-fallback-iris.png"),ImageFormat.Png);
      }
    }
    try{Directory.Delete(fixtureDir,true);}catch{}
  }
  [STAThread]
  public static int Main(string[] args) {
    string theme=Palette.Theme;bool solid=Palette.Solid,legacy=Palette.Legacy;
    try {
      bool renderOnly=args.Length==2&&args[0]=="--render-only";
      CheckGeometry(320);CheckGeometry(360);
      CheckCardMaterial("tide");CheckCardMaterial("pine");CheckCardMaterial("iris");CheckCardMaterial("amber");
      CheckNotification();CheckSolidFallback();CheckNavigationFailureCopy();
      if(!renderOnly){CheckNativeTransfers();CheckNotificationNativeTransfer();}
      if(args.Length==2&&(args[0]=="--render"||renderOnly))Export(args[1]);
      Console.WriteLine("AihotGlassTests passed ("+assertions+" assertions)."+(args.Length==2&&args[0]=="--render"?" Render evidence: "+Path.GetFullPath(args[1]):""));
      return 0;
    } catch(Exception e) { Console.WriteLine("AihotGlassTests failed: "+e.GetType().FullName+" / "+e.Message);return 1; }
    finally { Palette.Theme=theme;Palette.Solid=solid;Palette.Legacy=legacy; }
  }
}
