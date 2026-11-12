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
    public string Summary, Revision, Progress;
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
    public bool InboxInitialized;
    public List<AihotInboxRecord> Inbox = new List<AihotInboxRecord>();
    public List<AihotInboxRecord> ReadHistory = new List<AihotInboxRecord>();
  }
  [Serializable]
  public sealed class AihotInboxRecord {
    public string Key, Revision;
    public AihotItem Item;
  }
  public sealed class AihotMonitor : IDisposable {
    static readonly Uri Endpoint = new Uri("https://aihot.news/api/v1/hot-topics");
    readonly string statePath;
    readonly object gate = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength=8*1024*1024, RecursionLimit=128 };
    AihotSavedState state;
    CancellationTokenSource cancel;
    AihotPopupForm notification;
    AihotItem notificationItem;
    int notificationOldIndex;
    DateTime notificationOldPopup;
    List<string> notificationBatchIds=new List<string>();
    List<string> notificationAddedSeen=new List<string>();
    bool busy, disposed, enabled, freshData, stateReadFailed;
    string etag;
    DateTime nextRequestUtc, retryNotBeforeUtc;
    DateTime detailRetryNotBeforeUtc;
    bool detailsIncomplete;
    AihotSnapshot snapshot;
    string message = "正在连接 AIHOT";
    DateTime checkedUtc;
    public event EventHandler Changed;
    public event EventHandler NotificationChanged;
    public event EventHandler InboxRequested;
    public List<AihotItem> UnreadItems { get { lock(gate) return state.Inbox.Where(x=>x!=null&&x.Item!=null).Select(x=>x.Item).OrderByDescending(x=>x.LatestAt).ThenBy(x=>x.Id,StringComparer.Ordinal).ToList(); } }
    public int UnreadCount { get { lock(gate) return state.Inbox.Count; } }
    public bool MarkRead(AihotItem displayedItem) {
      if(displayedItem==null)return false;
      bool saved=true;
      lock(gate) {
        string key=StableKey(displayedItem);
        int index=state.Inbox.FindIndex(x=>x!=null&&String.Equals(x.Key,key,StringComparison.Ordinal));
        if(index<0) {
          var alreadyRead=state.ReadHistory.FirstOrDefault(x=>x!=null&&String.Equals(x.Key,key,StringComparison.Ordinal));
          return alreadyRead!=null&&String.Equals(alreadyRead.Revision,displayedItem.Revision,StringComparison.Ordinal);
        }
        if(!String.Equals(state.Inbox[index].Revision,displayedItem.Revision,StringComparison.Ordinal))return false;
        var old=state.Inbox[index]; int pendingIndex=state.Pending.IndexOf(old.Item.Id);
        int historyIndex=state.ReadHistory.FindIndex(x=>x!=null&&String.Equals(x.Key,key,StringComparison.Ordinal));
        var readHistoryBackup=state.ReadHistory.ToList();
        state.Inbox.RemoveAt(index); state.Pending.Remove(old.Item.Id);
        var readRecord=new AihotInboxRecord{Key=old.Key,Revision=old.Revision,Item=old.Item};
        if(historyIndex>=0)state.ReadHistory[historyIndex]=readRecord;else state.ReadHistory.Add(readRecord);
        while(state.ReadHistory.Count>500)state.ReadHistory.RemoveAt(0);
        if(!SaveState()){
          state.Inbox.Insert(index,old);if(pendingIndex>=0)state.Pending.Insert(Math.Min(pendingIndex,state.Pending.Count),old.Item.Id);
          state.ReadHistory.Clear();state.ReadHistory.AddRange(readHistoryBackup);
          freshData=false;message="无法保存已读状态";
          saved=false;
        }
      }
      RaiseChanged(); return saved;
    }
    public bool NotificationOpen { get { lock(gate) return notification!=null&&!notification.IsDisposed; } }
    public bool SetNotificationBounds(Rectangle bounds) {
      AihotPopupForm current;
      lock(gate) current=notification;
      return current!=null&&!current.IsDisposed&&current.SetNotificationBounds(bounds);
    }
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
      if(stateReadFailed)message="无法读取提醒状态文件 · 保留原文件并暂停写入";
    }
    public static AihotMonitor CreateRenderFixture(string stateDirectory) {
      var monitor=new AihotMonitor(stateDirectory);
      monitor.snapshot=new AihotSnapshot {
        SchemaVersion=1, Count=10
      }
      ;
      for(int rank=1;rank<=10;rank++) monitor.snapshot.Items.Add(new AihotItem {
        Rank=rank, Id="fixture-"+rank, Title="[FIXTURE] 热点示例 "+rank, Summary="[示例摘要] 这是用于界面预览的示例内容。", Revision="[FIXTURE] 热点示例 "+rank+"\n[示例摘要] 这是用于界面预览的示例内容。", SourceName="[FIXTURE] 来源", AihotUrl="https://aihot.news/fixture/"+rank, LatestAt=DateTime.UtcNow.AddMinutes(-rank)
      }
      );
      monitor.state.InboxInitialized=true;
      monitor.state.Inbox=monitor.snapshot.Items.Take(3).Select(x=>new AihotInboxRecord{Key=StableKey(x),Revision=x.Revision,Item=x}).ToList();
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
          CloseNotification();
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
      else CloseNotification();
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
      if(value)CloseNotification();
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
              AihotSnapshot unchanged;lock(gate)unchanged=snapshot;
              if(unchanged!=null) {
                AihotSnapshot retry=CloneSnapshot(unchanged);
                bool complete=await EnrichSummaries(retry,unchanged,token).ConfigureAwait(false);
                lock(gate) {
                  if(disposed||!enabled)return;
                  AihotSnapshot oldSnapshot=snapshot;
                  snapshot=retry;
                  detailsIncomplete=!complete;
                  ReconcileInbox(retry,false,oldSnapshot);
                  ObserveMonitorTopTen(retry,false);
                  checkedUtc=DateTime.UtcNow;freshData=true;nextRequestUtc=DateTime.UtcNow.AddMinutes(5);
                  message="AIHOT 热点 · 已检查 "+checkedUtc.ToLocalTime().ToString("HH:mm:ss");
                  if(!SaveState()){freshData=false;message="无法保存提醒状态 · 已暂停提醒";}
                }
                return;
              }
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
            AihotSnapshot before; lock(gate)before=snapshot;
            bool detailsComplete=await EnrichSummaries(fresh,before,token).ConfigureAwait(false);
            detailsIncomplete=!detailsComplete;
            if(detailsComplete&&response.Headers[HttpResponseHeader.ETag]!=null) etag=response.Headers[HttpResponseHeader.ETag];
            else if(!detailsComplete)etag=null;
            lock(gate) {
              if(disposed||!enabled)return;
              bool seed=snapshot==null && !state.InboxInitialized;
              AihotSnapshot previousSnapshot=snapshot;
              snapshot=fresh;
              checkedUtc=DateTime.UtcNow;
              freshData=true;
              message="AIHOT 热点 · 已检查 "+checkedUtc.ToLocalTime().ToString("HH:mm:ss");
              ReconcileInbox(fresh,seed,previousSnapshot);
              ObserveMonitorTopTen(fresh,seed);
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
    AihotItem FindCachedStoryItem(string key,AihotSnapshot previous) {
      AihotItem cached=previous==null?null:previous.Items.FirstOrDefault(x=>StableKey(x)==key);
      if(cached!=null)return cached;
      lock(gate) {
        var unread=state.Inbox.FirstOrDefault(x=>x!=null&&x.Key==key);
        if(unread!=null)return unread.Item;
        var read=state.ReadHistory.FirstOrDefault(x=>x!=null&&x.Key==key);
        return read==null?null:read.Item;
      }
    }
    static AihotSnapshot CloneSnapshot(AihotSnapshot source) {
      return new AihotSnapshot { SchemaVersion=source.SchemaVersion,Count=source.Count,Items=source.Items.Select(x=>new AihotItem {
        Rank=x.Rank,Id=x.Id,Title=x.Title,SourceName=x.SourceName,AihotUrl=x.AihotUrl,OriginalUrl=x.OriginalUrl,StoryUrl=x.StoryUrl,
        Summary=x.Summary,Revision=x.Revision,Progress=x.Progress,SourceCount=x.SourceCount,SignalCount=x.SignalCount,
        SourceNames=x.SourceNames==null?new string[0]:x.SourceNames.ToArray(),LatestAt=x.LatestAt
      }).ToList() };
    }
    async Task<bool> EnrichSummaries(AihotSnapshot fresh,AihotSnapshot previous,CancellationToken token) {
      bool complete=true;
      var detailItems=fresh.Items.Where(x=>x.Rank<=10).Take(10).ToList();
      for(int itemIndex=0;itemIndex<detailItems.Count;itemIndex++) {
        var item=detailItems[itemIndex];
        if(token.IsCancellationRequested){complete=false;break;}
        if(!IsStoryGuid(item.StoryUrl))continue;
        string key=StableKey(item);
        var old=FindCachedStoryItem(key,previous);
        if(old!=null&&!String.IsNullOrWhiteSpace(old.Summary)) {
          item.Summary=old.Summary;item.Progress=old.Progress;item.Revision=ContentRevision(item);
        }
        if(DateTime.UtcNow<detailRetryNotBeforeUtc){
          complete=false;break;
        }
        bool detailTimedOut=false;
        bool parsed=false;
        try {
          Uri story=new Uri(item.StoryUrl); string id=story.AbsolutePath.Substring("/story/".Length).Trim('/');
          var req=(HttpWebRequest)WebRequest.Create("https://aihot.news/api/v1/agent/stories/"+id);
          req.Method="GET";req.UserAgent="aihot-api/2.0.0";req.Timeout=8000;req.ReadWriteTimeout=8000;req.AllowAutoRedirect=false;
          using(var detailTimeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
          detailTimeout.CancelAfter(8000);
          using(detailTimeout.Token.Register(()=>{detailTimedOut=!token.IsCancellationRequested;try{req.Abort();}catch{}})) using(var response=(HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false)) {
            if(response.StatusCode!=HttpStatusCode.OK)continue;
            using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)) {
              string text=await reader.ReadToEndAsync().ConfigureAwait(false); if(text.Length>262144)continue;
              parsed=ParseStoryMarkdown(text,item);
            }
          }
          }
          if(!parsed)complete=false;
        } catch(OperationCanceledException) { complete=false;if(old!=null&&item.Progress==null){item.Summary=old.Summary;item.Progress=old.Progress;}break;
        } catch(WebException ex) {
          complete=false;
          var response=ex.Response as HttpWebResponse;
          if(response!=null)using(response)if((int)response.StatusCode==429||(int)response.StatusCode==503)detailRetryNotBeforeUtc=RetryDeadline(response.Headers["Retry-After"],DateTime.UtcNow);
          if(old!=null&&item.Progress==null){item.Summary=old.Summary;item.Progress=old.Progress;}
          if(detailRetryNotBeforeUtc>DateTime.UtcNow||token.IsCancellationRequested||detailTimedOut) {
            break;
          }
        } catch { complete=false;if(old!=null&&item.Progress==null){item.Summary=old.Summary;item.Progress=old.Progress;} }
        item.Revision=ContentRevision(item);
      }
      return complete;
    }
    static bool IsStoryGuid(string url) {
      Uri u;Guid id;return Uri.TryCreate(url,UriKind.Absolute,out u)&&u.Scheme=="https"&&String.Equals(u.Host,"aihot.news",StringComparison.OrdinalIgnoreCase)&&u.Port==443&&u.AbsolutePath.StartsWith("/story/",StringComparison.OrdinalIgnoreCase)&&Guid.TryParse(u.AbsolutePath.Substring(7).Trim('/'),out id);
    }
    static bool ParseStoryMarkdown(string markdown,AihotItem item) {
      if(String.IsNullOrWhiteSpace(markdown)||item==null)return false;
      string progress=null,synopsis=null;
      foreach(string raw in markdown.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
        string line=raw.Trim().TrimStart('#','*','-',' ');
        if(line.StartsWith("最新进展",StringComparison.Ordinal)) { int colon=line.IndexOf('：');if(colon<0)colon=line.IndexOf(':');if(colon>=0&&colon+1<line.Length)progress=FirstSentence(line.Substring(colon+1).Trim()); }
        else if(line.StartsWith("事件综述",StringComparison.Ordinal)) { int colon=line.IndexOf('：');if(colon<0)colon=line.IndexOf(':');if(colon>=0&&colon+1<line.Length)synopsis=FirstSentence(line.Substring(colon+1).Trim()); }
      }
      if(!String.IsNullOrWhiteSpace(progress))item.Progress=progress;
      string summary=synopsis??progress;
      if(!String.IsNullOrWhiteSpace(summary))item.Summary=summary;
      return !String.IsNullOrWhiteSpace(progress)||!String.IsNullOrWhiteSpace(synopsis);
    }
    static string FirstSentence(string value) { if(String.IsNullOrWhiteSpace(value))return null;string s=value.Trim();int n=s.IndexOfAny(new[]{'。','！','？','\n'});return n>0?s.Substring(0,n+1):s; }
    static IEnumerable<string> TopTenIds(AihotSnapshot value) {
      return value.Items.Where(x=>x.Rank<=10).OrderBy(x=>x.Rank).Select(x=>x.Id);
    }
    void ObserveMonitorTopTen(AihotSnapshot value,bool seed) {
      if(seed) { ObserveTopTen(value,state.Seen,state.Pending,true);return; }
      foreach(var item in value.Items.Where(x=>x.Rank<=10).OrderBy(x=>x.Rank)) {
        if(state.Seen.Contains(item.Id)||state.Pending.Contains(item.Id))continue;
        string key=StableKey(item);
        if(state.Inbox.Any(r=>r!=null&&r.Key==key)||state.ReadHistory.Any(r=>r!=null&&r.Key==key))continue;
        state.Pending.Add(item.Id);
      }
    }
    static string StableKey(AihotItem item) {
      if(item!=null&&!String.IsNullOrWhiteSpace(item.StoryUrl)) { Uri u; Guid id; if(Uri.TryCreate(item.StoryUrl,UriKind.Absolute,out u)&&String.Equals(u.Host,"aihot.news",StringComparison.OrdinalIgnoreCase)&&u.AbsolutePath.StartsWith("/story/",StringComparison.OrdinalIgnoreCase)&&Guid.TryParse(u.AbsolutePath.Substring(7).Trim('/'),out id))return "story:"+id.ToString("D").ToLowerInvariant(); }
      return "item:"+(item==null?"":item.Id??"");
    }
    static string ContentRevision(AihotItem x) { return (x.Title??"").Trim()+"\n"+(String.IsNullOrWhiteSpace(x.Progress)?(x.Summary??"").Trim():x.Progress.Trim()); }
    static bool ContentProgressed(AihotItem oldItem,AihotItem newItem) {
      if(oldItem==null||newItem==null)return false;
      if(!String.Equals((oldItem.Title??"").Trim(),(newItem.Title??"").Trim(),StringComparison.Ordinal))return true;
      string oldText=String.IsNullOrWhiteSpace(oldItem.Progress)?null:oldItem.Progress.Trim();
      string newText=String.IsNullOrWhiteSpace(newItem.Progress)?null:newItem.Progress.Trim();
      if(!String.IsNullOrEmpty(oldText)&&!String.IsNullOrEmpty(newText))return !String.Equals(oldText,newText,StringComparison.Ordinal);
      if(String.IsNullOrEmpty(oldText)&&String.IsNullOrEmpty(newText)) {
        string oldSummary=(oldItem.Summary??"").Trim(),newSummary=(newItem.Summary??"").Trim();
        if(String.IsNullOrEmpty(oldSummary)||oldSummary=="摘要未提供"||String.IsNullOrEmpty(newSummary))return false;
        return !String.Equals(oldSummary,newSummary,StringComparison.Ordinal);
      }
      return false;
    }
    void ReconcileInbox(AihotSnapshot fresh,bool seed,AihotSnapshot previous) {
      if(state.Inbox==null)state.Inbox=new List<AihotInboxRecord>();
      if(!state.InboxInitialized) {
        foreach(var x in fresh.Items.Where(x=>x.Rank<=10)) { string revision=ContentRevision(x); x.Revision=revision; if(!state.Inbox.Any(r=>r.Key==StableKey(x))) state.Inbox.Add(new AihotInboxRecord{Key=StableKey(x),Revision=revision,Item=x}); }
        state.InboxInitialized=true; return;
      }
      foreach(var x in fresh.Items.Where(x=>x.Rank<=10)) {
        string key=StableKey(x), revision=ContentRevision(x);
        x.Revision=revision;
        var record=state.Inbox.FirstOrDefault(r=>r.Key==key);
        if(record!=null) {
          bool unreadProgressed=ContentProgressed(record.Item,x);
          string oldId=record.Item==null?null:record.Item.Id;
          if(!String.IsNullOrEmpty(oldId)&&oldId!=x.Id&&state.Pending.Remove(oldId)&&!state.Pending.Contains(x.Id))state.Pending.Add(x.Id);
          record.Item=x; record.Revision=revision;
          if(unreadProgressed&&!state.Pending.Contains(x.Id))state.Pending.Add(x.Id);
          continue;
        }
        var read=state.ReadHistory.FirstOrDefault(r=>r!=null&&r.Key==key);
        if(read!=null) {
          if(!ContentProgressed(read.Item,x)) {
            state.Pending.Remove(x.Id);
            read.Item=x;read.Revision=revision;continue;
          }
          state.ReadHistory.Remove(read);
          if(!state.Pending.Contains(x.Id))state.Pending.Add(x.Id);
          state.Inbox.Add(new AihotInboxRecord{Key=key,Revision=revision,Item=x});
          continue;
        }
        AihotItem prior=previous==null?null:previous.Items.FirstOrDefault(y=>StableKey(y)==key);
        bool known=state.Seen.Contains(x.Id)||state.Pending.Contains(x.Id);
        bool newlyObservedProgress=!seed&&ContentProgressed(prior,x);
        if(!known||newlyObservedProgress) {
          state.Inbox.Add(new AihotInboxRecord{Key=key,Revision=revision,Item=x});
          if(newlyObservedProgress&&!state.Pending.Contains(x.Id))state.Pending.Add(x.Id);
        }
      }
    }
    public static List<string> NewTopTenIds(AihotSnapshot value,ICollection<string> seen) {
      return TopTenIds(value).Where(id=>!seen.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
    }
    public static void ObserveTopTen(AihotSnapshot value,List<string> seen,List<string> pending,bool seed) {
      if(seed) {
        foreach(string id in TopTenIds(value)) AddBounded(seen,id,300);
        return;
      }
      foreach(string id in NewTopTenIds(value,seen)) if(!pending.Contains(id))pending.Add(id);
    }
    public static bool CanAlert(bool enabled,bool paused,bool allowPopup,bool haveSnapshot,bool freshData,DateTime checkedUtc,DateTime lastPopupUtc,DateTime nowUtc) {
      return enabled && !paused && allowPopup && haveSnapshot && freshData && nowUtc>=checkedUtc && nowUtc-checkedUtc<=TimeSpan.FromMinutes(15) && nowUtc-lastPopupUtc>=TimeSpan.FromSeconds(15);
    }
    public static bool CanStartPoll(bool enabled,bool busy,DateTime nowUtc,DateTime nextRequestUtc,DateTime retryNotBeforeUtc) {
      return enabled && !busy && nowUtc>=nextRequestUtc && nowUtc>=retryNotBeforeUtc;
    }
    void TryNotify() {
      AihotItem item=null; List<string> batch=new List<string>(); List<string> addedSeen=new List<string>();
      int oldIndex=-1;
      DateTime oldPopup=DateTime.MinValue;
      lock(gate) {
        if(disposed||AihotPopupForm.HasVisiblePopup||!CanAlert(enabled,state.Paused,true,snapshot!=null,freshData,checkedUtc,state.LastPopupUtc,DateTime.UtcNow))return;
        var eligible=state.Pending.ToList();
        var records=state.Inbox.Where(r=>r!=null&&r.Item!=null&&eligible.Contains(r.Item.Id)&&String.Equals(r.Revision,r.Item.Revision,StringComparison.Ordinal)).OrderByDescending(r=>r.Item.LatestAt).ToList();
        if(records.Count==0)return;
        item=records[0].Item; batch=records.Select(r=>r.Item.Id).Distinct(StringComparer.Ordinal).ToList();
        oldIndex=state.Pending.IndexOf(batch[0]);
        oldPopup=state.LastPopupUtc;
        foreach(string id in batch){state.Pending.Remove(id);if(!state.Seen.Contains(id)){state.Seen.Add(id);addedSeen.Add(id);}}
        state.LastPopupUtc=DateTime.UtcNow;
        if(!SaveState()) {
          foreach(string id in batch)if(!state.Pending.Contains(id))state.Pending.Insert(Math.Max(0,Math.Min(oldIndex,state.Pending.Count)),id);
          state.LastPopupUtc=oldPopup;
          foreach(string id in addedSeen)state.Seen.Remove(id);
          freshData=false;message="无法保存提醒状态 · 已暂停提醒";
          return;
        }
      }
      AihotPopupForm f=null;
      lock(gate){notificationBatchIds=batch;notificationAddedSeen=addedSeen;}
      try {
        f=new AihotPopupForm(item,batch.Count,()=>RaiseInboxRequested(),()=>MarkRead(item));
        f.NotificationClosed+=OnNotificationClosed;
        lock(gate) { notification=f;notificationItem=item;notificationOldIndex=oldIndex;notificationOldPopup=oldPopup;notificationBatchIds=batch;notificationAddedSeen=addedSeen; }
        RaiseNotificationChanged();
        if(!f.HasNotificationBounds) { RollbackNotification(f,item,oldIndex,oldPopup,batch,addedSeen);return; }
        f.ShowLayered();
      } catch { RollbackNotification(f,item,oldIndex,oldPopup,batch,addedSeen); }
    }
    void RollbackNotification(AihotPopupForm form,AihotItem item,int oldIndex,DateTime oldPopup,IList<string> batch,IList<string> addedSeen) {
      lock(gate) {
        if(form!=null&&Object.ReferenceEquals(notification,form)){notification=null;notificationItem=null;notificationOldIndex=-1;notificationOldPopup=DateTime.MinValue;}
        var ids=batch==null||batch.Count==0?new List<string>{item.Id}:batch.ToList();
        var added=addedSeen==null?new List<string>():addedSeen.ToList();
        foreach(string id in added)state.Seen.Remove(id);
        foreach(string id in ids)if(!state.Pending.Contains(id))state.Pending.Insert(Math.Max(0,Math.Min(oldIndex,state.Pending.Count)),id);
        state.LastPopupUtc=oldPopup;
        if(!SaveState()){freshData=false;message="无法保存提醒状态 · 已暂停提醒";}
        notificationBatchIds=new List<string>();notificationAddedSeen=new List<string>();
      }
      if(form!=null) { form.NotificationClosed-=OnNotificationClosed;try{form.Dispose();}catch{} }
      RaiseNotificationChanged();
    }
    void OnNotificationClosed(object sender,EventArgs e) {
      bool changed=false;
      lock(gate)if(Object.ReferenceEquals(notification,sender)){notification=null;notificationItem=null;notificationOldIndex=-1;notificationOldPopup=DateTime.MinValue;notificationBatchIds=new List<string>();notificationAddedSeen=new List<string>();changed=true;}
      if(changed)RaiseNotificationChanged();
    }
    void RaiseInboxRequested(){var h=InboxRequested;if(h!=null)try{h(this,EventArgs.Empty);}catch{}}
    void RaiseNotificationChanged() { var h=NotificationChanged;if(h!=null)try{h(this,EventArgs.Empty);}catch{} }
    public void DeferNotification() {
      AihotPopupForm current;AihotItem item;int index;DateTime popup;
      List<string> batch,added;
      lock(gate){current=notification;item=notificationItem;index=notificationOldIndex;popup=notificationOldPopup;batch=notificationBatchIds.ToList();added=notificationAddedSeen.ToList();}
      if(current!=null&&item!=null)RollbackNotification(current,item,index,popup,batch,added);
    }
    public void CloseNotification() {
      AihotPopupForm current;
      lock(gate)current=notification;
      if(current!=null&&!current.IsDisposed)try{current.Close();}catch{current.Dispose();}
    }
    static void AddBounded(List<string> list,string value,int max) {
      if(!list.Contains(value)) list.Add(value);
      while(list.Count>max)list.RemoveAt(0);
    }
    bool SaveState() {
      if(stateReadFailed)return false;
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
    AihotSavedState LoadState(string path) {
      if(!File.Exists(path))return new AihotSavedState();
      try {
        var s=new JavaScriptSerializer { MaxJsonLength=8*1024*1024, RecursionLimit=128 }.Deserialize<AihotSavedState>(File.ReadAllText(path));
          if(s!=null) {
          if(s.Seen==null)s.Seen=new List<string>();
          if(s.Pending==null)s.Pending=new List<string>();
          if(s.Inbox==null)s.Inbox=new List<AihotInboxRecord>();
          if(s.ReadHistory==null)s.ReadHistory=new List<AihotInboxRecord>();
          return s;
          }
          else throw new InvalidDataException("empty state");
      }
      catch {
      }
      stateReadFailed=true;
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
      var ser=new JavaScriptSerializer { MaxJsonLength=8*1024*1024, RecursionLimit=128 };
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
          Id=id,Title=title,Rank=rank,SourceName=Get(source,"name")??"来源未注明",AihotUrl=SafeUrl(Child(links,"aihot")),OriginalUrl=SafeExternalUrl(Child(links,"original")),StoryUrl=SafeExternalUrl(Child(links,"story")),SourceCount=Int(obj,"sourceCount"),SignalCount=Int(obj,"signalCount"),SourceNames=names==null?new string[0]:names.Take(12).Select(x=>Convert.ToString(x)).ToArray(),LatestAt=time,Summary="摘要未提供",Revision=title+"\n摘要未提供"
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
      CloseNotification();
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
  public static class AihotNotificationLayout {
    public static int ReservedHeight(bool notificationOpen,bool pinkyRequested) { return notificationOpen&&!pinkyRequested?CornerLayout.CompactHeight+CornerLayout.Gap:0; }
    public static bool TryGetBounds(Rectangle workArea,Rectangle column,int footerHeight,int pinkyReservedHeight,int minimumCardsHeight,bool pinkyRequested,out Rectangle bounds) {
      bounds=Rectangle.Empty;
      int topSlot=CornerLayout.CompactHeight+CornerLayout.Gap;
      if(pinkyRequested||workArea.Width<=0||workArea.Height<=0||column.Width<CornerLayout.Width)return false;
      if(column.Height-footerHeight-8-pinkyReservedHeight-topSlot<minimumCardsHeight)return false;
      var candidate=new Rectangle(column.Left-AihotLayout.Margin,column.Top-AihotLayout.Margin,CornerLayout.Width+2*AihotLayout.Margin,CornerLayout.CompactHeight+2*AihotLayout.Margin);
      if(!workArea.Contains(candidate))return false;
      bounds=candidate;
      return true;
    }
  }
  internal sealed class AihotPopupForm : NoActivateForm {
    static readonly List<AihotPopupForm> OpenForms=new List<AihotPopupForm>();
    public static bool HasVisiblePopup { get { return OpenForms.Any(form=>!form.IsDisposed&&form.Visible); } }
    readonly AihotItem item;
    readonly Action<string> openUrl;
    readonly Action inboxAction;
    readonly Action openedAction;
    readonly int batchCount;
    readonly System.Windows.Forms.Timer life;
    Bitmap frame;
    string feedback="";
    bool pushing,cleaned;
    public bool LastPushSucceeded {get;private set;}
    public int LastPushError {get;private set;}
    Rectangle notificationBounds;
    public event EventHandler NotificationClosed;
    public bool HasNotificationBounds {get{return !notificationBounds.IsEmpty;}}
    public bool SetNotificationBounds(Rectangle bounds) {
      if(IsDisposed)return false;
      if(notificationBounds==bounds)return !bounds.IsEmpty;
      notificationBounds=bounds;
      if(bounds.IsEmpty)return false;
      Bounds=bounds;
      return !Visible||PushFrame();
    }
    public static void CloseAll() {
      foreach(var form in OpenForms.ToArray()) {
        try { if(!form.IsDisposed)form.Close(); }
        catch { }
      }
      OpenForms.Clear();
    }
    public AihotPopupForm(AihotItem item):this(item,null,1,null,null) { }
    internal AihotPopupForm(AihotItem value,Action<string> opener):this(value,opener,1,null,null) { }
    internal AihotPopupForm(AihotItem value,int count,Action inbox):this(value,count,inbox,null) { }
    internal AihotPopupForm(AihotItem value,int count,Action inbox,Action onOpened):this(value,null,count,inbox,onOpened) { }
    AihotPopupForm(AihotItem value,Action<string> opener,int count,Action inbox,Action onOpened) {
      item=value;
      openUrl=opener;
      batchCount=Math.Max(1,count);
      inboxAction=inbox;
      openedAction=onOpened;
      OpenForms.Add(this);
      PerPixelLayered=true;
      FormBorderStyle=FormBorderStyle.None;
      ShowInTaskbar=false;
      TopMost=true;
      StartPosition=FormStartPosition.Manual;
      Size=new Size(CornerLayout.Width+2*AihotLayout.Margin,CornerLayout.CompactHeight+2*AihotLayout.Margin);
      BackColor=Color.Fuchsia;
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
      if(AihotLayout.NotificationInboxHit(AihotLayout.Margin).Contains(e.Location)) { if(inboxAction!=null)inboxAction();Close();return; }
      if(AihotLayout.NotificationCloseHit(AihotLayout.Margin).Contains(e.Location)) {
        Close();
        return;
      }
      if(AihotLayout.NotificationBody(AihotLayout.Margin).Contains(e.Location))OpenNotification();
    }
    protected override void OnMouseMove(MouseEventArgs e) {
      base.OnMouseMove(e);
      bool hit=AihotLayout.NotificationCloseHit(AihotLayout.Margin).Contains(e.Location)||AihotLayout.NotificationBody(AihotLayout.Margin).Contains(e.Location)||AihotLayout.NotificationInboxHit(AihotLayout.Margin).Contains(e.Location);
      Cursor=hit?Cursors.Hand:Cursors.Default;
    }
    void OnSurfaceChanged(object sender,EventArgs e) {
      if(!IsDisposed&&Visible&&!PushFrame())Close();
    }
    bool PushFrame() {
      if(pushing||IsDisposed||!IsHandleCreated)return false;
      pushing=true;
      try {
        Bitmap next=AihotRenderer.RenderNotification(item,feedback,batchCount);
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
        if(openedAction!=null)try{openedAction();}catch{}
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
      var h=NotificationClosed;if(h!=null)try{h(this,EventArgs.Empty);}catch{}
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
