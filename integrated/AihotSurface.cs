using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace IslandIntegrated {
  public static class AihotLayout {
    public const int Margin=16, Radius=24, CardWidth=360, CardHeight=176, NoticeHeight=80;
    public static int InboxHeight(int unreadCount,bool expanded) { return CardHeight; }
    public static Rectangle CardBody(int width,int height) { return new Rectangle(Margin,Margin,width,height); }
    public static Rectangle TitleHit(int width) { return new Rectangle(Margin+16,Margin+4,Math.Max(1,width-144),36); }
    // Card hit rectangles are in full frame coordinates, including Margin.
    public static Rectangle OpenHit(int width) { return new Rectangle(Margin+Math.Max(0,width-128),Margin+8,104,32); }
    public static Rectangle StoryHit(int width) { return new Rectangle(Margin+24,Margin+40,width-64,64); }
    public static Rectangle RefreshHit(int width) { return new Rectangle(Margin+24,Margin+126,94,32); }
    public static Rectangle PauseHit(int width) { return new Rectangle(Margin+122,Margin+126,112,32); }
    public static Rectangle NotificationBody(int margin) { return new Rectangle(margin,margin,CardWidth,NoticeHeight); }
    public static Rectangle NotificationStoryBounds(int margin) {
      return new Rectangle(margin+20,margin+28,CardWidth-42,44);
    }
    public static Rectangle NotificationCloseHit(int margin) {
      return new Rectangle(margin+CardWidth-36,margin+4,24,24);
    }
    public static Rectangle NotificationInboxHit(int margin) { return new Rectangle(margin+CardWidth-126,margin+4,82,24); }
    public static Rectangle ButtonHit(int width,int index) { int buttonWidth=Math.Max(1,(width-48)/3);return new Rectangle(Margin+16+index*(buttonWidth+8),Margin+135,buttonWidth,30); }
    public static Rectangle UnreadStoryHit(int width,int index) { return new Rectangle(Margin+24,Margin+50+index*22,Math.Max(1,width-120),22); }
    public static Rectangle UnreadReadHit(int width,int index) { return new Rectangle(width-76,Margin+50+index*22,52,22); }
    public static Rectangle ExpandHit(int width,int height) { return ButtonHit(width,2); }
    public static Rectangle RefreshHit(int width,int height) { return ButtonHit(width,0); }
    public static Rectangle PauseHit(int width,int height) { return ButtonHit(width,1); }
    public static Rectangle CompactReadHit(int width) { return new Rectangle(width-76,Margin+105,52,18); }
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
    public static Bitmap RenderInboxCard(int width,int height,IList<AihotItem> items,string status,bool paused,bool refreshing,bool expanded,bool readError,bool material) {
      return RenderInboxCard(width,height,items,status,paused,refreshing,expanded,readError,material,0);
    }
    public static Bitmap RenderInboxCard(int width,int height,IList<AihotItem> items,string status,bool paused,bool refreshing,bool expanded,bool readError,bool material,int scrollOffset) {
      return RenderInboxCard(width,height,items,status,paused,refreshing,expanded,readError,material,scrollOffset,false,false,false,false,false,false);
    }
    public static Bitmap RenderInboxCard(int width,int height,IList<AihotItem> items,string status,bool paused,bool refreshing,bool expanded,bool readError,bool material,int scrollOffset,bool refreshHover,bool refreshPress,bool pauseHover,bool pausePress,bool modeHover,bool modePress) {
      var image=material?GlassSurface.StatusBackground(width,height,AihotLayout.Margin,AihotLayout.Radius):new Bitmap(width+2*AihotLayout.Margin,height+2*AihotLayout.Margin);
      using(var title=new Font("Microsoft YaHei UI",15,FontStyle.Bold,GraphicsUnit.Pixel))
      using(var story=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var summary=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var meta=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var action=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var g=Graphics.FromImage(image)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var body=AihotLayout.CardBody(width,height);using(var clip=GlassSurface.Path(body,AihotLayout.Radius))g.SetClip(clip);
        Draw(g,"AIHOT助手",title,Text,new RectangleF(40,24,170,22),1);
        Draw(g,"打开 AIHOT",action,Text,new RectangleF(width-116,24,90,22),1);
        int total=items==null?0:items.Count;
        int capacity=expanded?3:1;int start=items==null?0:Math.Max(0,Math.Min(scrollOffset,Math.Max(0,items.Count-capacity)));int count=items==null?0:Math.Min(capacity,items.Count-start);
        if(count==0||expanded)Draw(g,total==0?"AIHOT 热点":"未读 "+total+" 条 · 显示 "+(start+1)+"–"+Math.Min(total,start+capacity),meta,Secondary,new RectangleF(40,49,width-64,16),1);
        if(count==0) {
          Draw(g,"暂时没有新消息",story,Text,new RectangleF(40,70,width-64,24),1);
          Draw(g,String.IsNullOrEmpty(status)?"等待下次检查":status,meta,Secondary,new RectangleF(40,96,width-64,18),1);
        } else if(expanded) {
          for(int i=0;i<count;i++) {
            AihotItem item=items[start+i];
            Rectangle row=AihotLayout.UnreadStoryHit(width,i);
            Draw(g,item.Title??"（无标题）",summary,Text,new RectangleF(row.Left,row.Top,row.Width,row.Height),1);
            var read=AihotLayout.UnreadReadHit(width,i);Draw(g,"已读",meta,Secondary,new RectangleF(read.Left,read.Top,read.Width,read.Height),1);
          }
        } else {
          AihotItem item=items[start];
          Draw(g,item.Title??"（无标题）",story,Text,new RectangleF(40,56,width-64,44),2);
          Draw(g,item.Summary??"暂无摘要",summary,Text,new RectangleF(40,101,width-64,18),1);
          string updated=item.LatestAt==DateTime.MinValue?"更新时间未知":item.LatestAt.ToLocalTime().ToString("HH:mm");
          string sources=item.SourceCount>0?item.SourceCount+" 个来源":(item.SourceName??"来源未知");
          Draw(g,updated+" · "+sources+" · "+(start+1)+"/"+total,meta,Secondary,new RectangleF(40,121,width-120,18),1);
          var read=AihotLayout.CompactReadHit(width);Draw(g,"已读",meta,Secondary,new RectangleF(read.Left,read.Top,read.Width,read.Height),1);
        }
        string[] labels={refreshing?"检查中…":"立即检查",paused?"恢复提醒":"暂停提醒",expanded?"返回摘要":"未读列表"};
        DrawActionButton(g,AihotLayout.ButtonHit(width,0),labels[0],0,refreshHover,refreshPress,refreshing,false,action);
        DrawActionButton(g,AihotLayout.ButtonHit(width,1),labels[1],1,pauseHover,pausePress,false,paused,action);
        DrawActionButton(g,AihotLayout.ButtonHit(width,2),labels[2],2,modeHover,modePress,false,expanded,action);
        if(readError)Draw(g,"已读状态保存失败，请重试",meta,SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(220,110,40),new RectangleF(40,119,width-120,14),1);
      }
      return image;
    }
    static void DrawActionButton(Graphics g,Rectangle bounds,string label,int index,bool hovered,bool pressed,bool disabled,bool active,Font font) {
      Rectangle r=bounds;if(pressed&&!disabled)r.Offset(0,1);
      int fill=disabled?10:pressed?58:hovered?38:active?34:20;int rim=disabled?24:pressed?116:hovered?92:active?84:52;
      Color accent=SystemInformation.HighContrast?SystemColors.WindowText:Palette.Accent;
      using(var path=GlassSurface.Path(r,9))using(var brush=new SolidBrush(Color.FromArgb(fill,accent)))using(var pen=new Pen(Color.FromArgb(rim,accent),1)){g.FillPath(brush,path);g.DrawPath(pen,path);}
      Color ink=disabled?Secondary:Text;float cy=r.Top+r.Height/2f;
      if(index==0)IconRefresh(g,r.Left+10,cy-6,ink);
      else if(index==1){using(var p=new Pen(ink,1.7f)){p.StartCap=p.EndCap=LineCap.Round;if(label.StartsWith("恢复")){g.DrawLine(p,r.Left+13,cy-6,r.Left+13,cy+6);g.DrawLine(p,r.Left+13,cy-6,r.Left+23,cy);g.DrawLine(p,r.Left+13,cy+6,r.Left+23,cy);}else{g.DrawLine(p,r.Left+12,cy-6,r.Left+12,cy+6);g.DrawLine(p,r.Left+19,cy-6,r.Left+19,cy+6);}}}
      else{using(var p=new Pen(ink,1.4f)){p.StartCap=p.EndCap=LineCap.Round;for(int i=0;i<3;i++){g.DrawLine(p,r.Left+10,cy-6+i*6,r.Left+20,cy-6+i*6);g.DrawEllipse(p,r.Left+5,cy-7+i*6,2,2);}}}
      using(var brush=new SolidBrush(ink))using(var format=new StringFormat()){format.Alignment=StringAlignment.Center;format.LineAlignment=StringAlignment.Center;format.FormatFlags=StringFormatFlags.NoWrap;g.DrawString(label,font,brush,new RectangleF(r.Left+25,r.Top,r.Width-27,r.Height),format);}
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
    public static Bitmap RenderNotification(AihotItem item,string feedback) { return RenderNotification(item,feedback,1); }
    public static Bitmap RenderNotification(AihotItem item,string feedback,int batchCount) {
      int margin=AihotLayout.Margin;var image=GlassSurface.StatusBackground(AihotLayout.CardWidth,AihotLayout.NoticeHeight,margin,AihotLayout.Radius);
      using(var metadata=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var story=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel))
      using(var g=Graphics.FromImage(image)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        var body=AihotLayout.NotificationBody(margin);using(var clip=GlassSurface.Path(body,AihotLayout.Radius))g.SetClip(clip);
        Draw(g,batchCount>1?"AIHOT助手 · 新增 "+batchCount+" 条":"AIHOT助手 · 重要热点",metadata,Secondary,new RectangleF(body.Left+20,body.Top+8,202,20),1);
        if(batchCount>1)Draw(g,"查看未读",metadata,Secondary,AihotLayout.NotificationInboxHit(margin),1);
        Draw(g,String.IsNullOrWhiteSpace(feedback)?(item==null?"AIHOT 热点更新":item.Title):feedback,story,Text,AihotLayout.NotificationStoryBounds(margin),2);
        var close=AihotLayout.NotificationCloseHit(margin);using(var p=new Pen(Secondary,1.5f)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,close.Left+8,close.Top+8,close.Left+16,close.Top+16);g.DrawLine(p,close.Left+16,close.Top+8,close.Left+8,close.Top+16);}
      }
      return image;
    }
  }
}
