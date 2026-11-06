using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace IslandIntegrated {
  public static class ActionButtonSurface {
    public static void Draw(Graphics g,Rectangle bounds,string text,bool plus,bool enabled,bool pressed,bool focused){
      g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Palette.Base);
      var area=new RectangleF(bounds.X+.5f,bounds.Y+.5f,Math.Max(1,bounds.Width-1),Math.Max(1,bounds.Height-1));
      using(var path=GlassSurface.Path(area,8))using(var fill=new SolidBrush(pressed?Palette.Top:Palette.Base))using(var border=new Pen(Color.FromArgb(70,Palette.Accent),1)){g.FillPath(fill,path);g.DrawPath(border,path);}
      var color=enabled?Palette.Text:Color.FromArgb(123,135,153);
      if(plus){using(var pen=new Pen(color,1.8f)){float x=bounds.Left+bounds.Width/2f,y=bounds.Top+bounds.Height/2f;g.DrawLine(pen,x-5,y,x+5,y);g.DrawLine(pen,x,y-5,x,y+5);}}
      else using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(text,SystemFonts.MessageBoxFont,brush,bounds,format);
      if(focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(bounds,-4,-4),color,Palette.Base);
    }
  }
  public sealed class GlassActionButton : Button {
    bool pressed;
    public bool Plus {get;set;}
    public GlassActionButton(){FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;FlatAppearance.BorderSize=0;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AccessibleRole=AccessibleRole.PushButton;}
    protected override void OnPaintBackground(PaintEventArgs e){}
    // Do not call Button.OnPaint: the native themed rectangle must not show through.
    protected override void OnPaint(PaintEventArgs e){ActionButtonSurface.Draw(e.Graphics,ClientRectangle,Text,Plus,Enabled,pressed,Focused&&ShowFocusCues);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnMouseLeave(EventArgs e){pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<2||Height<2)return;using(var path=GlassSurface.Path(new RectangleF(0,0,Width,Height),8)){var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
  }
}
