using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace IslandIntegrated {
  // Shared legacy glass style. This draws material; it does not claim OS backdrop blur.
  public static class GlassSurface {
    public static GraphicsPath Path(RectangleF r,float radius) {
      var p=new GraphicsPath();float d=Math.Max(1,Math.Min(radius*2,Math.Min(r.Width,r.Height)));
      p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
      p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    static void Glow(Graphics g,float x,float y,float w,float h,Color color) {
      using(var p=new GraphicsPath()){p.AddEllipse(x,y,w,h);using(var b=new PathGradientBrush(p)){
        b.CenterColor=color;b.SurroundColors=new[]{Color.FromArgb(0,color.R,color.G,color.B)};g.FillPath(b,p);
      }}
    }
    public static Bitmap StatusBackground(int width,int height,int margin,float radius){
      var image=new Bitmap(width+2*margin,height+2*margin);using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
      // Shadow belongs outside the body. Stacking black fills underneath
      // translucent material previously defeated its alpha and made it opaque.
      var saved=g.Save();using(var inner=Path(new RectangleF(margin,margin,width,height),radius))using(var outside=new Region(new Rectangle(0,0,image.Width,image.Height))){outside.Exclude(inner);g.SetClip(outside,CombineMode.Replace);
      for(int i=8;i>=1;i--){using(var path=Path(new RectangleF(margin-i,margin-i+3,width+2*i,height+2*i),radius+i))using(var brush=new SolidBrush(Color.FromArgb(3+(9-i),0,0,0)))g.FillPath(brush,path);}}g.Restore(saved);
      if(Palette.Solid||System.Windows.Forms.SystemInformation.HighContrast)Paint(g,new RectangleF(margin,margin,width,height),radius,false);else StatusMaterial.Paint(g,new RectangleF(margin,margin,width,height),radius,false);}return image;
    }
    public static void LegacyPaint(Graphics g,RectangleF r,float radius,bool opaqueBacking) {
      if(r.Width<2||r.Height<2)return;var saved=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
      using(var body=Path(r,radius)) {
        // WinForms child controls are not per-pixel layered windows. A neutral
        // backing prevents alpha being blended against the parent's chroma key.
        if(opaqueBacking)using(var b=new SolidBrush(Color.FromArgb(18,29,43)))g.FillPath(b,body);
        using(var b=new LinearGradientBrush(new PointF(r.X,r.Top),new PointF(r.X,r.Bottom),Color.FromArgb(194,34,52,72),Color.FromArgb(218,16,31,49)))g.FillPath(b,body);
        g.SetClip(body,CombineMode.Intersect);
        using(var sheen=new LinearGradientBrush(new PointF(0,r.Top-4),new PointF(0,r.Top+120),Color.FromArgb(32,224,241,255),Color.FromArgb(0,224,241,255))){sheen.WrapMode=WrapMode.TileFlipXY;g.FillRectangle(sheen,r.X,r.Top-4,r.Width,124);}
        Glow(g,r.X+r.Width*.72f-90,r.Top-60,200,180,Color.FromArgb(15,151,218,255));
        Glow(g,r.X+r.Width*.04f-40,r.Top+r.Height*.30f,170,190,Color.FromArgb(10,178,203,232));
        if(r.Height>120)Glow(g,r.X+r.Width*.50f-90,r.Bottom-40,180,150,Color.FromArgb(14,110,160,255));
        var band=g.Save();g.TranslateTransform(r.X+r.Width*.40f,r.Top+r.Height*.30f);g.RotateTransform(-24);
        using(var b=new LinearGradientBrush(new PointF(-150,0),new PointF(150,0),Color.Transparent,Color.Transparent)){
          b.InterpolationColors=new ColorBlend(3){Colors=new[]{Color.FromArgb(0,255,255,255),Color.FromArgb(15,255,255,255),Color.FromArgb(0,255,255,255)},Positions=new[]{0f,.5f,1f}};
          g.FillRectangle(b,-150,-260,300,520);
        }g.Restore(band);g.ResetClip();
        using(var b=new LinearGradientBrush(new PointF(0,r.Top),new PointF(0,r.Bottom),Color.FromArgb(65,232,245,255),Color.FromArgb(18,209,231,249)))using(var pen=new Pen(b,.8f))g.DrawPath(pen,body);
      }g.Restore(saved);
    }
    public static void Paint(Graphics g,RectangleF r,float radius,bool opaqueBacking){
      if(Palette.Legacy){LegacyPaint(g,r,radius,true);return;}if(r.Width<2||r.Height<2)return;
      if(Palette.Solid||System.Windows.Forms.SystemInformation.HighContrast){using(var path=Path(r,radius))using(var brush=new SolidBrush(Palette.Base))g.FillPath(brush,path);return;}
      var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
      using(var path=Path(r,radius)){
        if(opaqueBacking)using(var b=new SolidBrush(Palette.Base))g.FillPath(b,path);
        bool high=System.Windows.Forms.SystemInformation.HighContrast;
        var lower=Color.FromArgb((int)(Palette.Base.R*.66),(int)(Palette.Base.G*.66),(int)(Palette.Base.B*.66));using(var b=new LinearGradientBrush(r,Color.FromArgb(255,Palette.Top),Color.FromArgb(255,lower),LinearGradientMode.Vertical))g.FillPath(b,path);
        g.SetClip(path,CombineMode.Intersect);
        float sheen=Math.Min(96,r.Height*.62f);using(var b=new LinearGradientBrush(new RectangleF(r.X,r.Y,r.Width,Math.Max(1,sheen)),Color.FromArgb(24,255,255,255),Color.Transparent,LinearGradientMode.Vertical)){b.WrapMode=WrapMode.TileFlipXY;g.FillRectangle(b,r.X,r.Y,r.Width,sheen);}
        Glow(g,r.X-r.Width*.17f,r.Y-r.Height*.32f,r.Width*.74f,r.Height*.68f,Color.FromArgb(10,255,255,255));
        g.Restore(state);state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var edge=new LinearGradientBrush(r,Color.FromArgb(88,255,255,255),Color.FromArgb(22,255,255,255),LinearGradientMode.Vertical))using(var pen=new Pen(edge,.75f))g.DrawPath(pen,path);
      }g.Restore(state);
    }
    public static Rectangle PointerBounds(Rectangle bounds,PointerLight light){if(light.Alpha<=0)return Rectangle.Empty;return Rectangle.Intersect(bounds,new Rectangle((int)light.X-164,(int)light.Y-114,328,228));}
    public static void PaintPointer(Graphics g,RectangleF r,float radius,PointerLight light){
      if(light==null||light.Alpha<=0||r.Width<2||r.Height<2)return;
      var saved=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
      using(var shape=Path(r,radius)){
        g.SetClip(shape,CombineMode.Intersect);
        Glow(g,r.X+light.X-138,r.Y+light.Y-88,276,176,Color.FromArgb((int)(38*light.Alpha),234,235,237));
        Glow(g,r.X+light.X-44,r.Y+light.Y-26,88,52,Color.FromArgb((int)(10*light.Alpha),250,250,250));
        using(var focus=new GraphicsPath()){focus.AddEllipse(r.X+light.X-160,r.Y+light.Y-110,320,220);using(var brush=new PathGradientBrush(focus)){
          brush.CenterColor=Color.FromArgb((int)(215*light.Alpha),243,244,246);brush.SurroundColors=new[]{Color.FromArgb(0,213,244,255)};brush.WrapMode=WrapMode.Clamp;
          using(var pen=new Pen(brush,1.35f))g.DrawPath(pen,shape);
        }}
      }g.Restore(saved);
    }
    public static Rectangle ShimmerBounds(Rectangle r,double phase){float x=r.Left-90+(float)phase*(r.Width+180),slant=r.Height*.06f;return Rectangle.Intersect(r,Rectangle.FromLTRB((int)Math.Floor(x-32-slant),(int)r.Top,(int)Math.Ceiling(x+32+slant),(int)r.Bottom));}
    public static void PaintShimmer(Graphics g,RectangleF r,float radius,double phase){
      if(r.Width<2||r.Height<2)return;var saved=g.Save();float x=r.Left-90+(float)phase*(r.Width+180),slant=r.Height*.06f;
      using(var shape=Path(r,radius)){g.SetClip(shape,CombineMode.Intersect);using(var brush=new LinearGradientBrush(new PointF(x-32,r.Top),new PointF(x+32,r.Top),Color.Transparent,Color.Transparent)){
        brush.InterpolationColors=new ColorBlend(3){Colors=new[]{Color.FromArgb(0,224,242,255),Color.FromArgb(16,224,242,255),Color.FromArgb(0,224,242,255)},Positions=new[]{0f,.5f,1f}};
        g.FillPolygon(brush,new[]{new PointF(x-32+slant,r.Top),new PointF(x+32+slant,r.Top),new PointF(x+32-slant,r.Bottom),new PointF(x-32-slant,r.Bottom)});
      }}g.Restore(saved);
    }
  }
}
