using System;using System.Collections.Generic;using System.Drawing;using System.Drawing.Drawing2D;using System.Globalization;using System.Linq;using System.Windows.Forms;
namespace IslandIntegrated {
 public sealed class EdgeWakeGate {long since=-1;public bool Tick(long now,bool edge,bool wasOpen,bool open,bool hidden){if(!edge){since=-1;return false;}if(open&&!wasOpen)return true;if(!hidden){since=-1;return false;}if(since<0){since=now;return false;}if(now-since<250)return false;since=now;return true;}}
 public static class WidgetGrid {
  public const int DefaultColumnWidth=360,Gap=24;
  public static bool Small(Card c){return c.Id=="autoloop"?c.AutoloopMode=="square":c.Id=="weather"?c.WeatherMode=="square":c.Id=="music"?c.MusicMode=="square":(c.Id=="clock"||c.Id=="calendar")&&c.ClockMode!="strip"&&c.SizeMode=="small";}
  public static int SmallWidth(int width){return Math.Max(1,(width-Gap)/2);}
   public static Dictionary<string,Rectangle> Arrange(IList<Card> list,int width,int aihotHeight=AihotLayout.CardHeight){var result=new Dictionary<string,Rectangle>();int half=SmallWidth(width),y=0;bool pending=false;
   foreach(var c in list){if(Small(c)){result[c.Id]=new Rectangle(pending?half+Gap:0,y,half,half);if(pending){y+=half+Gap;pending=false;}else pending=true;}
    else{if(pending){y+=half+Gap;pending=false;}int h=c.Id=="clock"&&c.SizeMode=="small"&&c.ClockMode=="strip"?120:c.Id=="calendar"||c.Id=="clock"?380:c.Id=="aihot"?aihotHeight:c.Id=="music"?208:c.Id=="weather"?176:c.Id=="autoloop"?176:132;result[c.Id]=new Rectangle(0,y,width,h);y+=h+Gap;}}
   return result;}
  public static int Height(IList<Card> list,int width,int aihotHeight=AihotLayout.CardHeight){var r=Arrange(list,width,aihotHeight);return r.Count==0?0:r.Values.Max(v=>v.Bottom)+Gap;}
 }
  // Compact A/C, long-strip B, and the expanded calendar belong to one persisted
  // time card. Rebuilding restores its month/selection without duplicating cards.
 public sealed class MiniClockCalendar : Control {
  public bool Editing;Bitmap content;long stamp;
   readonly bool calendar;readonly Func<DateTime> clock;readonly Timer timer=new Timer();DateTime month=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1),selected=DateTime.Today;string clockMode="digital";
  public bool CalendarMode {get{return calendar;}}
  public string ClockMode {get{return calendar?"calendar":clockMode;}}
  public DateTime SelectedDate {get{return selected;}}
  public DateTime DisplayMonth {get{return month;}}
  public event EventHandler ViewChanged;
  long Stamp {get{return ClockRenderStamp(clock());}}
  public static long ClockRenderStamp(DateTime value){return value.Ticks/TimeSpan.TicksPerMinute;}
  public static bool ClockNeedsRefresh(DateTime previous,DateTime current){return ClockRenderStamp(previous)!=ClockRenderStamp(current);}
  void ClearContent(){if(content!=null){content.Dispose();content=null;}}
  public void RestoreView(string selectedValue,string monthValue){DateTime value;if(DateTime.TryParseExact(selectedValue,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value))selected=value.Date;if(DateTime.TryParseExact(monthValue,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value))month=new DateTime(value.Year,value.Month,1);ClearContent();Invalidate();}
  protected override void OnSizeChanged(EventArgs e){ClearContent();base.OnSizeChanged(e);}
  public MiniClockCalendar(bool calendarMode):this(calendarMode,"digital",()=>DateTime.Now){}
  public MiniClockCalendar(bool calendarMode,Func<DateTime> localClock):this(calendarMode,"digital",localClock){}
  public MiniClockCalendar(bool calendarMode,string mode,Func<DateTime> localClock){if(localClock==null)throw new ArgumentNullException("localClock");clock=localClock;calendar=calendarMode;SetClockMode(mode);var today=clock().Date;month=new DateTime(today.Year,today.Month,1);selected=today;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.Transparent;timer.Interval=1000;stamp=Stamp;timer.Tick+=(s,e)=>{if(Visible&&!Editing&&stamp!=Stamp){ClearContent();Invalidate();}};timer.Start();UpdateAccessibleName();}
  void UpdateAccessibleName(){AccessibleName=calendar?"时间与月历":clockMode=="dial"?"表盘与数字时钟":clockMode=="strip"?"长条时钟":"数字时钟";}
  public void SetClockMode(string mode){string next=mode=="dial"?"dial":mode=="strip"?"strip":"digital";if(clockMode==next)return;clockMode=next;UpdateAccessibleName();ClearContent();Invalidate();}
  void PaintClock(Graphics g){var now=clock();
   if(clockMode=="strip"){PaintClockStrip(g,now);return;}
   using(var timeFont=new Font("Microsoft YaHei UI",32,FontStyle.Bold,GraphicsUnit.Pixel))
   using(var dateFont=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var text=new SolidBrush(Palette.Text))using(var secondary=new SolidBrush(Palette.Secondary)){
    if(clockMode=="dial"){
     using(var compactTime=new Font("Microsoft YaHei UI",24,FontStyle.Bold,GraphicsUnit.Pixel))using(var compactDate=new Font("Microsoft YaHei UI",9,FontStyle.Regular,GraphicsUnit.Pixel)){
      g.DrawString(now.ToString("HH:mm"),compactTime,text,new RectangleF(10,31,82,36));
      g.DrawString(now.ToString("M月d日"),compactDate,secondary,new RectangleF(11,70,80,18));
      float r=25,cx=Width-35,cy=Height-35;DrawDial(g,now,cx,cy,r);
     }
    }else{
     g.DrawString(now.ToString("HH:mm"),timeFont,text,new RectangleF(12,28,Width-24,48));
     g.DrawString(now.ToString("M月d日 dddd"),dateFont,secondary,new RectangleF(14,83,Width-28,22));
    }
   }
  }
  void PaintClockStrip(Graphics g,DateTime now){
   float scale=Math.Max(.7f,Math.Min(1.25f,Width/360f));
   using(var timeFont=new Font("Microsoft YaHei UI",40*scale,FontStyle.Bold,GraphicsUnit.Pixel))
   using(var detailFont=new Font("Microsoft YaHei UI",13*scale,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var weekFont=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var text=new SolidBrush(Palette.Text))using(var secondary=new SolidBrush(Palette.Secondary)){
     float left=16*scale,top=Math.Max(4,(Height-84*scale)/2f);
    g.DrawString(now.ToString("HH:mm"),timeFont,text,new RectangleF(left,top,Width-2*left,50*scale));
    float detailY=top+52*scale;
    g.DrawString(now.ToString("yyyy年M月d日 dddd"),detailFont,secondary,new RectangleF(left,detailY,Width-2*left,19*scale));
    int week=CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(now,CalendarWeekRule.FirstFourDayWeek,DayOfWeek.Monday);
    g.DrawString("第 "+week+" 周",weekFont,secondary,new RectangleF(left,detailY+18*scale,Width-2*left,17*scale));
   }
  }
  static void DrawDial(Graphics g,DateTime now,float cx,float cy,float r){
   using(var ring=new Pen(Color.FromArgb(100,Palette.Text),1))g.DrawEllipse(ring,cx-r,cy-r,2*r,2*r);
   for(int i=0;i<12;i++){double a=i*Math.PI/6;using(var p=new Pen(Color.FromArgb(120,Palette.Text),1))g.DrawLine(p,cx+(float)Math.Sin(a)*(r-4),cy-(float)Math.Cos(a)*(r-4),cx+(float)Math.Sin(a)*(r-7),cy-(float)Math.Cos(a)*(r-7));}
   double hour=(now.Hour%12+now.Minute/60.0)*Math.PI/6,minute=now.Minute*Math.PI/30;
   using(var p=new Pen(Palette.Text,2)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,cx,cy,cx+(float)Math.Sin(hour)*r*.45f,cy-(float)Math.Cos(hour)*r*.45f);g.DrawLine(p,cx,cy,cx+(float)Math.Sin(minute)*r*.68f,cy-(float)Math.Cos(minute)*r*.68f);}
   using(var brush=new SolidBrush(Palette.Accent))g.FillEllipse(brush,cx-2,cy-2,4,4);
  }
  int HeaderTop {get{return Editing?118:94;}}
  int DaysTop {get{return Editing?172:148;}}
  int RowHeight {get{return Editing?27:31;}}
  void PaintCalendar(Graphics g){var now=clock();using(var timeFont=new Font("Microsoft YaHei UI",40,FontStyle.Bold,GraphicsUnit.Pixel))using(var dateFont=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))using(var timeBrush=new SolidBrush(Palette.Text))using(var dateBrush=new SolidBrush(Palette.Secondary)){
   float timeY=Editing?38:12;g.DrawString(now.ToString("HH:mm"),timeFont,timeBrush,18,timeY);g.DrawString(now.ToString("yyyy年M月d日 dddd"),dateFont,dateBrush,20,timeY+43);
  }using(var font=new Font("Microsoft YaHei UI",10))using(var small=new Font("Microsoft YaHei UI",9))using(var text=new SolidBrush(Palette.Text))using(var muted=new SolidBrush(Palette.Secondary))using(var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
   g.DrawString(month.ToString("yyyy年M月"),font,text,new RectangleF(35,HeaderTop-4,Width-70,28),center);
   using(var pen=new Pen(Palette.Text,1.7f)){g.DrawLines(pen,new[]{new Point(25,HeaderTop+1),new Point(20,HeaderTop+6),new Point(25,HeaderTop+11)});g.DrawLines(pen,new[]{new Point(Width-25,HeaderTop+1),new Point(Width-20,HeaderTop+6),new Point(Width-25,HeaderTop+11)});}
   float cell=(Width-24)/7f;string[] names={"一","二","三","四","五","六","日"};for(int i=0;i<7;i++)g.DrawString(names[i],small,muted,new RectangleF(12+i*cell,DaysTop-27,cell,22),center);
   var first=new DateTime(month.Year,month.Month,1);int offset=((int)first.DayOfWeek+6)%7;
   for(int day=1;day<=DateTime.DaysInMonth(month.Year,month.Month);day++){int index=day-1+offset;float x=12+(index%7)*cell,y=DaysTop+(index/7)*RowHeight;var date=first.AddDays(day-1);bool active=date==selected.Date;
    if(active)using(var shape=GlassSurface.Path(new RectangleF(x+4,y+3,cell-8,RowHeight-6),9))using(var b=new SolidBrush(Color.FromArgb(222,Palette.Accent)))g.FillPath(b,shape);
    using(var b=new SolidBrush(active?Palette.SelectedText:Palette.Text))g.DrawString(day.ToString(),font,b,new RectangleF(x,y,cell,RowHeight),center);
    if(date==now.Date&&!active)using(var pen=new Pen(Color.FromArgb(160,Palette.Accent),1))g.DrawEllipse(pen,x+cell/2-13,y+7,26,26);
   }
   g.DrawString("已选 "+selected.ToString("M月d日 dddd"),small,muted,new RectangleF(12,Height-31,Width-24,23),center);
  }}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<1||Height<1)return;if(content!=null&&stamp!=Stamp)ClearContent();if(content==null){content=new Bitmap(Width,Height);using(var g=Graphics.FromImage(content)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;if(calendar)PaintCalendar(g);else PaintClock(g);if(Editing)EditCardChrome.Paint(g,"时间",Width,Math.Min(Width/(calendar?360f:168f),Height/(calendar?380f:168f)));}stamp=Stamp;}e.Graphics.DrawImageUnscaled(content,0,0);}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(!calendar||Editing||e.Button!=MouseButtons.Left)return;DateTime beforeMonth=month,beforeDate=selected;
   if(e.Y>=HeaderTop-8&&e.Y<HeaderTop+24){if(e.X<40&&month>DateTime.MinValue)month=month.AddMonths(-1);else if(e.X>Width-40&&(month.Year<9999||month.Month<12))month=month.AddMonths(1);}
   else if(e.Y>=DaysTop){float cell=(Width-24)/7f;int col=(int)Math.Floor((e.X-12)/cell),row=(e.Y-DaysTop)/RowHeight,offset=((int)new DateTime(month.Year,month.Month,1).DayOfWeek+6)%7,day=row*7+col-offset+1;if(col>=0&&col<7&&row>=0&&row<6&&day>=1&&day<=DateTime.DaysInMonth(month.Year,month.Month))selected=new DateTime(month.Year,month.Month,day);}
   if(beforeMonth==month&&beforeDate==selected)return;ClearContent();Invalidate();var changed=ViewChanged;if(changed!=null)changed(this,EventArgs.Empty);
  }
  protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();ClearContent();}base.Dispose(disposing);}
 }
}
