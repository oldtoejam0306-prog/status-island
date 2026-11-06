using System;using System.Collections.Generic;using System.Drawing;using System.Drawing.Drawing2D;using System.Globalization;using System.Linq;using System.Windows.Forms;
namespace IslandIntegrated {
 public sealed class EdgeWakeGate {long since=-1;public bool Tick(long now,bool edge,bool wasOpen,bool open,bool hidden){if(!edge){since=-1;return false;}if(open&&!wasOpen)return true;if(!hidden){since=-1;return false;}if(since<0){since=now;return false;}if(now-since<250)return false;since=now;return true;}}
 public static class WidgetGrid {
  public static bool Small(Card c){return c.Id=="autoloop"?c.AutoloopMode=="square":c.Id=="weather"?c.WeatherMode=="square":c.Id=="music"?c.MusicMode=="square":(c.Id=="clock"||c.Id=="calendar")&&c.SizeMode=="small";}
  public static Dictionary<string,Rectangle> Arrange(IList<Card> list,int width){var result=new Dictionary<string,Rectangle>();int gap=12,half=Math.Max(1,(width-gap)/2),y=0;bool pending=false;
   foreach(var c in list){if(Small(c)){result[c.Id]=new Rectangle(pending?half+gap:0,y,half,half);if(pending){y+=half+gap;pending=false;}else pending=true;}
    else{if(pending){y+=half+gap;pending=false;}int h=c.Id=="calendar"||c.Id=="clock"?380:c.Id=="music"?208:c.Id=="weather"?176:c.Id=="autoloop"?176:132;result[c.Id]=new Rectangle(0,y,width,h);y+=h+gap;}}
   return result;}
  public static int Height(IList<Card> list,int width){var r=Arrange(list,width);return r.Count==0?0:r.Values.Max(v=>v.Bottom)+12;}
 }
 // Both sizes belong to the same persisted time card. Rebuilding a view restores
 // its month/selection; changing size never adds or removes another component.
 public sealed class MiniClockCalendar : Control {
  public bool Editing;Bitmap content;long stamp;
  readonly bool calendar;readonly Func<DateTime> clock;readonly Timer timer=new Timer();DateTime month=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1),selected=DateTime.Today;
  public bool CalendarMode {get{return calendar;}}
  public DateTime SelectedDate {get{return selected;}}
  public DateTime DisplayMonth {get{return month;}}
  public event EventHandler ViewChanged;
  long Stamp {get{return clock().Ticks/TimeSpan.TicksPerMinute;}}
  void ClearContent(){if(content!=null){content.Dispose();content=null;}}
  public void RestoreView(string selectedValue,string monthValue){DateTime value;if(DateTime.TryParseExact(selectedValue,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value))selected=value.Date;if(DateTime.TryParseExact(monthValue,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value))month=new DateTime(value.Year,value.Month,1);ClearContent();Invalidate();}
  protected override void OnSizeChanged(EventArgs e){ClearContent();base.OnSizeChanged(e);}
  public MiniClockCalendar(bool calendarMode):this(calendarMode,()=>DateTime.Now){}
  public MiniClockCalendar(bool calendarMode,Func<DateTime> localClock){if(localClock==null)throw new ArgumentNullException("localClock");clock=localClock;calendar=calendarMode;var today=clock().Date;month=new DateTime(today.Year,today.Month,1);selected=today;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.Transparent;timer.Interval=1000;timer.Tick+=(s,e)=>{if(Visible&&!Editing&&stamp!=Stamp){ClearContent();Invalidate();}};timer.Start();AccessibleName=calendar?"时间与月历":"钟表表盘";}
  void PaintClock(Graphics g){float cx=Width/2f,cy=Height*.47f,r=Math.Min(Width,Height)*.315f;var now=clock();
   using(var ring=new Pen(Color.FromArgb(100,Palette.Text),1))g.DrawEllipse(ring,cx-r,cy-r,2*r,2*r);
   for(int i=0;i<60;i++){double a=i*Math.PI/30;float outer=r-4,inner=outer-(i%5==0?5:2);using(var p=new Pen(Color.FromArgb(i%5==0?220:76,Palette.Text),i%5==0?1.7f:.8f)){g.DrawLine(p,cx+(float)Math.Sin(a)*inner,cy-(float)Math.Cos(a)*inner,cx+(float)Math.Sin(a)*outer,cy-(float)Math.Cos(a)*outer);}}
   using(var font=new Font("Microsoft YaHei UI",8))using(var b=new SolidBrush(Palette.Secondary))using(var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){foreach(int n in new[]{12,3,6,9}){double a=n*Math.PI/6;g.DrawString(n.ToString(),font,b,new PointF(cx+(float)Math.Sin(a)*(r-16),cy-(float)Math.Cos(a)*(r-16)),sf);}g.DrawString(now.ToString("M月d日 ddd"),font,b,new RectangleF(5,Height-30,Width-10,22),sf);}
   double hour=(now.Hour%12+now.Minute/60.0)*Math.PI/6,minute=now.Minute*Math.PI/30;
   using(var p=new Pen(Palette.Text,3.5f)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,cx,cy,cx+(float)Math.Sin(hour)*r*.46f,cy-(float)Math.Cos(hour)*r*.46f);}
   using(var p=new Pen(Palette.Text,2)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,cx,cy,cx+(float)Math.Sin(minute)*r*.72f,cy-(float)Math.Cos(minute)*r*.72f);}
   using(var b=new SolidBrush(Palette.Accent))g.FillEllipse(b,cx-3,cy-3,6,6);
  }
  int HeaderTop {get{return Editing?118:94;}}
  int DaysTop {get{return Editing?172:148;}}
  int RowHeight {get{return Editing?27:31;}}
  void PaintCalendar(Graphics g){var now=clock();using(var timeFont=new Font("Microsoft YaHei UI",26,FontStyle.Bold))using(var dateFont=new Font("Microsoft YaHei UI",9))using(var timeBrush=new SolidBrush(Palette.Text))using(var dateBrush=new SolidBrush(Palette.Secondary)){
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
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<1||Height<1)return;if(content!=null&&stamp!=Stamp)ClearContent();if(content==null){content=new Bitmap(Width,Height);using(var g=Graphics.FromImage(content)){g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;if(calendar)PaintCalendar(g);else PaintClock(g);if(Editing)EditCardChrome.Paint(g,"时间",Width,Math.Min(Width/(calendar?344f:166f),Height/(calendar?380f:166f)));}stamp=Stamp;}e.Graphics.DrawImageUnscaled(content,0,0);}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(!calendar||Editing||e.Button!=MouseButtons.Left)return;DateTime beforeMonth=month,beforeDate=selected;
   if(e.Y>=HeaderTop-8&&e.Y<HeaderTop+24){if(e.X<40&&month>DateTime.MinValue)month=month.AddMonths(-1);else if(e.X>Width-40&&(month.Year<9999||month.Month<12))month=month.AddMonths(1);}
   else if(e.Y>=DaysTop){float cell=(Width-24)/7f;int col=(int)Math.Floor((e.X-12)/cell),row=(e.Y-DaysTop)/RowHeight,offset=((int)new DateTime(month.Year,month.Month,1).DayOfWeek+6)%7,day=row*7+col-offset+1;if(col>=0&&col<7&&row>=0&&row<6&&day>=1&&day<=DateTime.DaysInMonth(month.Year,month.Month))selected=new DateTime(month.Year,month.Month,day);}
   if(beforeMonth==month&&beforeDate==selected)return;ClearContent();Invalidate();var changed=ViewChanged;if(changed!=null)changed(this,EventArgs.Empty);
  }
  protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();ClearContent();}base.Dispose(disposing);}
 }
}
