using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
namespace IslandIntegrated {
 public enum WeatherKind {Unknown,Clear,Cloud,Rain,Snow}
 public sealed class WeatherSnapshot {
  public string City="",State="unconfigured",Description="",Attribution="";public double? Temperature,FeelsLike,High,Low;public DateTimeOffset? ObservedAt;public WeatherKind Kind;public double? Humidity,WindSpeed,Precipitation;public int OffsetSeconds;public string Timezone="";public bool RefreshFailed;public string FailureCode="";public int? HttpStatus;public DateTimeOffset? RetryAt;public System.Collections.Generic.List<WeatherHour> Hourly=new System.Collections.Generic.List<WeatherHour>();public System.Collections.Generic.List<WeatherDay> Daily=new System.Collections.Generic.List<WeatherDay>();public WeatherSnapshot FailedRefresh(){var copy=(WeatherSnapshot)MemberwiseClone();copy.RefreshFailed=true;return copy;}
  public static bool ValidCity(string value){if(String.IsNullOrWhiteSpace(value)||value.Length>60)return false;foreach(char c in value)if(Char.IsControl(c)||c=='/'||c=='\\'||c=='<'||c=='>')return false;return true;}
  static bool Valid(double? n){return !n.HasValue||(!Double.IsNaN(n.Value)&&!Double.IsInfinity(n.Value)&&n>=-100&&n<=100);}
  public string EffectiveState(DateTimeOffset now){if(!ValidCity(City))return "unconfigured";if(State!="fresh")return State;if(!ObservedAt.HasValue||!Temperature.HasValue||!Valid(Temperature)||!Valid(FeelsLike)||!Valid(High)||!Valid(Low)||High.HasValue&&Low.HasValue&&Low>High)return "invalid";double age=(now-ObservedAt.Value).TotalMinutes;return age < -5?"invalid":age>30||RefreshFailed?"stale":"fresh";}
 }
 public static class WeatherRefreshSchedule {
  public static TimeSpan Delay(WeatherSnapshot data,DateTimeOffset now){if(data==null)return TimeSpan.FromSeconds(30);string state=data.EffectiveState(now);if(state=="fresh"||state=="not-connected"||state=="unconfigured")return TimeSpan.FromMinutes(2);var delay=TimeSpan.FromSeconds(30);if(data.RetryAt.HasValue&&data.RetryAt.Value>now&&data.RetryAt.Value-now>delay)delay=data.RetryAt.Value-now;return delay;}
 }
 public interface IWeatherProvider {Task<WeatherSnapshot> ReadAsync(string city,CancellationToken cancelled);}
 public sealed class DisconnectedWeatherProvider:IWeatherProvider {public Task<WeatherSnapshot> ReadAsync(string city,CancellationToken cancelled){return Task.FromResult(new WeatherSnapshot{City=city??"",State=WeatherSnapshot.ValidCity(city)?"not-connected":"unconfigured"});}}
 // No HTTP provider or location discovery is enabled. A city and an explicitly
 // approved service are required before a future network adapter may be wired.
 public sealed class WeatherCardControl:Control {
  readonly Button choose=new GlassActionButton{Text="选择城市",AccessibleName="选择天气城市",FlatStyle=FlatStyle.Flat,BackColor=Color.Transparent};WeatherSnapshot snapshot=new WeatherSnapshot();Bitmap material;
  public bool Square=true,Editing;public event EventHandler ChooseCity,DetailRequested;public Func<DateTimeOffset> Clock=()=>DateTimeOffset.UtcNow;
  public WeatherCardControl(){SetStyle(ControlStyles.Selectable,true);TabStop=true;Cursor=Cursors.Hand;SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);BackColor=Color.Transparent;choose.Click+=(s,e)=>{var h=ChooseCity;if(h!=null)h(this,EventArgs.Empty);};Controls.Add(choose);AccessibleName="天气";ContextMenuStrip=WeatherCredit.Menu(()=>snapshot);}
  public void Present(WeatherSnapshot data){var next=data??new WeatherSnapshot();if(next.Kind!=snapshot.Kind&&material!=null){material.Dispose();material=null;}snapshot=next;AccessibleDescription=Status(snapshot,Clock())+"; "+snapshot.Attribution;LayoutChoice();Invalidate();}
  float MetricScale {get{return Math.Min(Width/(Square?166f:344f),Height/(Square?166f:176f));}}
  void LayoutChoice(){float s=Math.Max(.5f,MetricScale);bool ready=snapshot.EffectiveState(Clock())=="fresh"||snapshot.EffectiveState(Clock())=="stale";choose.Visible=!Editing&&!ready;choose.Text=String.IsNullOrWhiteSpace(snapshot.City)?"选择城市":"更换城市";choose.Bounds=new Rectangle((int)(14*s),(int)((Square?122:130)*s),(int)((Square?138:126)*s),(int)(28*s));}
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(material!=null){material.Dispose();material=null;}LayoutChoice();}
  static string Degree(double? value){return value.HasValue?value.Value.ToString("0")+"°":"—";}
  public static string Status(WeatherSnapshot value,DateTimeOffset now){switch(value.EffectiveState(now)){case "fresh":return (value.Attribution=="Open-Meteo"?"Open-Meteo · ":"更新 ")+value.ObservedAt.Value.ToOffset(TimeSpan.FromSeconds(value.OffsetSeconds)).ToString("HH:mm");case "stale":return "已过期 "+value.ObservedAt.Value.ToOffset(TimeSpan.FromSeconds(value.OffsetSeconds)).ToString("HH:mm")+(value.Attribution=="Open-Meteo"?" · Open-Meteo":"");case "unconfigured":return "请选择城市，不读取定位";case "not-connected":return "尚未配置天气服务";case "invalid":return "天气数据无效";default:return value.FailureCode=="timeout"?"天气请求超时":value.FailureCode=="rate-limited"?"天气服务限流，稍后重试":value.FailureCode=="proxy-authentication"?"天气代理需要认证":value.FailureCode=="certificate"?"天气证书验证失败":value.FailureCode=="secure-channel"?"天气安全连接失败":value.FailureCode=="invalid-response"?"天气数据异常":value.FailureCode=="http-error"?"天气服务响应异常":value.FailureCode=="transport"?"天气连接失败":"天气暂不可用";}}
  public static void DrawIcon(Graphics g,RectangleF r,WeatherKind kind,Color color){var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/40,r.Height/40);using(var p=new Pen(color,2.2f)){p.StartCap=p.EndCap=LineCap.Round;if(kind==WeatherKind.Clear){g.DrawEllipse(p,11,11,18,18);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,20+(float)Math.Cos(a)*13,20+(float)Math.Sin(a)*13,20+(float)Math.Cos(a)*17,20+(float)Math.Sin(a)*17);}}else{using(var path=new GraphicsPath()){path.AddBezier(8,28,1,27,1,17,10,16);path.AddBezier(10,16,10,4,27,4,29,16);path.AddBezier(29,16,40,15,41,29,31,29);path.AddLine(31,29,8,29);g.DrawPath(p,path);}if(kind==WeatherKind.Rain||kind==WeatherKind.Snow)for(int i=0;i<3;i++){float x=11+i*9;g.DrawLine(p,x,33,x-2,37);}}}g.Restore(state);}
  void TextAt(Graphics g,string value,float px,FontStyle style,Brush brush,RectangleF r){using(var f=new Font("Microsoft YaHei UI",px,style,GraphicsUnit.Pixel))using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter,LineAlignment=StringAlignment.Center})g.DrawString(value??"",f,brush,r,format);}
  public string PaintedStatus {get{string state=snapshot.EffectiveState(Clock());return state=="fresh"?"":state=="stale"?"\u5df2\u8fc7\u671f":Status(snapshot,Clock());}}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<2||Height<2)return;float s=Math.Max(.5f,MetricScale);var g=e.Graphics;if(material==null)material=WeatherMaterial.Create(Size,snapshot.Kind);g.DrawImageUnscaled(material,0,0);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;if(Editing)EditCardChrome.Paint(g,"天气",Width,s);string state=snapshot.EffectiveState(Clock());bool values=state=="fresh"||state=="stale";using(var text=new SolidBrush(WeatherMaterial.Text))using(var secondary=new SolidBrush(WeatherMaterial.Secondary)){
   if(!Square){
    float top=Editing?42:12;float available=Width-28*s;
    TextAt(g,WeatherSnapshot.ValidCity(snapshot.City)?snapshot.City:"\u5929\u6c14",14*s,FontStyle.Bold,text,new RectangleF(14*s,top*s,available,23*s));
    if(values){
     DrawIcon(g,new RectangleF(16*s,(top+34)*s,42*s,42*s),snapshot.Kind,WeatherMaterial.Text);
     TextAt(g,Degree(snapshot.Temperature),40*s,FontStyle.Regular,text,new RectangleF(69*s,(top+25)*s,103*s,53*s));
     TextAt(g,snapshot.Description,16*s,FontStyle.Regular,text,new RectangleF(185*s,(top+28)*s,Width-201*s,25*s));
     TextAt(g,"\u4f53\u611f "+Degree(snapshot.FeelsLike),14*s,FontStyle.Regular,secondary,new RectangleF(185*s,(top+55)*s,Width-201*s,24*s));
     TextAt(g,"\u9ad8 "+Degree(snapshot.High)+"  \u4f4e "+Degree(snapshot.Low),14*s,FontStyle.Regular,text,new RectangleF(16*s,(Editing?138:115)*s,164*s,23*s));
     TextAt(g,state=="stale"?PaintedStatus:"\u964d\u6c34 "+(snapshot.Precipitation.HasValue?snapshot.Precipitation.Value.ToString("0.#")+" mm":"\u2014"),13.5f*s,FontStyle.Regular,secondary,new RectangleF(185*s,(Editing?138:115)*s,Width-201*s,23*s));
    }else TextAt(g,PaintedStatus,14*s,FontStyle.Regular,secondary,new RectangleF(16*s,(top+32)*s,Width-32*s,50*s));
    return;
   }
   if(Editing){
    TextAt(g,snapshot.City,13*s,FontStyle.Bold,text,new RectangleF(14*s,39*s,Width-28*s,22*s));
    if(values){DrawIcon(g,new RectangleF(Width-51*s,68*s,34*s,34*s),snapshot.Kind,WeatherMaterial.Text);TextAt(g,Degree(snapshot.Temperature),32*s,FontStyle.Regular,text,new RectangleF(14*s,63*s,92*s,41*s));TextAt(g,snapshot.Description,13*s,FontStyle.Regular,secondary,new RectangleF(14*s,108*s,Width-28*s,20*s));TextAt(g,"\u9ad8 "+Degree(snapshot.High)+"  \u4f4e "+Degree(snapshot.Low),13*s,FontStyle.Regular,text,new RectangleF(14*s,132*s,Width-28*s,22*s));}
    else TextAt(g,PaintedStatus,13*s,FontStyle.Regular,secondary,new RectangleF(14*s,69*s,Width-28*s,55*s));return;
   }
   TextAt(g,WeatherSnapshot.ValidCity(snapshot.City)?snapshot.City:"\u5929\u6c14",14*s,FontStyle.Bold,text,new RectangleF(14*s,12*s,Width-28*s,23*s));
   if(values){DrawIcon(g,new RectangleF(Width-52*s,46*s,36*s,36*s),snapshot.Kind,WeatherMaterial.Text);TextAt(g,Degree(snapshot.Temperature),34*s,FontStyle.Regular,text,new RectangleF(14*s,42*s,96*s,48*s));TextAt(g,snapshot.Description,13.5f*s,FontStyle.Regular,secondary,new RectangleF(14*s,91*s,Width-28*s,21*s));TextAt(g,"\u9ad8 "+Degree(snapshot.High)+"  \u4f4e "+Degree(snapshot.Low),13.5f*s,FontStyle.Regular,text,new RectangleF(14*s,114*s,Width-28*s,21*s));TextAt(g,state=="stale"?PaintedStatus:"\u964d\u6c34 "+(snapshot.Precipitation.HasValue?snapshot.Precipitation.Value.ToString("0.#")+" mm":"\u2014"),12.5f*s,FontStyle.Regular,secondary,new RectangleF(14*s,138*s,Width-28*s,19*s));}
   else{DrawIcon(g,new RectangleF(Width-55*s,47*s,35*s,35*s),WeatherKind.Unknown,WeatherMaterial.Secondary);TextAt(g,PaintedStatus,13*s,FontStyle.Regular,secondary,new RectangleF(14*s,57*s,Width-72*s,50*s));}
  }}  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(!Editing&&e.Button==MouseButtons.Left){var action=DetailRequested;if(action!=null)action(this,EventArgs.Empty);}}
  protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(!Editing&&(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space)){e.Handled=true;e.SuppressKeyPress=true;var action=DetailRequested;if(action!=null)action(this,EventArgs.Empty);}}
  protected override void Dispose(bool disposing){if(disposing){if(material!=null){material.Dispose();material=null;}if(ContextMenuStrip!=null)ContextMenuStrip.Dispose();}base.Dispose(disposing);}
 }
}
