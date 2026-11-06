using System;using System.Drawing;
namespace IslandIntegrated {
 public static class EditCardChrome {
  public static void Paint(Graphics g,string title,int width,float scale){float s=Math.Max(.5f,scale);using(var f=new Font("Microsoft YaHei UI",13*s,FontStyle.Bold,GraphicsUnit.Pixel))using(var b=new SolidBrush(Palette.Text))using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter,LineAlignment=StringAlignment.Center})g.DrawString(title,f,b,new RectangleF(14*s,7*s,Math.Max(1,width-90*s),25*s),format);}
 }
}
