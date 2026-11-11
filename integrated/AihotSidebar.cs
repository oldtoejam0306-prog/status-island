using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace IslandIntegrated {
  public sealed class AihotItem {
    public int Rank;
    public string Id, Title, SourceName, AihotUrl, OriginalUrl, StoryUrl;
    public int SourceCount, SignalCount;
    public string[] SourceNames;
    public DateTime LatestAt;
  }
  public sealed class AihotSnapshot {
    public int SchemaVersion;
    public int Count;
    public List<AihotItem> Items = new List<AihotItem>();
  }
  internal sealed class AihotSavedState {
    public bool Paused;
    public List<string> Seen = new List<string>();
    public List<string> Pending = new List<string>();
    public DateTime LastPopupUtc;
  }
  public sealed class AihotMonitor : IDisposable {
    static readonly Uri Endpoint = new Uri("https://aihot.news/api/v1/hot-topics");
    readonly string statePath;
    readonly object gate = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    AihotSavedState state;
    CancellationTokenSource cancel;
    bool busy, disposed, enabled, freshData;
    string etag;
    DateTime nextRequestUtc, retryNotBeforeUtc;
    AihotSnapshot snapshot;
    string message = "正在连接 AIHOT";
    DateTime checkedUtc;
    public event EventHandler Changed;
    public AihotSnapshot Snapshot {
      get {
        lock(gate) return snapshot;
      }
    }
    public bool IsRefreshing { get { lock(gate) return busy; } }
    public string Status {
      get {
        lock(gate) return message;
      }
    }
    public DateTime LastCheckedUtc {
      get {
        lock(gate) return checkedUtc;
      }
    }
    public bool Paused {
      get {
        lock(gate) return state.Paused;
      }
    }
    public AihotMonitor(string stateDirectory) {
      Directory.CreateDirectory(stateDirectory);
      statePath=Path.Combine(stateDirectory,"aihot-monitor.json");
      state=LoadState(statePath);
    }
    public static AihotMonitor CreateRenderFixture(string stateDirectory) {
      var monitor=new AihotMonitor(stateDirectory);
      monitor.snapshot=new AihotSnapshot {
        SchemaVersion=1, Count=10
      }
      ;
      for(int rank=1;rank<=10;rank++) monitor.snapshot.Items.Add(new AihotItem {
        Rank=rank, Id="fixture-"+rank, Title="[FIXTURE] 热点示例 "+rank, SourceName="[FIXTURE] 来源", AihotUrl="https://aihot.news/fixture/"+rank, LatestAt=DateTime.UtcNow
      }
      );
      monitor.checkedUtc=DateTime.UtcNow;
      monitor.freshData=true;
      monitor.message="[FIXTURE] 已更新 12:00";
      return monitor;
    }
    CancellationToken BeginPoll() {
      busy=true;
      message="正在检查 AIHOT…";
      cancel=new CancellationTokenSource();
      return cancel.Token;
    }
    public void Tick(bool isEnabled, bool allowPopup) {
      CancellationToken? request=null;
      lock(gate) {
        enabled=isEnabled;
        if(disposed)return;
        if(!enabled) {
          AihotPopupForm.CloseAll();
          return;
        }
        if(CanStartPoll(enabled,busy,DateTime.UtcNow,nextRequestUtc,retryNotBeforeUtc))request=BeginPoll();
      }
      if(request.HasValue) {
        RaiseChanged();
        CancellationToken token=request.Value;
        Task.Run(()=>Poll(token));
      }
      if(allowPopup)TryNotify();
      else AihotPopupForm.CloseAll();
    }
    public void Refresh() {
      CancellationToken? request=null;
      lock(gate) {
        if(disposed)return;
        if(!enabled)message="AIHOT 卡片尚未启用";
        else if(busy)message="正在检查 AIHOT…";
        else if(DateTime.UtcNow<retryNotBeforeUtc)
          message="请求冷却中 · "+retryNotBeforeUtc.ToLocalTime().ToString("HH:mm:ss")+" 后重试";
        else request=BeginPoll();
      }
      RaiseChanged();
      if(request.HasValue) {
        CancellationToken token=request.Value;
        Task.Run(()=>Poll(token));
      }
    }
    public void SetPaused(bool value) {
      lock(gate) {
        bool previous = state.Paused;
        state.Paused = value;
        if (!SaveState()) {
          state.Paused = previous;
          message = "无法保存提醒设置";
        }
      }
      if(value)AihotPopupForm.CloseAll();
      RaiseChanged();
    }
    async Task Poll(CancellationToken token) {
      CancellationToken monitorToken = token;
      CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
      try {
        requestTimeout.CancelAfter(20000);
        token = requestTimeout.Token;
        HttpWebRequest req=(HttpWebRequest)WebRequest.Create(Endpoint);
        req.Method="GET";
        req.UserAgent="aihot-api/2.0.0";
        req.Timeout=20000;
        req.ReadWriteTimeout=20000;
        req.AllowAutoRedirect=false;
        try {
          string actor=Environment.GetEnvironmentVariable("AIHOT_ACTOR_ID");if(String.IsNullOrWhiteSpace(actor))throw new InvalidOperationException("AIHOT actor is not configured");actor=actor.Trim();
          Guid actorId;
          if (Guid.TryParseExact(actor,"D",out actorId) && actor.Length>14 && actor[14]=='4') req.UserAgent="aihot-api/2.0.0 aihot-actor/"+actor;
        }
        catch {
        }
        if(!String.IsNullOrEmpty(etag)) req.Headers[HttpRequestHeader.IfNoneMatch]=etag;
        using(token.Register(()=> {
          try {
            req.Abort();
          }
          catch {
          }
        }
        )) {
          HttpWebResponse response=null;
          try {
            response=(HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false);
          }
          catch(WebException ex) {
            response=ex.Response as HttpWebResponse;
            if(response==null) throw;
          }
          using(response) {
            if(response.StatusCode==HttpStatusCode.NotModified) {
              lock(gate) {
                if(disposed||!enabled)return;
                checkedUtc=DateTime.UtcNow;
                freshData=true;
                nextRequestUtc=DateTime.UtcNow.AddMinutes(5);
                message="检查完成 · 内容无变化 · "+checkedUtc.ToLocalTime().ToString("HH:mm:ss");
              }
              return;
            }
            if((int)response.StatusCode==429 || (int)response.StatusCode==503) {
              lock(gate) {
                freshData=false;
                retryNotBeforeUtc=RetryAt(response);
                message="服务繁忙 · 稍后重试";
              }
              return;
            }
            if(response.StatusCode!=HttpStatusCode.OK) throw new InvalidDataException("HTTP "+(int)response.StatusCode);
            string body;
            using(var ms=new MemoryStream()) {
              byte[] buf=new byte[8192];
              int n,total=0;
              Stream stream=response.GetResponseStream();
              while((n=await stream.ReadAsync(buf,0,buf.Length,token).ConfigureAwait(false))>0) {
                total+=n;
                if(total>262144)throw new InvalidDataException("response too large");
                ms.Write(buf,0,n);
              }
              body=Encoding.UTF8.GetString(ms.ToArray());
            }
            AihotSnapshot fresh=Parse(body,DateTime.UtcNow);
            if(response.Headers[HttpResponseHeader.ETag]!=null) etag=response.Headers[HttpResponseHeader.ETag];
            lock(gate) {
              if(disposed||!enabled)return;
              bool seed=snapshot==null && state.Seen.Count==0;
              snapshot=fresh;
              checkedUtc=DateTime.UtcNow;
              freshData=true;
              message="AIHOT 热点 · 已检查 "+checkedUtc.ToLocalTime().ToString("HH:mm:ss");
              ObserveTopThree(fresh,state.Seen,state.Pending,seed);
              nextRequestUtc=DateTime.UtcNow.AddMinutes(5);
              if(!SaveState()) {
                freshData=false;
                message="无法保存提醒状态 · 已暂停提醒";
              }
            }
          }
        }
      }
      catch(OperationCanceledException) {
        lock(gate) {
          if (!disposed && !monitorToken.IsCancellationRequested) {
            freshData=false;
            message="AIHOT 请求超时 · 检查网络后重试";
            retryNotBeforeUtc=DateTime.UtcNow.AddSeconds(20);
          }
        }
      }
      catch(Exception) {
        lock(gate) {
          freshData=false;
          message="AIHOT 暂不可用 · 检查网络后重试";
          retryNotBeforeUtc=DateTime.UtcNow.AddSeconds(20);
        }
      }
      finally {
        requestTimeout.Dispose();
        lock(gate) {
          busy=false;
          if(cancel!=null)cancel.Dispose();
          cancel=null;
        }
        RaiseChanged();
      }
    }
    DateTime RetryAt(HttpWebResponse r) {
      return RetryDeadline(r.Headers["Retry-After"],DateTime.UtcNow);
    }
    public static DateTime RetryDeadline(string value,DateTime nowUtc) {
      DateTime date;
      if (DateTime.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out date) && date>nowUtc) return date;
      int seconds;
      if (Int32.TryParse(value,out seconds) && seconds>=0) return nowUtc.AddSeconds(seconds);
      return nowUtc.AddMinutes(5);
    }
    static IEnumerable<string> TopThreeIds(AihotSnapshot value) {
      return value.Items.Where(x=>x.Rank<=3).OrderBy(x=>x.Rank).Select(x=>x.Id);
    }
    public static List<string> NewTopThreeIds(AihotSnapshot value,ICollection<string> seen) {
      return TopThreeIds(value).Where(id=>!seen.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
    }
    public static void ObserveTopThree(AihotSnapshot value,List<string> seen,List<string> pending,bool seed) {
      if(seed) {
        foreach(string id in TopThreeIds(value)) AddBounded(seen,id,300);
        return;
      }
      foreach(string id in NewTopThreeIds(value,seen)) AddBounded(pending,id,100);
    }
    public static bool CanAlert(bool enabled,bool paused,bool allowPopup,bool haveSnapshot,bool freshData,DateTime checkedUtc,DateTime lastPopupUtc,DateTime nowUtc) {
      return enabled && !paused && allowPopup && haveSnapshot && freshData && nowUtc>=checkedUtc && nowUtc-checkedUtc<=TimeSpan.FromMinutes(15) && nowUtc-lastPopupUtc>=TimeSpan.FromMinutes(15);
    }
    public static bool CanStartPoll(bool enabled,bool busy,DateTime nowUtc,DateTime nextRequestUtc,DateTime retryNotBeforeUtc) {
      return enabled && !busy && nowUtc>=nextRequestUtc && nowUtc>=retryNotBeforeUtc;
    }
    void TryNotify() {
      AihotItem item=null;
      int oldIndex=-1;
      DateTime oldPopup=DateTime.MinValue;
      lock(gate) {
        if(disposed||!CanAlert(enabled,state.Paused,true,snapshot!=null,freshData,checkedUtc,state.LastPopupUtc,DateTime.UtcNow))return;
        item=snapshot.Items.FirstOrDefault(x=>x.Rank<=3 && state.Pending.Contains(x.Id) && x.LatestAt>=DateTime.UtcNow.AddHours(-24) && x.LatestAt<=DateTime.UtcNow.AddMinutes(5));
        if(item==null)return;
        oldIndex=state.Pending.IndexOf(item.Id);
        oldPopup=state.LastPopupUtc;
        state.Pending.Remove(item.Id);
        state.LastPopupUtc=DateTime.UtcNow;
        AddBounded(state.Seen,item.Id,300);
        if(!SaveState()) {
          state.Pending.Insert(Math.Max(0,oldIndex),item.Id);
          state.LastPopupUtc=oldPopup;
          state.Seen.Remove(item.Id);
          return;
        }
      }
      try {
        var f=new AihotPopupForm(item);
        f.ShowLayered();
      }
      catch {
        lock(gate) {
          state.Seen.Remove(item.Id);
          AddBounded(state.Pending,item.Id,100);
          state.LastPopupUtc=oldPopup;
          SaveState();
        }
      }
    }
    static void AddBounded(List<string> list,string value,int max) {
      if(!list.Contains(value)) list.Add(value);
      while(list.Count>max)list.RemoveAt(0);
    }
    bool SaveState() {
      try {
        string tmp=statePath+".tmp";
        File.WriteAllText(tmp,json.Serialize(state),new UTF8Encoding(false));
        if(File.Exists(statePath))File.Replace(tmp,statePath,null);
        else File.Move(tmp,statePath);
        return true;
      }
      catch {
        return false;
      }
    }
    static AihotSavedState LoadState(string path) {
      try {
        var s=new JavaScriptSerializer().Deserialize<AihotSavedState>(File.ReadAllText(path));
        if(s!=null) {
          if(s.Seen==null)s.Seen=new List<string>();
          if(s.Pending==null)s.Pending=new List<string>();
          return s;
        }
      }
      catch {
      }
      return new AihotSavedState();
    }
    static string Get(object o,string key) {
      var d=o as Dictionary<string,object>;
      return d!=null&&d.ContainsKey(key)?Convert.ToString(d[key]):null;
    }
    static int Int(object o,string k) {
      int n;
      return Int32.TryParse(Get(o,k),out n)?n:0;
    }
    static object Child(object o,string k) {
      var d=o as Dictionary<string,object>;
      object v;
      return d!=null&&d.TryGetValue(k,out v)?v:null;
    }
    static string SafeUrl(object o) {
      Uri u;
      string s=o as string;
      return Uri.TryCreate(s,UriKind.Absolute,out u)&&u.Scheme==Uri.UriSchemeHttps&&String.Equals(u.Host,"aihot.news",StringComparison.OrdinalIgnoreCase)&&u.UserInfo==""&&u.Port==443?u.ToString():null;
    }
    static string SafeExternalUrl(object o) {
      Uri u;
      string s=o as string;
      return Uri.TryCreate(s,UriKind.Absolute,out u)&&u.Scheme==Uri.UriSchemeHttps&&u.UserInfo==""&&u.Port==443?u.ToString():null;
    }
    public static AihotSnapshot Parse(string raw,DateTime nowUtc) {
      var ser=new JavaScriptSerializer();
      var root=ser.DeserializeObject(raw) as Dictionary<string,object>;
      if(root==null||Int(root,"schemaVersion")!=1)throw new InvalidDataException("schema");
      var arr=Child(root,"items") as object[];
      if(arr==null||arr.Length==0||arr.Length>10)throw new InvalidDataException("items");
      var result=new AihotSnapshot {
        SchemaVersion=1,Count=Int(root,"count")
      }
      ;
      var seen=new HashSet<string>(StringComparer.Ordinal);
      foreach(object obj in arr) {
        string id=Get(obj,"id"),title=Get(obj,"title");
        int rank=Int(obj,"rank");
        DateTime time;
        string ts=Get(obj,"latestAt");
        if(String.IsNullOrWhiteSpace(id)||id.Length>200||!seen.Add(id)||String.IsNullOrWhiteSpace(title)||title.Length>600||rank<1||rank>10||!DateTime.TryParse(ts,null,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out time)||time<nowUtc.AddHours(-24)||time>nowUtc.AddMinutes(5))continue;
        object links=Child(obj,"links"),source=Child(obj,"source");
        var names=Child(obj,"sourceNames") as object[];
        result.Items.Add(new AihotItem {
          Id=id,Title=title,Rank=rank,SourceName=Get(source,"name")??"来源未注明",AihotUrl=SafeUrl(Child(links,"aihot")),OriginalUrl=SafeExternalUrl(Child(links,"original")),StoryUrl=SafeExternalUrl(Child(links,"story")),SourceCount=Int(obj,"sourceCount"),SignalCount=Int(obj,"signalCount"),SourceNames=names==null?new string[0]:names.Take(12).Select(x=>Convert.ToString(x)).ToArray(),LatestAt=time
        }
        );
      }
      if(result.Items.Count==0)throw new InvalidDataException("stale or invalid");
      result.Items=result.Items.OrderBy(x=>x.Rank).ToList();
      return result;
    }
    void RaiseChanged() {
      var h=Changed;
      if(h!=null)try {
        h(this,EventArgs.Empty);
      }
      catch {
      }
    }
    public void Dispose() {
      lock(gate) {
        disposed=true;
        if(cancel!=null)cancel.Cancel();
      }
      AihotPopupForm.CloseAll();
    }
  }
  internal static class AihotNavigation {
    internal const string HomeUrl="https://aihot.news/";
    internal static bool IsSafeArticleUrl(string url) {
      Uri uri;
      return Uri.TryCreate(url,UriKind.Absolute,out uri) && uri.Scheme==Uri.UriSchemeHttps &&
        String.Equals(uri.Host,"aihot.news",StringComparison.OrdinalIgnoreCase) &&
        String.IsNullOrEmpty(uri.UserInfo) && uri.Port==443;
    }
    internal static bool OpenHomepage(Action<string> opener) {
      return Dispatch(HomeUrl,opener);
    }
    internal static bool OpenItem(string url,Action<string> opener) {
      return Dispatch(IsSafeArticleUrl(url)?new Uri(url).ToString():HomeUrl,opener);
    }
    static bool Dispatch(string url,Action<string> opener) {
      try {
        (opener??OpenBrowser)(url);
        return true;
      } catch {
        return false;
      }
    }
    static void OpenBrowser(string url) {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute=true });
    }
  }
  sealed class AihotHitLabel : Label {
    public AihotHitLabel() { SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint,true); }
    protected override void OnPaint(PaintEventArgs e) { }
  }
  sealed class AihotHitButton : Button {
    public AihotHitButton() {
      SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
    }
    protected override void OnPaint(PaintEventArgs e) { }
  }
  public sealed class AihotSidebarControl : Control {
    internal readonly AihotMonitor monitor;
    readonly Label title,summary,status;
    readonly Button refresh,pause,inspect;
    readonly Action<string> openUrl;
    internal AihotItem displayedItem;
    AihotCardOverlay overlay;
    bool overlayMode,disposed;
    internal bool RefreshHovered,RefreshPressed;
    string visualFeedback="";
    public string DisplayedStatus {get{return String.IsNullOrEmpty(visualFeedback)?monitor.Status:visualFeedback;}}
    public AihotSidebarControl(AihotMonitor m) : this(m,null) { }
    internal AihotSidebarControl(AihotMonitor m,Action<string> opener) {
      openUrl=opener;monitor=m;Size=new Size(AihotLayout.CardWidth,AihotLayout.CardHeight);SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;ForeColor=Palette.Text;Font=new Font("Microsoft YaHei UI",9f);DoubleBuffered=true;
      title=LabelAt("AIHOT助手",24,9,160,24,14f,true);summary=LabelAt("正在载入热点",20,42,320,48,12f,false);status=LabelAt("正在连接 AIHOT",20,104,320,20,9f,false);
      refresh=ButtonAt("立即检查",20,128,94);pause=ButtonAt(m.Paused?"恢复提醒":"暂停提醒",118,128,112);inspect=ButtonAt("打开 AIHOT",232,8,104);
      refresh.Click+=(s,e)=>RefreshNow();pause.Click+=(s,e)=>TogglePause();inspect.Click+=(s,e)=>OpenHomepage();title.Click+=(s,e)=>OpenHomepage();summary.Click+=(s,e)=>OpenDisplayedItem();title.Cursor=Cursors.Hand;summary.Cursor=Cursors.Hand;
      refresh.MouseEnter+=(s,e)=>SetRefreshVisualState(true,false);
      refresh.MouseLeave+=(s,e)=>SetRefreshVisualState(false,false);
      refresh.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)SetRefreshVisualState(true,true);};
      refresh.MouseUp+=(s,e)=>SetRefreshVisualState(refresh.ClientRectangle.Contains(e.Location),false);
      refresh.MouseCaptureChanged+=(s,e)=>{if(!refresh.Capture&&RefreshPressed)SetRefreshVisualState(RefreshHovered,false);};
      monitor.Changed+=OnChanged;ParentChanged+=(s,e)=>SyncOverlay();LocationChanged+=(s,e)=>SyncOverlay();SizeChanged+=(s,e)=>{LayoutHitControls();SyncOverlay();};VisibleChanged+=(s,e)=>SyncOverlay();EnabledChanged+=(s,e)=>SyncOverlay();Disposed+=(s,e)=>{disposed=true;StopOverlay();};RefreshView();
    }
    Label LabelAt(string text,int x,int y,int w,int h,float size,bool bold){var l=new AihotHitLabel{Text=text,AccessibleName=text,Left=x,Top=y,Width=w,Height=h,ForeColor=Palette.Text,BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular),AutoEllipsis=true};Controls.Add(l);return l;}
    Button ButtonAt(string text,int x,int y,int w){var b=new AihotHitButton{Text=text,AccessibleName=text,Left=x,Top=y,Width=w,Height=32,FlatStyle=FlatStyle.Flat,ForeColor=Palette.Text,BackColor=Color.Transparent,Font=new Font("Microsoft YaHei UI",9f)};b.FlatAppearance.BorderSize=0;Controls.Add(b);return b;}
    void OpenHomepage(){visualFeedback=AihotNavigation.OpenHomepage(openUrl)?"":"无法打开默认浏览器，请检查 Windows 浏览器设置后重试。";RefreshPresentation();}
    void OpenDisplayedItem(){visualFeedback=AihotNavigation.OpenItem(displayedItem==null?null:displayedItem.AihotUrl,openUrl)?"":"无法打开默认浏览器，请检查 Windows 浏览器设置后重试。";RefreshPresentation();}
    void RefreshNow(){visualFeedback="";monitor.Refresh();RefreshPresentation();}
    internal void SetRefreshVisualState(bool hovered,bool pressed){
      pressed=hovered&&pressed&&!monitor.IsRefreshing;
      if(RefreshHovered==hovered&&RefreshPressed==pressed)return;
      RefreshHovered=hovered;RefreshPressed=pressed;RefreshPresentation();
    }
    void TogglePause(){visualFeedback="";monitor.SetPaused(!monitor.Paused);RefreshPresentation();}
    void RefreshPresentation(){
      bool checking=monitor.IsRefreshing;
      refresh.Text=checking?"检查中…":"立即检查";
      refresh.AccessibleName=refresh.Text;
      refresh.Enabled=!checking;
      if(overlay!=null)overlay.MarkDirty();fallbackKey="";Invalidate();if(status!=null){status.Text=DisplayedStatus;status.AccessibleName=DisplayedStatus;}if(pause!=null){pause.Text=monitor.Paused?"恢复提醒":"暂停提醒";pause.AccessibleName=pause.Text;}if(summary!=null)summary.AccessibleName=summary.Text;}
    internal void HandleFrameClick(Point p){if(AihotLayout.RefreshHit(Width).Contains(p)){RefreshNow();return;}if(AihotLayout.PauseHit(Width).Contains(p)){TogglePause();return;}if(AihotLayout.OpenHit(Width).Contains(p)){OpenHomepage();return;}if(AihotLayout.TitleHit(Width).Contains(p)){OpenHomepage();return;}if(AihotLayout.StoryHit(Width).Contains(p))OpenDisplayedItem();}
    static bool IsLayeredHost(NoActivateForm host){return host!=null&&host.TopLevel&&!host.PerPixelLayered&&host.TransparencyKey.ToArgb()==Color.Fuchsia.ToArgb();}
    void SyncOverlay(){if(disposed)return;var host=FindForm() as NoActivateForm;bool use=IsLayeredHost(host)&&Visible&&Enabled&&Parent!=null;if(!use){bool wasOverlay=overlayMode;overlayMode=false;StopOverlay();ShowFallback(true);if(wasOverlay)Invalidate();return;}overlayMode=true;ShowFallback(false);if(overlay==null){overlay=new AihotCardOverlay(this);overlay.SetOwner(host);overlay.Synchronize();return;}overlay.SetOwner(host);overlay.Synchronize();}
    void LayoutHitControls(){
      if(title==null)return;
      title.Bounds=BodyHit(AihotLayout.TitleHit(Width));
      summary.Bounds=BodyHit(AihotLayout.StoryHit(Width));
      inspect.Bounds=BodyHit(AihotLayout.OpenHit(Width));
      refresh.Bounds=BodyHit(AihotLayout.RefreshHit(Width));
      pause.Bounds=BodyHit(AihotLayout.PauseHit(Width));
      // Status text is rendered by the card and receives no independent action.
      status.Visible=false;
    }
    static Rectangle BodyHit(Rectangle value){value.Offset(-AihotLayout.Margin,-AihotLayout.Margin);return value;}
    void ShowFallback(bool value){foreach(Control child in Controls)child.Visible=value;LayoutHitControls();}
    void StopOverlay(){if(overlay!=null){var old=overlay;overlay=null;try{old.Close();}catch{}old.Dispose();}}
    public string PresentationError {get;private set;}
    Bitmap fallbackFrame;string fallbackKey="";
    internal void LayeredPushFailed(int error){PresentationError="layered update failed ("+error+")";overlayMode=false;StopOverlay();ShowFallback(true);Invalidate();}
    internal void LayeredPushSucceeded(){PresentationError=null;}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(overlayMode?Color.Fuchsia:Palette.Base);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(overlayMode)return;bool contentOnly=GlassComposition.Active&&Parent is WidgetPanel;AihotItem item=displayedItem;string state=DisplayedStatus;bool paused=monitor.Paused;string key=contentOnly+"|"+Width+"|"+Height+"|"+(item==null?"":(item.Id??"")+"|"+(item.Title??"")+"|"+(item.SourceName??""))+"|"+state+"|"+paused+"|"+monitor.IsRefreshing+"|"+RefreshHovered+"|"+RefreshPressed+"|"+Palette.Theme+"|"+Palette.Solid+"|"+Palette.Legacy+"|"+SystemInformation.HighContrast;if(fallbackFrame==null||key!=fallbackKey){if(fallbackFrame!=null)fallbackFrame.Dispose();fallbackFrame=contentOnly?AihotRenderer.RenderCardContent(Width,Height,item,state,paused,monitor.IsRefreshing,RefreshHovered,RefreshPressed):AihotRenderer.RenderCard(Width,Height,item,state,paused,monitor.IsRefreshing,RefreshHovered,RefreshPressed);fallbackKey=key;}if(contentOnly)e.Graphics.DrawImageUnscaled(fallbackFrame,-AihotLayout.Margin,-AihotLayout.Margin);else GlassComposition.DrawImageReplacing(e.Graphics,fallbackFrame,-AihotLayout.Margin,-AihotLayout.Margin);}
    void OnChanged(object s,EventArgs e){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((MethodInvoker)RefreshView);return;}RefreshView();}
    void RefreshView(){var snap=monitor.Snapshot;displayedItem=snap==null||snap.Items.Count==0?null:snap.Items[0];title.Text="AIHOT助手";summary.Text=displayedItem==null?"暂时无法获取热点 · 点击刷新":displayedItem.Title;status.Text=DisplayedStatus;inspect.Text="打开 AIHOT";refresh.Text="立即检查";pause.Text=monitor.Paused?"恢复提醒":"暂停提醒";RefreshPresentation();}
    protected override void Dispose(bool disposing){if(disposing){disposed=true;monitor.Changed-=OnChanged;StopOverlay();if(fallbackFrame!=null){fallbackFrame.Dispose();fallbackFrame=null;}}base.Dispose(disposing);}
    public static void RenderEvidence(string directory){Directory.CreateDirectory(directory);using(var monitor=AihotMonitor.CreateRenderFixture(directory)){var item=monitor.Snapshot.Items[0];using(var bmp=AihotRenderer.RenderCard(AihotLayout.CardWidth,AihotLayout.CardHeight,item,monitor.Status,monitor.Paused))bmp.Save(Path.Combine(directory,"aihot-card-compact.png"));using(var bmp=AihotRenderer.RenderNotification(item,""))bmp.Save(Path.Combine(directory,"aihot-popup-compact.png"));}}
  }
  internal sealed class AihotCardOverlay : NoActivateForm {
    readonly AihotSidebarControl card;
    readonly System.Windows.Forms.Timer sync;
    Form owner;
    ScrollableControl scrollParent;
    Bitmap baseFrame, clippedFrame;
    string contentKey="", pushKey="";
    bool closed,forceRender=true;
    public int LastPushError {get;private set;}
    public bool LastPushSucceeded {get;private set;}
    public string LastClip {get;private set;}
    public AihotCardOverlay(AihotSidebarControl value) {
      card=value;
      PerPixelLayered=true;
      FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;
      TopMost=true;
      BackColor=Color.Fuchsia;
      sync=new System.Windows.Forms.Timer { Interval=16 };
      sync.Tick+=(s,e)=>Synchronize();
      GeometryContextChanged+=OnContext;
      LayeredSurfaceChanged+=OnContext;
      FormClosed+=(s,e)=>Cleanup();
    }
    public void SetOwner(Form value) {
      if(owner==value)return;
      UnhookOwner();
      owner=value;
      if(owner==null)return;
      ContextMenuStrip=owner.ContextMenuStrip;
      owner.LocationChanged+=OnGeometryChanged;
      owner.SizeChanged+=OnGeometryChanged;
      owner.VisibleChanged+=OnOwnerVisibility;
      owner.EnabledChanged+=OnOwnerVisibility;
      for(Control c=card.Parent;c!=null&&c!=owner;c=c.Parent) {
        var candidate=c as ScrollableControl;
        if(candidate!=null&&candidate.AutoScroll) {
          scrollParent=candidate;
          scrollParent.Scroll+=OnScroll;
          break;
        }
      }
    }
    void UnhookOwner() {
      if(owner!=null) {
        owner.LocationChanged-=OnGeometryChanged;
        owner.SizeChanged-=OnGeometryChanged;
        owner.VisibleChanged-=OnOwnerVisibility;
        owner.EnabledChanged-=OnOwnerVisibility;
      }
      if(scrollParent!=null) {
        scrollParent.Scroll-=OnScroll;
        scrollParent=null;
      }
    }
    void OnGeometryChanged(object sender,EventArgs e) { Synchronize(); }
    void OnScroll(object sender,ScrollEventArgs e) { Synchronize(); }
    void OnOwnerVisibility(object sender,EventArgs e) {
      if(owner==null||!owner.Visible||!owner.Enabled) {
        sync.Stop();
        if(Visible)Hide();
        return;
      }
      if(Visible)sync.Start();
      Synchronize();
    }
    void OnContext(object sender,EventArgs e) { forceRender=true;Synchronize(); }
    public void MarkDirty() { forceRender=true;Synchronize(); }
    Rectangle VisibleClip(Point loc,Size size) {
      Rectangle clip=Rectangle.Intersect(new Rectangle(loc,size),SystemInformation.VirtualScreen);
      if(scrollParent!=null) {
        if(!scrollParent.Visible)return Rectangle.Empty;
        clip=Rectangle.Intersect(clip,scrollParent.RectangleToScreen(scrollParent.ClientRectangle));
      }
      if(owner==null||!owner.Visible||!owner.Enabled)return Rectangle.Empty;
      clip=Rectangle.Intersect(clip,owner.RectangleToScreen(owner.ClientRectangle));
      return clip;
    }
    static string ItemKey(AihotItem item) {
      if(item==null)return "";
      return (item.Id??"")+"|"+(item.Title??"")+"|"+(item.SourceName??"")+"|"+(item.AihotUrl??"");
    }
    void ReplaceClippedFrame(Rectangle clip,Point loc) {
      if(clippedFrame!=null) { clippedFrame.Dispose();clippedFrame=null; }
      if(baseFrame==null)return;
      clippedFrame=(Bitmap)baseFrame.Clone();
      Rectangle screenClip=Rectangle.Intersect(clip,new Rectangle(loc,clippedFrame.Size));
      Rectangle localClip=new Rectangle(screenClip.X-loc.X,screenClip.Y-loc.Y,Math.Max(0,screenClip.Width),Math.Max(0,screenClip.Height));
      using(var g=Graphics.FromImage(clippedFrame)) {
        g.CompositingMode=CompositingMode.SourceCopy;
        using(var outside=new Region(new Rectangle(Point.Empty,clippedFrame.Size)))
        using(var transparent=new SolidBrush(Color.Transparent)) {
          outside.Exclude(localClip);
          g.SetClip(outside,CombineMode.Replace);
          g.FillRectangle(transparent,0,0,clippedFrame.Width,clippedFrame.Height);
        }
      }
    }
    void Cleanup() {
      if(closed)return;
      closed=true;
      sync.Stop();
      sync.Dispose();
      UnhookOwner();
      if(clippedFrame!=null) { clippedFrame.Dispose();clippedFrame=null; }
      if(baseFrame!=null) { baseFrame.Dispose();baseFrame=null; }
    }
    public void Synchronize() {
      if(closed||card.IsDisposed||owner==null)return;
      bool should=card.Visible&&card.Enabled&&owner.Visible&&owner.Enabled;
      if(!should) { sync.Stop();if(Visible)Hide();return; }
      Point cardLocation=card.PointToScreen(Point.Empty);
      Point loc=new Point(cardLocation.X-AihotLayout.Margin,cardLocation.Y-AihotLayout.Margin);
      Size size=new Size(card.Width+2*AihotLayout.Margin,card.Height+2*AihotLayout.Margin);
      Rectangle bounds=new Rectangle(loc,size);
      if(Bounds!=bounds)Bounds=bounds;
      Rectangle clip=VisibleClip(loc,size);
      byte alpha=(byte)Math.Max(0,Math.Min(255,(int)Math.Round(owner.Opacity*255)));
      AihotItem item=card.displayedItem;
      string status=card.DisplayedStatus;
      bool paused=card.monitor.Paused;
      string nextContentKey=Palette.Theme+"|"+Palette.Solid+"|"+Palette.Legacy+"|"+SystemInformation.HighContrast+"|"+ItemKey(item)+"|"+status+"|"+paused+"|"+card.monitor.IsRefreshing+"|"+card.RefreshHovered+"|"+card.RefreshPressed;
      if(forceRender||baseFrame==null||contentKey!=nextContentKey) {
        if(baseFrame!=null)baseFrame.Dispose();
        baseFrame=AihotRenderer.RenderCard(card.Width,card.Height,item,status,paused,card.monitor.IsRefreshing,card.RefreshHovered,card.RefreshPressed);
        contentKey=nextContentKey;
        forceRender=false;
        pushKey="";
      }
      string nextPushKey=loc+"|"+clip+"|"+alpha+"|"+contentKey;
      if(nextPushKey==pushKey&&Visible)return;
      pushKey=nextPushKey;
      LastClip=clip.ToString();
      ReplaceClippedFrame(clip,loc);
      if(!IsHandleCreated) { IntPtr unused=Handle; }
      LastPushSucceeded=clippedFrame!=null&&GlassWin.Push(Handle,clippedFrame,loc,alpha);
      LastPushError=LastPushSucceeded?0:GlassWin.LastPushError;
      if(!LastPushSucceeded) { card.LayeredPushFailed(LastPushError);return; }
      card.LayeredPushSucceeded();
      if(!Visible)Show(owner);
      sync.Start();
    }
    protected override void OnMouseClick(MouseEventArgs e) {
      base.OnMouseClick(e);
      if(e.Button==MouseButtons.Left)card.HandleFrameClick(e.Location);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
      base.OnMouseDown(e);
      if(e.Button==MouseButtons.Left)card.SetRefreshVisualState(AihotLayout.RefreshHit(card.Width).Contains(e.Location),true);
    }
    protected override void OnMouseUp(MouseEventArgs e) {
      base.OnMouseUp(e);
      card.SetRefreshVisualState(AihotLayout.RefreshHit(card.Width).Contains(e.Location),false);
    }
    protected override void OnMouseLeave(EventArgs e) {
      base.OnMouseLeave(e);card.SetRefreshVisualState(false,false);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      card.SetRefreshVisualState(AihotLayout.RefreshHit(card.Width).Contains(e.Location),card.RefreshPressed&&(e.Button&MouseButtons.Left)!=0);
      bool hit=AihotLayout.RefreshHit(card.Width).Contains(e.Location)||AihotLayout.PauseHit(card.Width).Contains(e.Location)||AihotLayout.OpenHit(card.Width).Contains(e.Location)||AihotLayout.TitleHit(card.Width).Contains(e.Location)||AihotLayout.StoryHit(card.Width).Contains(e.Location);
      Cursor=hit?Cursors.Hand:Cursors.Default;
    }
    protected override void OnMouseWheel(MouseEventArgs e) {
      base.OnMouseWheel(e);
      if(scrollParent==null)return;
      int lines=SystemInformation.MouseWheelScrollLines;
      int amount=lines<0?Math.Max(1,scrollParent.ClientSize.Height-24):Math.Max(1,lines)*Math.Max(1,Font.Height);
      int current=Math.Max(0,-scrollParent.AutoScrollPosition.Y);
      scrollParent.AutoScrollPosition=new Point(Math.Max(0,-scrollParent.AutoScrollPosition.X),Math.Max(0,current-Math.Sign(e.Delta)*amount));
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { Cleanup();base.OnFormClosed(e); }
    protected override void Dispose(bool disposing) { if(disposing)Cleanup();base.Dispose(disposing); }
  }  internal sealed class AihotPopupForm : NoActivateForm {
    static readonly List<AihotPopupForm> OpenForms=new List<AihotPopupForm>();
    readonly AihotItem item;
    readonly Action<string> openUrl;
    readonly System.Windows.Forms.Timer life;
    Bitmap frame;
    string feedback="";
    bool pushing,cleaned;
    public bool LastPushSucceeded {get;private set;}
    public int LastPushError {get;private set;}
    public static void CloseAll() {
      foreach(var form in OpenForms.ToArray()) {
        try { if(!form.IsDisposed)form.Close(); }
        catch { }
      }
      OpenForms.Clear();
    }
    public AihotPopupForm(AihotItem item):this(item,null) { }
    internal AihotPopupForm(AihotItem value,Action<string> opener) {
      item=value;
      openUrl=opener;
      OpenForms.Add(this);
      PerPixelLayered=true;
      FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;
      TopMost=true;
      StartPosition=FormStartPosition.Manual;
      Size=new Size(CornerLayout.Width+2*AihotLayout.Margin,CornerLayout.CompactHeight+2*AihotLayout.Margin);
      BackColor=Color.Fuchsia;
      Rectangle area=Screen.PrimaryScreen.WorkingArea;
      Rectangle body=CornerLayout.Body(area,CornerLayout.CompactHeight,AihotLayout.Margin,SystemInformation.VerticalScrollBarWidth);
      Location=new Point(body.Left-AihotLayout.Margin,body.Bottom+CornerLayout.Gap-AihotLayout.Margin);
      GeometryContextChanged+=OnSurfaceChanged;
      LayeredSurfaceChanged+=OnSurfaceChanged;
      life=new System.Windows.Forms.Timer { Interval=12000 };
      life.Tick+=(sender,args)=>Close();
    }
    protected override CreateParams CreateParams {
      get {
        var parameters=base.CreateParams;
        parameters.ExStyle|=0x08000000|0x00080000;
        return parameters;
      }
    }
    public void ShowLayered() {
      IntPtr unused=Handle;
      if(!PushFrame()) {
        int error=LastPushError;
        Dispose();
        throw new InvalidOperationException("AIHOT notification surface could not be presented ("+error+").");
      }
      Show();
      life.Start();
    }
    protected override void OnShown(EventArgs e) {
      base.OnShown(e);
      if(!LastPushSucceeded&&!PushFrame())Close();
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnMouseClick(MouseEventArgs e) {
      base.OnMouseClick(e);
      if(e.Button!=MouseButtons.Left)return;
      if(AihotLayout.NotificationCloseHit(AihotLayout.Margin).Contains(e.Location)) {
        Close();
        return;
      }
      if(AihotLayout.NotificationBody(AihotLayout.Margin).Contains(e.Location))OpenNotification();
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      bool hit=AihotLayout.NotificationCloseHit(AihotLayout.Margin).Contains(e.Location)||AihotLayout.NotificationBody(AihotLayout.Margin).Contains(e.Location);
      Cursor=hit?Cursors.Hand:Cursors.Default;
    }
    void OnSurfaceChanged(object sender,EventArgs e) {
      if(!IsDisposed&&Visible&&!PushFrame())Close();
    }
    bool PushFrame() {
      if(pushing||IsDisposed||!IsHandleCreated)return false;
      pushing=true;
      try {
        Bitmap next=AihotRenderer.RenderNotification(item,feedback);
        if(frame!=null)frame.Dispose();
        frame=next;
        LastPushSucceeded=GlassWin.Push(Handle,frame,Location,(byte)255);
        LastPushError=LastPushSucceeded?0:GlassWin.LastPushError;
        return LastPushSucceeded;
      } finally {
        pushing=false;
      }
    }
    void OpenNotification() {
      if(AihotNavigation.OpenItem(item==null?null:item.AihotUrl,openUrl)) {
        Close();
        return;
      }
      feedback="无法打开默认浏览器，请检查 Windows 浏览器设置后重试。";
      if(!PushFrame())Close();
    }
    void Cleanup() {
      if(cleaned)return;
      cleaned=true;
      life.Stop();
      life.Dispose();
      GeometryContextChanged-=OnSurfaceChanged;
      LayeredSurfaceChanged-=OnSurfaceChanged;
      if(frame!=null) { frame.Dispose();frame=null; }
      OpenForms.Remove(this);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) {
      Cleanup();
      base.OnFormClosed(e);
    }
    protected override void Dispose(bool disposing) {
      if(disposing)Cleanup();
      base.Dispose(disposing);
    }
  }
}
