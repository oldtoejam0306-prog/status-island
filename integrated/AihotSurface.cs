using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace IslandIntegrated {
  public static class AihotLayout {
    public const int Margin=16, Radius=24, CardWidth=360, CardHeight=176, NoticeHeight=80;
    public static Rectangle CardBody(int width,int height) { return new Rectangle(Margin,Margin,width,height); }
    public static Rectangle TitleHit(int width) { return new Rectangle(Margin+16,Margin+4,Math.Max(1,width-144),36); }
    // Card hit rectangles are in full frame coordinates, including Margin.
    public static Rectangle OpenHit(int width) { return new Rectangle(Margin+Math.Max(0,width-128),Margin+8,104,32); }
    public static Rectangle StoryHit(int width) { return new Rectangle(Margin+24,Margin+42,Math.Max(1,width-48),76); }
    public static Rectangle RefreshHit(int width) { return new Rectangle(Margin+24,Margin+126,94,32); }
    public static Rectangle PauseHit(int width) { return new Rectangle(Margin+122,Margin+126,112,32); }
    public static Rectangle NotificationBody(int margin) { return new Rectangle(margin,margin,CardWidth,NoticeHeight); }
    public static Rectangle NotificationStoryBounds(int margin) {
      return new Rectangle(margin+20,margin+28,CardWidth-42,44);
    }
    public static Rectangle NotificationCloseHit(int margin) {
      return new Rectangle(margin+CardWidth-36,margin+4,24,24);
    }
  }

  public static class AihotRenderer {
    static Color Text { get { return SystemInformation.HighContrast?SystemColors.WindowText:Palette.Text; } }
    static Color Secondary { get { return SystemInformation.HighContrast?SystemColors.WindowText:Palette.Secondary; } }
    static void Draw(Graphics g,string value,Font font,Color color,RectangleF box,int lines) {
      using(var brush=new SolidBrush(color)) using(var format=new StringFormat()) {
        format.Trimming=StringTrimming.EllipsisCharacter;
        format.FormatFlags=StringFormatFlags.LineLimit;
        format.LineAlignment=StringAlignment.Center;
        format.Alignment=StringAlignment.Near;
        if(lines==1)format.FormatFlags|=StringFormatFlags.NoWrap;
        g.DrawString(value??"",font,brush,box,format);
      }
    }
    static string Metadata(AihotItem item,string status) {
      string value=status??"";
      const string checkedPrefix="AIHOT 热点 · 已检查 ";
      if(value.StartsWith(checkedPrefix,StringComparison.Ordinal))return (item==null?"AIHOT":item.SourceName)+" · 检查 "+value.Substring(checkedPrefix.Length);
      const string current="AIHOT 热点 · 已更新 ";
      const string prior="已更新 · ";
      if(value.StartsWith(current,StringComparison.Ordinal))return (item==null?"AIHOT":item.SourceName)+" · 更新 "+value.Substring(current.Length);
      if(value.StartsWith(prior,StringComparison.Ordinal))return (item==null?"AIHOT":item.SourceName)+" · 更新 "+value.Substring(prior.Length);
      return value;
    }
    static void IconRefresh(Graphics g,float x,float y,Color color) {
      using(var p=new Pen(color,1.5f)){p.StartCap=p.EndCap=LineCap.Round;g.DrawArc(p,x,y,12,12,42,280);g.DrawLine(p,x+10,y+1,x+13,y+2);g.DrawLine(p,x+13,y+2,x+12,y+5);}
    }
    static void ActionMark(Graphics g,Rectangle r,string label,bool refresh,Font font) {
      float centerY=r.Top+r.Height/2f;
      if(refresh) { IconRefresh(g,r.Left+10,centerY-6,Secondary); Draw(g,label,font,Secondary,new RectangleF(r.Left+30,r.Top,r.Width-34,r.Height),1); }
      else { using(var p=new Pen(Secondary,1.7f)){p.StartCap=p.EndCap=LineCap.Round;if(label=="暂停提醒"){g.DrawLine(p,r.Left+15,centerY-6,r.Left+15,centerY+6);g.DrawLine(p,r.Left+21,centerY-6,r.Left+21,centerY+6);}else{g.DrawLine(p,r.Left+14,centerY-5,r.Left+14,centerY+5);g.DrawLine(p,r.Left+14,centerY-5,r.Left+23,centerY);g.DrawLine(p,r.Left+14,centerY+5,r.Left+23,centerY);}} Draw(g,label,font,Secondary,new RectangleF(r.Left+31,r.Top,r.Width-35,r.Height),1); }
    }
    public static Bitmap RenderCard(int width,int height,AihotItem item,string status,bool paused) {
      return RenderCardFrame(width,height,item,status,paused,true,false,false,false);
    }
    public static Bitmap RenderCardContent(int width,int height,AihotItem item,string status,bool paused) {
      return RenderCardFrame(width,height,item,status,paused,false,false,false,false);
    }
    public static Bitmap RenderCard(int width,int height,AihotItem item,string status,bool paused,bool refreshing,bool hovered,bool pressed) {
      return RenderCardFrame(width,height,item,status,paused,true,refreshing,hovered,pressed);
    }
    public static Bitmap RenderCardContent(int width,int height,AihotItem item,string status,bool paused,bool refreshing,bool hovered,bool pressed) {
      return RenderCardFrame(width,height,item,status,paused,false,refreshing,hovered,pressed);
    }
    static Bitmap RenderCardFrame(int width,int height,AihotItem item,string status,bool paused,bool material,bool refreshing,bool hovered,bool pressed) {
      var image=material?GlassSurface.StatusBackground(width,height,AihotLayout.Margin,AihotLayout.Radius):new Bitmap(width+2*AihotLayout.Margin,height+2*AihotLayout.Margin);
      using(var header=new Font("Microsoft YaHei UI",14,FontStyle.Bold,GraphicsUnit.Pixel))
      using(var story=new Font("Microsoft YaHei UI",16,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var meta=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var action=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var g=Graphics.FromImage(image)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var body=AihotLayout.CardBody(width,height);
        using(var clip=GlassSurface.Path(body,AihotLayout.Radius))g.SetClip(clip);
        Draw(g,"AIHOT助手",header,Text,new RectangleF(40,28,210,28),1);
        var link=AihotLayout.OpenHit(width);Draw(g,"打开 AIHOT",action,Text,link,1);
        string headline=item==null?"暂时无法获取热点 · 点击刷新":item.Title;
        Draw(g,headline,story,Text,new RectangleF(40,58,width-48,48),2);
        Draw(g,Metadata(item,status),meta,Secondary,new RectangleF(40,110,width-48,20),1);
        var refresh=AihotLayout.RefreshHit(width);
        if(refreshing||hovered||pressed) {
          var feedback=Rectangle.Inflate(refresh,-2,-1);
          using(var shape=GlassSurface.Path(feedback,10))
          using(var fill=new SolidBrush(Color.FromArgb(pressed?64:hovered?30:18,Palette.Accent)))
          using(var rim=new Pen(Color.FromArgb(pressed?112:hovered?80:48,Palette.Accent),1)) {
            g.FillPath(fill,shape);g.DrawPath(rim,shape);
          }
        }
        ActionMark(g,refresh,refreshing?"检查中…":"立即检查",true,action);
        var pause=AihotLayout.PauseHit(width);ActionMark(g,pause,paused?"恢复提醒":"暂停提醒",false,action);
      }
      return image;
    }
    public static Bitmap RenderNotification(AihotItem item,string feedback) {
      int margin=AihotLayout.Margin;var image=GlassSurface.StatusBackground(AihotLayout.CardWidth,AihotLayout.NoticeHeight,margin,AihotLayout.Radius);
      using(var metadata=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var story=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var g=Graphics.FromImage(image)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var body=AihotLayout.NotificationBody(margin);using(var clip=GlassSurface.Path(body,AihotLayout.Radius))g.SetClip(clip);
        Draw(g,"AIHOT助手 · 重要热点",metadata,Secondary,new RectangleF(body.Left+20,body.Top+8,270,20),1);
        Draw(g,String.IsNullOrWhiteSpace(feedback)?(item==null?"AIHOT 热点更新":item.Title):feedback,story,Text,AihotLayout.NotificationStoryBounds(margin),2);
        var close=AihotLayout.NotificationCloseHit(margin);using(var p=new Pen(Secondary,1.5f)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,close.Left+8,close.Top+8,close.Left+16,close.Top+16);g.DrawLine(p,close.Left+16,close.Top+8,close.Left+8,close.Top+16);}
      }
      return image;
    }
  }
}
