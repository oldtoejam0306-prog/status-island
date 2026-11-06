using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace IslandIntegrated {
 // Compact offline adaptation of the Autoloop liquid card. No transport or actions are wired.
 public sealed class AutoloopSidebarControl : Control {
  public AutoloopSidebarControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleName="Autoloop，未连接";AccessibleDescription="尚未连接，无法显示任务或执行操作。";}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));
   using(var path=GlassSurface.Path(r,16)){using(var b=new LinearGradientBrush(r,Color.FromArgb(67,79,89),Color.FromArgb(27,39,50),90f))g.FillPath(b,path);using(var pen=new Pen(Color.FromArgb(115,190,211,221)))g.DrawPath(pen,path);}
   int pad=15; using(var title=new Font("Microsoft YaHei UI",10.5f,FontStyle.Bold)) using(var body=new Font("Microsoft YaHei UI",9f)) {
    TextRenderer.DrawText(g,"Autoloop",title,new Rectangle(pad,15,Math.Max(1,Width-2*pad),25),Color.FromArgb(234,242,249),TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    using(var dot=new SolidBrush(Color.FromArgb(158,175,184)))g.FillEllipse(dot,pad,52,7,7);
    TextRenderer.DrawText(g,"未连接",body,new Rectangle(pad+13,45,Math.Max(1,Width-pad-20),25),Color.FromArgb(183,210,220),TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    TextRenderer.DrawText(g,"连接后显示任务",body,new Rectangle(pad,82,Math.Max(1,Width-2*pad),44),Color.FromArgb(168,184,194),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
   }
  }
 }
}
