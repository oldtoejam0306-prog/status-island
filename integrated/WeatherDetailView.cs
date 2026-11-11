using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IslandIntegrated {
  public sealed class WeatherSearchBox:TextBox {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr parameter,string text);
    Font scaledFont;
    public bool Composing {get;private set;}
    public void ApplyScale(float scale){
      float pixels=Math.Max(12,14*scale);
      if(scaledFont!=null&&Math.Abs(scaledFont.Size-pixels)<.05f)return;
      var previous=scaledFont;scaledFont=new Font("Microsoft YaHei UI",pixels,FontStyle.Regular,GraphicsUnit.Pixel);Font=scaledFont;
      if(previous!=null)previous.Dispose();
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);SendMessage(Handle,0x1501,new IntPtr(1),"城市或地区");}
    protected override void WndProc(ref Message m){if(m.Msg==0x10D)Composing=true;if(m.Msg==0x10E)Composing=false;base.WndProc(ref m);}
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&scaledFont!=null){scaledFont.Dispose();scaledFont=null;}}
  }

  public sealed class WeatherChromeButton:Button {
    public bool CloseGlyph;
    public int Arrow;
    bool hovered,pressed;
    readonly Font labelFont=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel);
    public WeatherChromeButton(){
      SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
      FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;BackColor=Color.Transparent;UseVisualStyleBackColor=false;Text="";AccessibleRole=AccessibleRole.PushButton;
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected override void OnPaint(PaintEventArgs e){
      using(var frame=new Bitmap(Math.Max(1,Width),Math.Max(1,Height)))using(var g=Graphics.FromImage(frame)){
        var parent=Parent as WeatherDetailView;
        if(parent!=null)parent.PaintChromeBackdrop(g,Bounds);else g.Clear(Palette.Base);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(!String.IsNullOrEmpty(Text)||hovered||Focused&&ShowFocusCues)
          using(var shape=GlassSurface.Path(new RectangleF(.5f,.5f,Width-1,Height-1),10))
          using(var fill=new SolidBrush(Color.FromArgb(pressed?48:hovered?30:15,Palette.Face)))g.FillPath(fill,shape);
        Color color=Enabled?Palette.Text:Color.FromArgb(115,Palette.Secondary);
        float x=Width/2f,y=Height/2f;
        using(var pen=new Pen(color,1.6f)){
          pen.StartCap=pen.EndCap=LineCap.Round;
          if(CloseGlyph){g.DrawLine(pen,x-4,y-4,x+4,y+4);g.DrawLine(pen,x+4,y-4,x-4,y+4);}
          else if(Arrow!=0)g.DrawLines(pen,new[]{new PointF(x-2*Arrow,y-5),new PointF(x+3*Arrow,y),new PointF(x-2*Arrow,y+5)});
          else using(var ink=new SolidBrush(color))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            g.DrawString(Text,labelFont,ink,ClientRectangle,format);
        }
        e.Graphics.DrawImageUnscaled(frame,0,0);
      }
    }
    protected override void Dispose(bool disposing){if(disposing)labelFont.Dispose();base.Dispose(disposing);}
  }

  public sealed class WeatherDetailView:Control {
    public const int ContentHeight=800;
    readonly WeatherSearchBox query=new WeatherSearchBox{MaxLength=60,BorderStyle=BorderStyle.None,AccessibleName="城市或地区"};
    readonly WeatherChromeButton search=new WeatherChromeButton{Text="搜索",AccessibleName="搜索城市"},home=new WeatherChromeButton{Text="返回常驻"},setHome=new WeatherChromeButton{Text="设为常驻"},previous=new WeatherChromeButton{Arrow=-1,AccessibleName="前8小时"},next=new WeatherChromeButton{Arrow=1,AccessibleName="后8小时"},refresh=new WeatherChromeButton{Text="刷新"};
    readonly WeatherChromeButton cities=new WeatherChromeButton{Text="城市",AccessibleName="城市列表"},close=new WeatherChromeButton{CloseGlyph=true,AccessibleName="关闭天气详情"};
    readonly ListBox results=new ListBox{Visible=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=54,IntegralHeight=false,BorderStyle=BorderStyle.None,AccessibleName="城市列表，单击或回车选择"};
    readonly ToolTip tips=new ToolTip();
    readonly WeatherDetailNavigation navigation=new WeatherDetailNavigation();
    readonly System.Windows.Forms.Timer ageTimer=new System.Windows.Forms.Timer{Interval=30000};
    List<WeatherCityChoice> recent=new List<WeatherCityChoice>();
    WeatherSnapshot snapshot=new WeatherSnapshot();
    Bitmap material;
    bool drawer,searchResults;
    string materialKey="",lastState="";
    public string Notice="";
    public bool Busy,SearchAllowed,RefreshAvailable,CanSetHome,CurrentIsHome;
    public string HomeName="";
    public event Action<string> SearchRequested,LocalCityChosen;
    public event Action<WeatherPlace> PlaceChosen;
    public event Action<WeatherCityChoice> RecentCityChosen;
    public event EventHandler HomeRequested,SetHomeRequested,RefreshRequested,CloseRequested,DragRequested,ContentSizeChanged;
    public WeatherSnapshot Snapshot {get{return snapshot;}}
    public int HourOffset {get{return navigation.HourOffset;}}
    public bool CityDrawerVisible {get{return drawer;}}
    internal bool DismissalProtected {
      get { return query.Composing || (ContextMenuStrip != null && ContextMenuStrip.Visible); }
    }
    public int DesiredHeight {get{return drawer?Math.Max(340,264+Math.Min(8,results.Items.Count)*54):ContentHeight;}}
    bool HasValues {get{string state=snapshot.EffectiveState(DateTimeOffset.UtcNow);return state=="fresh"||state=="stale";}}
    public WeatherDetailView(){
      SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
      BackColor=Palette.Base;Size=new Size(CornerLayout.Width,ContentHeight);AccessibleName="天气详情";
      ContextMenuStrip=WeatherCredit.Menu(()=>snapshot);
      Controls.AddRange(new Control[]{query,search,home,setHome,previous,next,refresh,results,cities,close});
      query.ApplyScale(1);query.BackColor=Palette.Top;query.ForeColor=Palette.Text;results.BackColor=Palette.Base;results.ForeColor=Palette.Text;
      tips.SetToolTip(cities,"搜索与最近查看的城市");tips.SetToolTip(close,"关闭天气详情");tips.SetToolTip(previous,"前8小时");tips.SetToolTip(next,"后8小时");
      cities.Click+=(s,e)=>ShowCitySearch(!drawer);
      close.Click+=(s,e)=>Raise(CloseRequested);
      search.Click+=(s,e)=>{
        if(Busy)return;
        string text=query.Text.Trim();
        if(!WeatherSnapshot.ValidCity(text)){Notice="请输入城市或地区名称";Invalidate();return;}
        var action=SearchAllowed?SearchRequested:LocalCityChosen;if(action!=null)action(text);
      };
      query.KeyDown+=(s,e)=>{
        if(query.Composing)return;
        if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;search.PerformClick();}
        else if(e.KeyCode==Keys.Down&&results.Visible&&results.Items.Count>0){e.SuppressKeyPress=true;results.SelectedIndex=0;results.Focus();}
      };
      query.TextChanged+=(s,e)=>{if(drawer&&!Busy)ShowRecent();};
      home.Click+=(s,e)=>Raise(HomeRequested);setHome.Click+=(s,e)=>Raise(SetHomeRequested);refresh.Click+=(s,e)=>Raise(RefreshRequested);
      previous.Click+=(s,e)=>{navigation.Move(-8,snapshot.Hourly.Count);UpdateControls();Invalidate();};
      next.Click+=(s,e)=>{navigation.Move(8,snapshot.Hourly.Count);UpdateControls();Invalidate();};
      results.DrawItem+=DrawCity;
      results.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left&&results.IndexFromPoint(e.Location)>=0)PickCity();};
      results.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;PickCity();}else if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;ShowCitySearch(false);}};
      ageTimer.Tick+=(s,e)=>{if(!Visible)return;string state=snapshot.EffectiveState(DateTimeOffset.UtcNow);if(state!=lastState){lastState=state;Invalidate();}};
      ageTimer.Start();UpdateControls();
    }
    void Raise(EventHandler handler){if(handler!=null)handler(this,EventArgs.Empty);}
    public void SetRecentCities(IEnumerable<WeatherCityChoice> values){recent=WeatherCityHistory.Normalize(values);if(drawer&&!searchResults)ShowRecent();}
    public void SetQuery(string value){query.Text=value??"";}
    public void Present(WeatherSnapshot value,string notice){snapshot=value??new WeatherSnapshot();Notice=notice??"";navigation.Reset();drawer=!WeatherSnapshot.ValidCity(snapshot.City);searchResults=false;UpdateControls();if(drawer)ShowRecent();else Raise(ContentSizeChanged);Invalidate();}
    public void UpdateState(bool busy,bool searchAllowed,string notice){Busy=busy;SearchAllowed=searchAllowed;Notice=notice??"";UpdateControls();Invalidate();}
    public void ShowCitySearch(bool visible){drawer=visible;searchResults=false;UpdateControls();if(visible){if(query.Text.Length>0)query.Clear();else ShowRecent();query.Focus();}else Raise(ContentSizeChanged);Invalidate();}
    public void ShowPlaces(IList<WeatherPlace> places){
      drawer=true;searchResults=true;results.Items.Clear();
      if(places!=null)foreach(var place in places){var choice=WeatherCityChoice.FromPlace(place);if(choice!=null&&choice.Valid)results.Items.Add(choice);}
      UpdateControls();if(results.Items.Count>0){results.SelectedIndex=0;results.Focus();}else Notice="没有匹配城市，可补充省份或国家";
      Raise(ContentSizeChanged);
      Invalidate();
    }
    void ShowRecent(){
      searchResults=false;results.Items.Clear();string filter=query.Text.Trim();
      foreach(var city in recent)if(filter.Length==0||city.DisplayName.IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0)results.Items.Add(city.Copy());
      results.Visible=drawer&&results.Items.Count>0;UpdateControls();Raise(ContentSizeChanged);Invalidate();
    }
    void PickCity(){
      if(Busy)return;
      var choice=results.SelectedItem as WeatherCityChoice;if(choice==null)return;
      bool fromSearch=searchResults;drawer=false;UpdateControls();Raise(ContentSizeChanged);Invalidate();
      if(fromSearch){var place=choice.ToPlace();var action=PlaceChosen;if(place!=null&&action!=null)action(place);}
      else{var action=RecentCityChosen;if(action!=null)action(choice.Copy());}
    }
    void DrawCity(object sender,DrawItemEventArgs e){
      if(e.Index<0||e.Index>=results.Items.Count)return;
      var city=(WeatherCityChoice)results.Items[e.Index];var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
      EnsureMaterial();g.DrawImage(material,e.Bounds,new Rectangle(results.Left+e.Bounds.Left,results.Top+e.Bounds.Top,e.Bounds.Width,e.Bounds.Height),GraphicsUnit.Pixel);
      if((e.State&DrawItemState.Selected)!=0)using(var path=GlassSurface.Path(Rectangle.Inflate(e.Bounds,-2,-2),12))using(var fill=new SolidBrush(Palette.Top))g.FillPath(fill,path);
      string detail=String.Join(" · ",new[]{city.Region,city.Country}.Where(x=>!String.IsNullOrWhiteSpace(x)&&x!=city.Name));
      TextAt(g,city.Name,16,FontStyle.Regular,Palette.Text,new RectangleF(e.Bounds.Left+12,e.Bounds.Top+(detail.Length==0?14:5),e.Bounds.Width-24,23));
      if(detail.Length>0)TextAt(g,detail,12,FontStyle.Regular,Palette.Secondary,new RectangleF(e.Bounds.Left+12,e.Bounds.Top+29,e.Bounds.Width-24,18));
      if((e.State&DrawItemState.Focus)!=0)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(e.Bounds,-4,-4),Palette.Accent,results.BackColor);
    }
    void UpdateControls(){
      cities.Text=drawer?"返回":"城市";cities.AccessibleName=drawer?"返回天气详情":"城市列表";query.Visible=search.Visible=drawer;query.Enabled=search.Enabled=results.Enabled=!Busy;
      search.Text=Busy?"搜索中…":SearchAllowed?"搜索":"保存";
      results.Visible=drawer&&results.Items.Count>0;
      home.Visible=drawer&&RefreshAvailable;home.Enabled=!Busy;
      setHome.Visible=RefreshAvailable&&HasValues&&!CurrentIsHome;setHome.Enabled=!Busy&&CanSetHome&&!CurrentIsHome;setHome.Text=CurrentIsHome?"常驻城市":"设为常驻";
      previous.Visible=next.Visible=HasValues&&!drawer;previous.Enabled=navigation.HourOffset>0;next.Enabled=navigation.HourOffset+8<snapshot.Hourly.Count;
      refresh.Visible=RefreshAvailable&&!drawer;refresh.Enabled=!Busy;
      LayoutControls();
    }
    Rectangle SearchSurface {get{return new Rectangle(24,68,Math.Max(80,Width-48-80),42);}}
    void LayoutControls(){
      if(query==null)return;
      cities.SetBounds(Width-128,12,60,32);close.SetBounds(Width-56,12,32,32);
      var field=SearchSurface;query.SetBounds(field.Left+12,field.Top+(field.Height-query.PreferredHeight)/2,Math.Max(1,field.Width-24),query.PreferredHeight);
      search.SetBounds(field.Right+8,field.Top,72,field.Height);
      results.SetBounds(24,146,Math.Max(1,Width-48),Math.Min(Math.Max(54,Height-264),Math.Max(54,results.Items.Count*results.ItemHeight)));
      home.SetBounds(24,Height-104,100,32);
      setHome.SetBounds(Width-120,drawer?Height-104:66,96,30);
      refresh.SetBounds(Width-84,226,60,28);previous.SetBounds(Width-96,344,32,28);next.SetBounds(Width-56,344,32,28);
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ClearMaterial();LayoutControls();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&e.Y<50&&e.X<Width-136)Raise(DragRequested);}
    void ClearMaterial(){if(material!=null){material.Dispose();material=null;}}
    void EnsureMaterial(){
      string key=Palette.Theme+"|"+Palette.Solid+"|"+Palette.Legacy+"|"+SystemInformation.HighContrast;
      if(material!=null&&key==materialKey)return;
      ClearMaterial();materialKey=key;material=new Bitmap(Math.Max(1,Width),Math.Max(1,Height));
      using(var g=Graphics.FromImage(material)){g.Clear(Palette.Base);GlassSurface.Paint(g,new RectangleF(0,0,Width,Height),24,false);}
    }
    internal void PaintChromeBackdrop(Graphics g,Rectangle bounds){EnsureMaterial();g.DrawImage(material,new Rectangle(Point.Empty,bounds.Size),bounds,GraphicsUnit.Pixel);}
    static string Temp(double? value){return value.HasValue?value.Value.ToString("0")+"°":"—";}
    static string Metric(double? value,string suffix){return value.HasValue?value.Value.ToString("0.#")+suffix:"—";}
    static void TextAt(Graphics g,string text,float pixels,FontStyle style,Color color,RectangleF box,StringAlignment align=StringAlignment.Near){
      using(var font=new Font("Microsoft YaHei UI",pixels,style,GraphicsUnit.Pixel))using(var ink=new SolidBrush(color))
      using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter,Alignment=align,LineAlignment=StringAlignment.Center})g.DrawString(text??"",font,ink,box,format);
    }
    void Divider(Graphics g,float y){using(var pen=new Pen(Color.FromArgb(30,Palette.Secondary),1))g.DrawLine(pen,24,y,Width-24,y);}
    protected override void OnPaint(PaintEventArgs e){
      base.OnPaint(e);if(Width<2||Height<2)return;EnsureMaterial();var g=e.Graphics;g.DrawImageUnscaled(material,0,0);
      g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
      Color text=Palette.Text,muted=Palette.Secondary;float available=Width-48;
      TextAt(g,drawer?"城市":"天气",16,FontStyle.Bold,text,new RectangleF(24,12,Width-164,32));
      if(drawer){
        using(var path=GlassSurface.Path(SearchSurface,12))using(var fill=new SolidBrush(Palette.Top))g.FillPath(fill,path);
        TextAt(g,searchResults?"搜索结果":"最近查看",13,FontStyle.Bold,muted,new RectangleF(26,118,available,22));
        if(results.Items.Count==0)TextAt(g,searchResults?"没有匹配城市":query.Text.Trim().Length>0?"点搜索查找这个城市":"查过的城市会保存在这里",14,FontStyle.Regular,muted,new RectangleF(24,166,available,36));
        TextAt(g,Notice,12,FontStyle.Regular,muted,new RectangleF(24,Height-58,available,38));
        return;
      }
      string name=WeatherSnapshot.ValidCity(snapshot.City)?snapshot.City:"选择城市";
      TextAt(g,name,22,FontStyle.Regular,text,new RectangleF(24,64,available-(setHome.Visible?104:0),34));
      if(!HasValues){
        WeatherCardControl.DrawIcon(g,new RectangleF(24,120,48,48),WeatherKind.Unknown,muted);
        TextAt(g,Busy?"正在获取天气…":WeatherCardControl.Status(snapshot,DateTimeOffset.UtcNow),16,FontStyle.Regular,text,new RectangleF(24,186,available,56));
        TextAt(g,Notice,13,FontStyle.Regular,muted,new RectangleF(24,262,available,64));return;
      }
      TextAt(g,Temp(snapshot.Temperature),56,FontStyle.Regular,text,new RectangleF(20,100,available-48,72));
      WeatherCardControl.DrawIcon(g,new RectangleF(Width-84,112,44,44),snapshot.Kind,text);
      TextAt(g,snapshot.Description,16,FontStyle.Regular,text,new RectangleF(24,174,available,25));
      TextAt(g,"最高 "+Temp(snapshot.High)+"   最低 "+Temp(snapshot.Low),13,FontStyle.Regular,muted,new RectangleF(24,201,available,23));
      TextAt(g,Busy?"正在刷新…":WeatherCardControl.Status(snapshot,DateTimeOffset.UtcNow),12,FontStyle.Regular,muted,new RectangleF(24,229,available-68,23));
      Divider(g,264);
      string[] names={"体感","湿度","风速","降水"};
      string[] values={Temp(snapshot.FeelsLike),Metric(snapshot.Humidity,"%"),Metric(snapshot.WindSpeed," km/h"),Metric(snapshot.Precipitation," mm")};
      for(int i=0;i<4;i++){float x=24+i*available/4;TextAt(g,names[i],12,FontStyle.Regular,muted,new RectangleF(x,278,available/4-3,18));TextAt(g,values[i],14,FontStyle.Regular,text,new RectangleF(x,301,available/4-3,24));}
      TextAt(g,"逐小时",14,FontStyle.Bold,text,new RectangleF(24,344,available-80,28));
      int hours=Math.Min(8,Math.Max(0,snapshot.Hourly.Count-navigation.HourOffset));
      if(hours==0)TextAt(g,"暂无逐小时数据",13,FontStyle.Regular,muted,new RectangleF(24,390,available,48));
      for(int i=0;i<hours;i++){
        var hour=snapshot.Hourly[navigation.HourOffset+i];float x=24+i*available/8,cell=available/8;
        TextAt(g,hour.Time.ToOffset(TimeSpan.FromSeconds(snapshot.OffsetSeconds)).ToString("HH:mm"),12,FontStyle.Regular,muted,new RectangleF(x,386,cell,18),StringAlignment.Center);
        WeatherCardControl.DrawIcon(g,new RectangleF(x+(cell-20)/2,412,20,20),hour.Kind,text);
        TextAt(g,Temp(hour.Temperature),14,FontStyle.Regular,text,new RectangleF(x,440,cell,22),StringAlignment.Center);
        TextAt(g,Metric(hour.PrecipitationProbability,"%"),12,FontStyle.Regular,muted,new RectangleF(x,466,cell,17),StringAlignment.Center);
      }
      Divider(g,496);TextAt(g,"未来 7 天",14,FontStyle.Bold,text,new RectangleF(24,508,available,26));
      int days=Math.Min(7,snapshot.Daily.Count);
      if(days==0)TextAt(g,"暂无多日数据",13,FontStyle.Regular,muted,new RectangleF(24,548,available,48));
      for(int i=0;i<days;i++){
        var day=snapshot.Daily[i];float y=544+i*25;
        TextAt(g,day.Time.ToOffset(TimeSpan.FromSeconds(snapshot.OffsetSeconds)).ToString("MM/dd"),12,FontStyle.Regular,muted,new RectangleF(24,y,48,24));
        WeatherCardControl.DrawIcon(g,new RectangleF(84,y+2,20,20),day.Kind,text);
        TextAt(g,day.Description+(day.Precipitation.HasValue?" · "+Metric(day.Precipitation," mm"):""),12,FontStyle.Regular,muted,new RectangleF(116,y,Math.Max(35,available-202),24));
        TextAt(g,Temp(day.Low)+" / "+Temp(day.High),13,FontStyle.Regular,text,new RectangleF(Width-118,y,94,24),StringAlignment.Far);
      }
      TextAt(g,Notice,12,FontStyle.Regular,muted,new RectangleF(24,738,available,42));
    }
    protected override void Dispose(bool disposing){if(disposing){ageTimer.Dispose();tips.Dispose();ClearMaterial();}base.Dispose(disposing);}
  }

  public sealed class WeatherDetailForm:Form {
    public readonly WeatherDetailView View=new WeatherDetailView();
    readonly BufferedFlowPanel scroll=new BufferedFlowPanel{Dock=DockStyle.Fill,AutoScroll=true};
    readonly OpenMeteoWeatherProvider network;
    IWeatherProvider homeProvider;
    WeatherPlace selected,homePlace;
    WeatherSnapshot displayed;
    WeatherCityHistory history;
    string homeName,historyNotice="";
    readonly HoverState dismissHover = new HoverState(200);
    readonly RevealMotion dismissMotion = new RevealMotion();
    readonly AnimationTimerPrecision animationPrecision = new AnimationTimerPrecision();
    readonly System.Windows.Forms.Timer dismissTimer = new System.Windows.Forms.Timer { Interval = 50 };
    long keyboardActiveUntil;
    bool explicitClose;
    int sequence;
    CancellationTokenSource operation;
    public event Action<WeatherPlace,WeatherSnapshot> HomeChanged;
    public event Action<string> HomeTextChanged;
    public event Action<IList<WeatherCityChoice>> HistoryChanged;
    internal bool AutoDismissed { get; private set; }
    internal event Action<bool> AutoDismissChanged;
    public bool ReduceMotion { get; set; }
    public WeatherDetailForm(string city,IWeatherProvider provider,WeatherSnapshot initial):this(city,provider,initial,null){}
    public WeatherDetailForm(string city,IWeatherProvider provider,WeatherSnapshot initial,IEnumerable<WeatherCityChoice> recent){
      homeProvider=provider;network=provider as OpenMeteoWeatherProvider;homeName=city;selected=homePlace=network==null?null:network.Location;
      displayed=initial??new WeatherSnapshot{City=city};history=new WeatherCityHistory(recent);
      Text="天气";FormBorderStyle=FormBorderStyle.None;AutoScaleMode=AutoScaleMode.None;ShowInTaskbar=false;KeyPreview=true;BackColor=Palette.Base;
      ClientSize=new Size(CornerLayout.Width,WeatherDetailView.ContentHeight);StartPosition=FormStartPosition.Manual;
      scroll.BackColor=Palette.Base;Controls.Add(scroll);scroll.Controls.Add(View);scroll.SizeChanged+=(s,e)=>ResizeView();View.ContentSizeChanged+=(s,e)=>ResizeForContent();
      View.RefreshAvailable=network!=null;View.CanSetHome=selected!=null;View.CurrentIsHome=true;View.HomeName=homeName;View.SetRecentCities(history.Items);
      View.Present(displayed,"");View.UpdateState(false,network!=null&&network.CitySearchEnabled,"");
      View.CloseRequested+=(s,e)=>RequestClose();View.DragRequested+=(s,e)=>{ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);};
      KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;e.SuppressKeyPress=true;if(View.CityDrawerVisible)View.ShowCitySearch(false);else RequestClose();}};
      View.LocalCityChosen+=name=>{
        try{
          var callback=HomeTextChanged;if(callback!=null)callback(name);homeName=name;homePlace=selected=null;homeProvider=new DisconnectedWeatherProvider();
          Remember(WeatherCityChoice.FromName(name));displayed=new WeatherSnapshot{City=name,State="not-connected"};View.HomeName=name;View.CanSetHome=false;View.CurrentIsHome=true;
          View.Present(displayed,"城市已保存");View.UpdateState(false,network!=null&&network.CitySearchEnabled,View.Notice);
        }catch{View.UpdateState(false,network!=null&&network.CitySearchEnabled,"城市保存失败，请重试");}
      };
      View.SearchRequested+=async query=>{
        if(network==null||!network.CitySearchEnabled)return;
        int ticket=Begin();View.UpdateState(true,true,"正在搜索城市…");
        try{var places=await network.SearchAsync(query,operation.Token);if(IsCurrent(ticket)){View.UpdateState(false,true,"");View.ShowPlaces(places);}}
        catch{if(IsCurrent(ticket))View.UpdateState(false,true,"搜索失败，可补充省份或国家后重试");}
      };
      View.PlaceChosen+=async place=>{if(network!=null)await LoadCity(network.ForPlace(place),place.Name,place);};
      View.RecentCityChosen+=async choice=>{
        var place=choice.ToPlace();
        if(place!=null&&network!=null)await LoadCity(network.ForPlace(place),place.Name,place);
        else{View.ShowCitySearch(true);View.SetQuery(choice.Name);View.UpdateState(false,network!=null&&network.CitySearchEnabled,"请搜索该城市以获取天气");}
      };
      View.HomeRequested+=async (s,e)=>await LoadCity(homeProvider,homeName,homePlace);
      View.RefreshRequested+=async (s,e)=>{var source=selected!=null&&network!=null?network.ForPlace(selected):homeProvider;await LoadCity(source,selected==null?homeName:selected.Name,selected,true);};
      View.SetHomeRequested+=(s,e)=>{
        if(selected==null||displayed==null)return;
        try{var callback=HomeChanged;if(callback!=null)callback(selected,displayed);if(network!=null)homeProvider=network.ForPlace(selected);homeName=selected.Name;homePlace=selected;View.HomeName=homeName;View.CurrentIsHome=true;View.UpdateState(false,network!=null&&network.CitySearchEnabled,"已设为常驻城市");}
        catch{View.UpdateState(false,network!=null&&network.CitySearchEnabled,"常驻城市保存失败，请重试");}
      };
      Load+=(s,e)=>{
        var area=Screen.FromControl(Owner??this).WorkingArea;ClientSize=new Size(Math.Min(CornerLayout.Width,Math.Max(280,area.Width-32)),Math.Min(View.DesiredHeight,Math.Max(320,area.Height-32)));
        int right=Owner==null?area.Left+(area.Width+Width)/2:Owner.Left+Math.Min(CornerLayout.Width,Owner.ClientSize.Width);
        int top=Owner==null?area.Top+(area.Height-Height)/2:Owner.Top;
        Location=new Point(Math.Max(area.Left+16,Math.Min(right-Width,area.Right-Width-16)),Math.Max(area.Top+16,Math.Min(top,area.Bottom-Height-16)));ResizeView();
        Opacity = ReduceMotion || SystemInformation.HighContrast || !SystemInformation.UIEffectsEnabled ? 1 : 0;
      };
      Shown+=async (s,e)=>await LoadCity(homeProvider,homeName,selected);
      dismissTimer.Tick += MaintainHover;
      FormClosing+=(s,e)=>{dismissTimer.Stop();animationPrecision.SetActive(false);sequence++;if(operation!=null)operation.Cancel();};ResizeView();
    }
    protected override void OnShown(EventArgs e) {
      dismissHover.Open = true;
      dismissHover.OutsideSince = -1;
      dismissMotion.Target(true, PreviewClock.NowMs, ReduceMotion || SystemInformation.HighContrast || !SystemInformation.UIEffectsEnabled);
      animationPrecision.SetActive(dismissMotion.Moving);
      dismissTimer.Interval = dismissMotion.Moving ? 15 : 50;
      dismissTimer.Start();
      base.OnShown(e);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
      keyboardActiveUntil = PreviewClock.NowMs + 250;
      base.OnKeyDown(e);
    }
    void MaintainHover(object sender, EventArgs e) {
      if (!Visible || IsDisposed) return;
      long now = PreviewClock.NowMs;
      bool interacting = MouseButtons != MouseButtons.None || View.DismissalProtected || now < keyboardActiveUntil;
      bool inside = Bounds.Contains(Cursor.Position) || interacting;
      if (!explicitClose && AutoDismissed && inside) {
        SetAutoDismissed(false, now);
      } else if (!explicitClose && !AutoDismissed) {
        dismissHover.Tick(now, false, inside, false);
        if (!dismissHover.Open) SetAutoDismissed(true, now);
      }
      dismissMotion.Advance(now);
      Opacity = dismissMotion.Amount;
      animationPrecision.SetActive(dismissMotion.Moving);
      dismissTimer.Interval = dismissMotion.Moving ? 15 : 50;
      if ((explicitClose || AutoDismissed) && !dismissMotion.Moving) Close();
    }
    void RequestClose() {
      if (explicitClose) return;
      explicitClose = true;
      if (AutoDismissed) {
        AutoDismissed = false;
        var changed = AutoDismissChanged;
        if (changed != null) changed(false);
      }
      dismissMotion.Target(false, PreviewClock.NowMs, ReduceMotion || SystemInformation.HighContrast || !SystemInformation.UIEffectsEnabled);
      animationPrecision.SetActive(dismissMotion.Moving);
      if (!dismissMotion.Moving) { Close(); return; }
      dismissTimer.Interval = 15;
      dismissTimer.Start();
    }
    void SetAutoDismissed(bool value, long now) {
      AutoDismissed = value;
      if (!value) {
        dismissHover.Open = true;
        dismissHover.OutsideSince = -1;
      }
      dismissMotion.Target(!value, now, ReduceMotion || SystemInformation.HighContrast || !SystemInformation.UIEffectsEnabled);
      var changed = AutoDismissChanged;
      if (changed != null) changed(value);
    }
    void ResizeForContent(){if(!IsHandleCreated){ResizeView();return;}var area=Screen.FromControl(Owner??this).WorkingArea;int height=Math.Min(View.DesiredHeight,Math.Max(320,area.Height-32));if(ClientSize.Height!=height)ClientSize=new Size(ClientSize.Width,height);Top=Math.Max(area.Top+16,Math.Min(Top,area.Bottom-Height-16));ResizeView();}
    void ResizeView(){
      int height=View.DesiredHeight;
      bool overflow=height>scroll.Height;
      int position=Math.Max(0,-scroll.AutoScrollPosition.Y);
      scroll.SuspendLayout();
      try{
        // Clear the previous scroll extent before shrinking the city drawer.
        scroll.AutoScroll=false;scroll.AutoScrollMinSize=Size.Empty;
        int width=Math.Max(1,scroll.Width-(overflow?SystemInformation.VerticalScrollBarWidth:0));
        View.SetBounds(0,0,width,height);
        scroll.AutoScrollMinSize=new Size(0,height);scroll.AutoScroll=true;
        if(overflow)scroll.AutoScrollPosition=new Point(0,Math.Min(position,height-scroll.Height));
      }finally{scroll.ResumeLayout(true);}
    }
    void Remember(WeatherCityChoice city){
      var before=history.Items;if(!history.Touch(city))return;
      try{var callback=HistoryChanged;if(callback!=null)callback(history.Items);historyNotice="";View.SetRecentCities(history.Items);}
      catch{history=new WeatherCityHistory(before);View.SetRecentCities(before);historyNotice="最近城市未能保存，请稍后重试";View.UpdateState(false,network!=null&&network.CitySearchEnabled,historyNotice);}
    }
    int Begin(){sequence++;if(operation!=null){operation.Cancel();operation.Dispose();}operation=new CancellationTokenSource();return sequence;}
    bool IsCurrent(int ticket){return !IsDisposed&&ticket==sequence;}
    async Task LoadCity(IWeatherProvider provider,string city,WeatherPlace place,bool force=false){
      int ticket=Begin();Remember(place==null?WeatherCityChoice.FromName(city):WeatherCityChoice.FromPlace(place));
      View.UpdateState(true,network!=null&&network.CitySearchEnabled,"正在获取 "+city+" 的天气…");
      try{
        var live=provider as OpenMeteoWeatherProvider;var data=force&&live!=null?await live.ReadFreshAsync(city,operation.Token):await provider.ReadAsync(city,operation.Token);
        if(!IsCurrent(ticket))return;
        selected=place;displayed=data;View.CanSetHome=selected!=null;View.CurrentIsHome=SamePlace(selected,homePlace)&&city==homeName;
        string notice=data.RefreshFailed?"刷新失败，显示缓存天气":data.EffectiveState(DateTimeOffset.UtcNow)=="fresh"?"":WeatherCardControl.Status(data,DateTimeOffset.UtcNow);
        View.Present(data,String.IsNullOrEmpty(historyNotice)?notice:historyNotice);
      }catch{if(IsCurrent(ticket))View.Present(new WeatherSnapshot{City=city,State="unavailable"},"天气获取失败，请稍后重试");}
      finally{if(IsCurrent(ticket))View.UpdateState(false,network!=null&&network.CitySearchEnabled,View.Notice);}
    }
    static bool SamePlace(WeatherPlace first,WeatherPlace second){return first==null&&second==null||first!=null&&second!=null&&Math.Abs(first.Latitude-second.Latitude)<.000001&&Math.Abs(first.Longitude-second.Longitude)<.000001;}
    [DllImport("user32.dll")]static extern bool ReleaseCapture();
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);int border=unchecked((int)0xfffffffe),corner=1;try{DwmSetWindowAttribute(Handle,34,ref border,4);DwmSetWindowAttribute(Handle,33,ref corner,4);}catch{}}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<2||Height<2)return;using(var shape=GlassSurface.Path(new RectangleF(0,0,Width,Height),24)){var previous=Region;Region=new Region(shape);if(previous!=null)previous.Dispose();}}
    protected override void Dispose(bool disposing){
      if(disposing){
        dismissTimer.Dispose();
        animationPrecision.Dispose();
        if(operation!=null){operation.Cancel();operation.Dispose();operation=null;}
      }
      base.Dispose(disposing);
    }
  }
}
