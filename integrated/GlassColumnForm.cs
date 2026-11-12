using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IslandIntegrated {
  public static class GlassComposition {
    [ThreadStatic] static int depth;
    public static bool Active { get { return depth>0; } }
    sealed class Scope : IDisposable {
      public Scope(){depth++;}
      public void Dispose(){depth--;}
    }
    public static void DrawImageReplacing(Graphics g,Bitmap image,int x,int y){
      var saved=g.Save();
      try{g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,x,y);}
      finally{g.Restore(saved);}
    }
    internal sealed class PaintBridge : Control {
      public void Background(Control c,Graphics g){InvokePaintBackground(c,new PaintEventArgs(g,c.ClientRectangle));}
      public void Content(Control c,Graphics g){InvokePaint(c,new PaintEventArgs(g,c.ClientRectangle));}
    }
    public static Bitmap CaptureControl(Control control){
      if(control==null)throw new ArgumentNullException("control");
      var image=new Bitmap(Math.Max(1,control.Width),Math.Max(1,control.Height),PixelFormat.Format32bppArgb);
      try{
        using(var g=Graphics.FromImage(image))using(var bridge=new PaintBridge())using(new Scope()){
          g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
          PaintTree(bridge,g,control,Point.Empty,null,true);
        }
        return image;
      }catch{image.Dispose();throw;}
    }
    internal static void PaintFrame(Graphics g,Control host,Func<WidgetPanel,Bitmap> cached){
      using(var bridge=new PaintBridge())using(new Scope()){
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        for(int i=host.Controls.Count-1;i>=0;i--)PaintTree(bridge,g,host.Controls[i],host.Controls[i].Location,cached,false);
      }
    }
    static bool IsHitOnly(Control c){string name=c.GetType().Name;return name=="AihotHitLabel"||name=="AihotHitButton";}
    static void PaintTree(PaintBridge bridge,Graphics g,Control c,Point origin,Func<WidgetPanel,Bitmap> cached,bool root){
      if(c.IsDisposed||(!root&&!c.Visible)||c.Width<1||c.Height<1||IsHitOnly(c))return;
      var saved=g.Save();
      try{
        g.TranslateTransform(origin.X,origin.Y);
        g.SetClip(new Rectangle(Point.Empty,c.Size),CombineMode.Intersect);
        var widget=c as WidgetPanel;
        if(widget!=null&&cached!=null){g.DrawImageUnscaled(cached(widget),0,0);return;}
        if(c.Region!=null&&widget==null&&!(c is GlassActionButton))g.SetClip(c.Region,CombineMode.Intersect);
        var client=g.Save();
        try{
          g.SetClip(c.ClientRectangle,CombineMode.Intersect);
          if(widget!=null)bridge.Background(c,g);
          else if(c is Label)PaintLabel(g,(Label)c);
          else bridge.Content(c,g);
          // The bitmap owns its antialiased rim. Clip child contents after painting that rim.
          if(widget!=null&&c.Region!=null)g.SetClip(c.Region,CombineMode.Intersect);
          for(int i=c.Controls.Count-1;i>=0;i--)PaintTree(bridge,g,c.Controls[i],c.Controls[i].Location,cached,false);
        }finally{g.Restore(client);}
        var scroll=c as ScrollableControl;
        if(scroll!=null)PaintScrollbar(g,scroll);
      }finally{g.Restore(saved);}
    }
    static void PaintLabel(Graphics g,Label label){
      if(label.BackColor.A>0)using(var fill=new SolidBrush(label.BackColor))g.FillRectangle(fill,label.ClientRectangle);
      if(String.IsNullOrEmpty(label.Text))return;
      using(var format=new StringFormat())using(var ink=new SolidBrush(label.Enabled?label.ForeColor:Palette.Secondary)){
        switch(label.TextAlign){
          case ContentAlignment.TopCenter:case ContentAlignment.MiddleCenter:case ContentAlignment.BottomCenter:format.Alignment=StringAlignment.Center;break;
          case ContentAlignment.TopRight:case ContentAlignment.MiddleRight:case ContentAlignment.BottomRight:format.Alignment=StringAlignment.Far;break;
        }
        switch(label.TextAlign){
          case ContentAlignment.MiddleLeft:case ContentAlignment.MiddleCenter:case ContentAlignment.MiddleRight:format.LineAlignment=StringAlignment.Center;break;
          case ContentAlignment.BottomLeft:case ContentAlignment.BottomCenter:case ContentAlignment.BottomRight:format.LineAlignment=StringAlignment.Far;break;
        }
        format.Trimming=label.AutoEllipsis?StringTrimming.EllipsisCharacter:StringTrimming.None;
        format.HotkeyPrefix=label.UseMnemonic?HotkeyPrefix.Show:HotkeyPrefix.None;
        if(label.RightToLeft==RightToLeft.Yes)format.FormatFlags|=StringFormatFlags.DirectionRightToLeft;
        var bounds=label.ClientRectangle;bounds.X+=label.Padding.Left;bounds.Y+=label.Padding.Top;
        bounds.Width=Math.Max(1,bounds.Width-label.Padding.Horizontal);bounds.Height=Math.Max(1,bounds.Height-label.Padding.Vertical);
        g.DrawString(label.Text,label.Font,ink,bounds,format);
      }
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct ScrollbarInfo {
      public int Size;public NativeRect Bounds;public int ButtonSize,ThumbTop,ThumbBottom,Reserved;
      [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public int[] State;
    }
    [DllImport("user32.dll")]static extern bool GetScrollBarInfo(IntPtr window,int id,ref ScrollbarInfo info);
    static void PaintScrollbar(Graphics g,ScrollableControl scroll){
      if(!scroll.AutoScroll||!scroll.VerticalScroll.Visible||!scroll.IsHandleCreated)return;
      var info=new ScrollbarInfo{Size=Marshal.SizeOf(typeof(ScrollbarInfo)),State=new int[6]};
      if(!GetScrollBarInfo(scroll.Handle,-5,ref info))return;
      Point origin=scroll.PointToClient(new Point(info.Bounds.Left,info.Bounds.Top));
      var track=new Rectangle(origin.X,origin.Y,info.Bounds.Right-info.Bounds.Left,info.Bounds.Bottom-info.Bounds.Top);
      // A nonzero alpha retains the native scrollbar's complete input target.
      using(var fill=new SolidBrush(Color.FromArgb(1,Palette.Base)))g.FillRectangle(fill,track);
      int width=Math.Min(5,track.Width),height=info.ThumbBottom-info.ThumbTop;
      if(width<1||height<1)return;
      var thumb=new Rectangle(track.Left+(track.Width-width)/2,track.Top+info.ThumbTop,width,height);
      using(var path=GlassSurface.Path(thumb,width/2f))using(var fill=new SolidBrush(Color.FromArgb(128,Palette.Secondary)))g.FillPath(fill,path);
    }
  }

  public class GlassColumnForm : NoActivateForm {
    readonly Timer renderTimer;
    readonly HashSet<Control> hooked=new HashSet<Control>();
    readonly Dictionary<WidgetPanel,Bitmap> cache=new Dictionary<WidgetPanel,Bitmap>();
    readonly HashSet<WidgetPanel> dirtyCards=new HashSet<WidgetPanel>();
    Bitmap frame;
    bool dirty=true,disposed,rendering,hasPushed,presenting;
    double opacity=1;
    int lastOpacity=-1,frameVersion,lastPushedVersion=-1;
    Point lastPosition;
    long lastRender;
    public int RenderCount {get;private set;}
    public int CardRenderCount {get;private set;}
    public int PushCount {get;private set;}
    public string LastPushError {get;private set;}
    bool Layered {get{return TopLevel&&PerPixelLayered;}}
    public new double Opacity {
      get{return opacity;}
      set{
        double next=Double.IsNaN(value)||Double.IsInfinity(value)?1:Math.Max(0,Math.Min(1,value));
        if(opacity==next)return;
        opacity=next;
        PresentFrame(false);
      }
    }
    public void SetPresentation(Point position, double value) {
      presenting = true;
      try {
        Location = position;
        Opacity = value;
      } finally {
        presenting = false;
      }
      int prior = PushCount;
      PresentFrame(false);
      if (PushCount != prior) TransitionMetrics.MarkTick("column-transform", PreviewClock.NowMs);
    }
    public GlassColumnForm(){
      PerPixelLayered=true;
      renderTimer=new Timer{Interval=16};renderTimer.Tick+=OnTick;
      ControlAdded+=OnAdded;ControlRemoved+=OnRemoved;
      SizeChanged+=OnGeometry;LocationChanged+=OnPosition;VisibleChanged+=OnVisibility;
      HandleCreated+=OnHandle;HandleDestroyed+=OnHandleLost;
      renderTimer.Start();
    }
    protected override CreateParams CreateParams {
      get{var p=base.CreateParams;if(TopLevel)p.ExStyle|=0x80000;else p.ExStyle&=~0x80000;return p;}
    }
    void OnHandle(object sender,EventArgs e){PerPixelLayered=TopLevel;dirty=true;hasPushed=false;}
    void OnHandleLost(object sender,EventArgs e){hasPushed=false;}
    void OnGeometry(object sender,EventArgs e){dirty=true;}
    void OnPosition(object sender,EventArgs e){PresentFrame(false);}
    void OnVisibility(object sender,EventArgs e){dirty=true;if(Visible)PresentFrame(true);}
    void OnAdded(object sender,ControlEventArgs e){Hook(e.Control);MarkDirty(sender as Control);}
    void OnRemoved(object sender,ControlEventArgs e){Unhook(e.Control);MarkDirty(sender as Control);}
    void Hook(Control c){
      if(!hooked.Add(c))return;
      c.ControlAdded+=OnAdded;c.ControlRemoved+=OnRemoved;c.Invalidated+=OnInvalidated;
      c.TextChanged+=OnChanged;c.EnabledChanged+=OnChanged;c.VisibleChanged+=OnChanged;
      c.SizeChanged+=OnChanged;c.LocationChanged+=OnChildPosition;
      var widget=c as WidgetPanel;if(widget!=null)dirtyCards.Add(widget);
      var scroll=c as ScrollableControl;if(scroll!=null)scroll.Scroll+=OnScroll;
      foreach(Control child in c.Controls)Hook(child);
    }
    void Unhook(Control c){
      if(!hooked.Remove(c))return;
      c.ControlAdded-=OnAdded;c.ControlRemoved-=OnRemoved;c.Invalidated-=OnInvalidated;
      c.TextChanged-=OnChanged;c.EnabledChanged-=OnChanged;c.VisibleChanged-=OnChanged;
      c.SizeChanged-=OnChanged;c.LocationChanged-=OnChildPosition;
      var scroll=c as ScrollableControl;if(scroll!=null)scroll.Scroll-=OnScroll;
      var widget=c as WidgetPanel;
      if(widget!=null){Bitmap old;if(cache.TryGetValue(widget,out old)){old.Dispose();cache.Remove(widget);}dirtyCards.Remove(widget);}
      foreach(Control child in c.Controls)Unhook(child);
    }
    void OnInvalidated(object sender,InvalidateEventArgs e){MarkDirty(sender as Control);}
    void OnChanged(object sender,EventArgs e){MarkDirty(sender as Control);}
    void OnChildPosition(object sender,EventArgs e){if(sender is WidgetPanel)dirty=true;else MarkDirty(sender as Control);}
    void OnScroll(object sender,ScrollEventArgs e){dirty=true;}
    void MarkDirty(Control c){
      if(rendering||disposed)return;
      dirty=true;
      for(var p=c;p!=null&&p!=this;p=p.Parent){var widget=p as WidgetPanel;if(widget!=null){dirtyCards.Add(widget);break;}}
    }
    Bitmap CardFrame(WidgetPanel card){
      Bitmap prior;
      if(!cache.TryGetValue(card,out prior)||dirtyCards.Contains(card)||prior.Size!=card.Size){
        var next=GlassComposition.CaptureControl(card);
        if(prior!=null)prior.Dispose();cache[card]=next;dirtyCards.Remove(card);CardRenderCount++;return next;
      }
      return prior;
    }
    void EnsureFrame(bool force){
      bool resized=frame==null||frame.Size!=ClientSize;
      if(!dirty&&!resized)return;
      long now=PreviewClock.NowMs;
      if(!force&&!resized&&now-lastRender<33)return;
      var next=new Bitmap(Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height),PixelFormat.Format32bppArgb);
      rendering=true;
      try{
        using(var g=Graphics.FromImage(next))GlassComposition.PaintFrame(g,this,CardFrame);
        if(frame!=null)frame.Dispose();frame=next;next=null;
        dirty=false;lastRender=now;frameVersion++;RenderCount++;
      }finally{rendering=false;if(next!=null)next.Dispose();}
    }
    void PresentFrame(bool force){
      if(presenting||disposed||!Layered||!IsHandleCreated||!Visible||Width<1||Height<1)return;
      presenting=true;
      try{
        EnsureFrame(force);
        if(frame==null||frame.Size!=ClientSize)return;
        Point destination=Location;
        int alpha=(int)Math.Round(opacity*255);
        if(hasPushed&&lastPushedVersion==frameVersion&&lastPosition==destination&&lastOpacity==alpha)return;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        PushCount++;
        if(GlassWin.Push(Handle,frame,destination,(byte)alpha)){
          hasPushed=true;lastPushedVersion=frameVersion;lastPosition=destination;lastOpacity=alpha;LastPushError=null;
        }else LastPushError="native "+GlassWin.LastPushError;
        TransitionMetrics.Record("column-native-submit",watch.Elapsed.TotalMilliseconds);
      }catch(Exception error){LastPushError="render "+error.GetType().Name;}
      finally{presenting=false;}
    }
    void OnTick(object sender,EventArgs e){PresentFrame(false);}
    public void CommitLayoutFrame(){PresentFrame(true);}
    public Bitmap CaptureFrame(){EnsureFrame(true);return frame==null?new Bitmap(1,1):(Bitmap)frame.Clone();}
    public Bitmap RenderFrameForEvidence(){return CaptureFrame();}
    protected override void OnPaintBackground(PaintEventArgs e){if(!TopLevel){PerPixelLayered=false;base.OnPaintBackground(e);}}
    protected override void OnPaint(PaintEventArgs e){if(!TopLevel){PerPixelLayered=false;base.OnPaint(e);}}
    protected override void Dispose(bool disposing){
      if(disposing&&!disposed){
        disposed=true;renderTimer.Stop();renderTimer.Tick-=OnTick;renderTimer.Dispose();
        ControlAdded-=OnAdded;ControlRemoved-=OnRemoved;SizeChanged-=OnGeometry;LocationChanged-=OnPosition;VisibleChanged-=OnVisibility;
        HandleCreated-=OnHandle;HandleDestroyed-=OnHandleLost;
        foreach(Control c in Controls)Unhook(c);
        foreach(var image in cache.Values)image.Dispose();cache.Clear();dirtyCards.Clear();hooked.Clear();
        if(frame!=null){frame.Dispose();frame=null;}
      }
      base.Dispose(disposing);
    }
  }
}
