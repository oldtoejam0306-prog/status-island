using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IslandMedia {
  public sealed class Snapshot {
    public string Title="网易云音乐", Artist="", Album="", Detail="尚未读取系统媒体", SourceId="", TrackId="";
    public byte[] Cover;
    public double? PositionSeconds,DurationSeconds;public DateTimeOffset? PositionTimestamp,PlaybackObservedAt;
    public bool CanToggle,CanPrevious,CanNext,Playing,CanSeek;public long ObservedTicks;
  }
  public sealed class NeteaseSource : IDisposable {
    GlobalSystemMediaTransportControlsSession session;
    GlobalSystemMediaTransportControlsSessionManager manager;
    DateTime retryAfter=DateTime.MinValue;
    int failures;readonly SemaphoreSlim nativeReads=new SemaphoreSlim(1,1);readonly MediaSessionLease nativeLease=new MediaSessionLease();bool hasCurrentSessionEvent;int nativeManagerRefreshes;public int NativeManagerRefreshCount{get{return Volatile.Read(ref nativeManagerRefreshes);}}static long NativeNow{get{return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*1000d/System.Diagnostics.Stopwatch.Frequency);}}
    readonly Func<DateTime> clock;
    readonly Func<Task<Snapshot>> provider;
    bool disposed;volatile bool metadataDirty=true,sessionDirty;GlobalSystemMediaTransportControlsSessionMediaProperties cachedProperties;DateTime propertiesAt=DateTime.MinValue,fastUntil=DateTime.MinValue;readonly MediaAssetCache assets=new MediaAssetCache();readonly Func<Task<byte[]>> artworkProvider;readonly Func<string,Task<bool>> controlProvider;int controlBusy;int mediaGeneration;readonly Func<double,Task<bool>> seekProvider;public event Action StateChanged;public ControlTrace LastControlTrace {get;private set;}bool hasPlaybackEvent,hasPropertiesEvent,hasTimelineEvent,hasSessionsEvent;public bool NativeEventsBound {get{return hasPlaybackEvent&&hasPropertiesEvent&&hasTimelineEvent;}}
    readonly CancellationTokenSource stopped=new CancellationTokenSource();
    public NeteaseSource() : this(null,null,null,null) {}
    public NeteaseSource(Func<DateTime> clock, Func<Task<Snapshot>> provider,Func<string,Task<bool>> controlProvider=null,Func<Task<byte[]>> artworkProvider=null,Func<double,Task<bool>> seekProvider=null) { this.clock=clock ?? (()=>DateTime.UtcNow); this.provider=provider;this.controlProvider=controlProvider;this.artworkProvider=artworkProvider;this.seekProvider=seekProvider;assets.Changed+=RaiseChanged; }
    public void RequestFreshMetadata(){if(!disposed)metadataDirty=true;}
    public void Dispose() { if(disposed)return; disposed=true; stopped.Cancel();assets.Dispose();ResetNativeManager();StateChanged=null; }
    void ResetNativeManager(){BindSession(null);var old=manager;manager=null;if(old!=null){try{if(hasSessionsEvent)old.SessionsChanged-=SessionsChanged;}catch{}try{if(hasCurrentSessionEvent)old.CurrentSessionChanged-=CurrentSessionChangedHandler;}catch{}}hasSessionsEvent=hasCurrentSessionEvent=false;}
    void CurrentSessionChangedHandler(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args){SessionsChanged(sender,null);}
    void RaiseChanged(){if(disposed)return;var changed=StateChanged;if(changed!=null)try{changed();}catch{}}
    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args){RaiseChanged();}
    void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender,MediaPropertiesChangedEventArgs args){Interlocked.Increment(ref mediaGeneration);metadataDirty=true;RaiseChanged();}
    void TimelineChanged(GlobalSystemMediaTransportControlsSession sender,TimelinePropertiesChangedEventArgs args){RaiseChanged();}
    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,SessionsChangedEventArgs args){Interlocked.Increment(ref mediaGeneration);sessionDirty=true;nativeLease.Request();retryAfter=DateTime.MinValue;RaiseChanged();}
    void BindSession(GlobalSystemMediaTransportControlsSession value){if(Object.ReferenceEquals(session,value))return;if(session!=null){try{if(hasPlaybackEvent)session.PlaybackInfoChanged-=PlaybackChanged;}catch{}try{if(hasPropertiesEvent)session.MediaPropertiesChanged-=PropertiesChanged;}catch{}try{if(hasTimelineEvent)session.TimelinePropertiesChanged-=TimelineChanged;}catch{}}hasPlaybackEvent=hasPropertiesEvent=hasTimelineEvent=false;session=value;cachedProperties=null;metadataDirty=true;if(session!=null){try{session.PlaybackInfoChanged+=PlaybackChanged;hasPlaybackEvent=true;session.MediaPropertiesChanged+=PropertiesChanged;hasPropertiesEvent=true;session.TimelinePropertiesChanged+=TimelineChanged;hasTimelineEvent=true;}catch{}}}
    static string AssetKey(Snapshot s){return s.SourceId+"\n"+s.Title+"\n"+s.Artist;}
    async Task<byte[]> ReadArtwork(GlobalSystemMediaTransportControlsSessionMediaProperties properties){if(properties==null||properties.Thumbnail==null)return null;using(var stream=await Await(properties.Thumbnail.OpenReadAsync()).ConfigureAwait(false)){if(stream.Size==0||stream.Size>4194304)return null;using(var reader=new DataReader(stream.GetInputStreamAt(0))){uint n=(uint)stream.Size;await Await(reader.LoadAsync(n)).ConfigureAwait(false);var bytes=new byte[n];reader.ReadBytes(bytes);return bytes;}}}
    public static bool IsNetease(string id) {
      return String.Equals(id,"cloudmusic",StringComparison.OrdinalIgnoreCase) || String.Equals(id,"cloudmusic.exe",StringComparison.OrdinalIgnoreCase) || String.Equals(id,@"C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe",StringComparison.OrdinalIgnoreCase);
    }
    async Task<T> Await<T>(IAsyncOperation<T> operation) {
      var completion=new TaskCompletionSource<T>();
      operation.Completed=(o,status)=> { try { completion.TrySetResult(o.GetResults()); } catch(Exception e) { completion.TrySetException(e); } };
      using(stopped.Token.Register(()=> { try { operation.Cancel(); } catch {} })) {
        if(await Task.WhenAny(completion.Task,Task.Delay(3500,stopped.Token))!=completion.Task) { operation.Cancel(); stopped.Token.ThrowIfCancellationRequested(); throw new TimeoutException("系统媒体查询超时"); }
        return await completion.Task;
      }
    }
    static void ObserveLateFault(Task<Snapshot> pending) {
      pending.ContinueWith(t=> { var ignored=t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    public async Task<Snapshot> ReadAsync() {
      if(disposed) return new Snapshot { Detail="媒体源已关闭" };
      var result=new Snapshot();
      if(clock()<retryAfter) { result.Detail="系统媒体重连等待中"; return result; }
      try {
        var pending=provider!=null ? provider() : ReadWindowsAsync();
        var cancelled=new TaskCompletionSource<bool>();
        using(stopped.Token.Register(()=>cancelled.TrySetResult(true))) {
          if(await Task.WhenAny(pending,cancelled.Task)!=pending) {
            // Observe a later provider fault even after the caller has stopped waiting.
            ObserveLateFault(pending);
            return new Snapshot { Detail="媒体源已关闭" };
          }
        }
        result=await pending.ConfigureAwait(false);if(artworkProvider!=null&&!String.IsNullOrEmpty(result.SourceId)){string key=AssetKey(result);result.Cover=assets.Get(key);assets.Begin(key,artworkProvider);}
        if(disposed) return new Snapshot { Detail="媒体源已关闭" };
        if(String.IsNullOrEmpty(result.SourceId)) { BindSession(null);retryAfter=clock().AddSeconds(10);return result; }
        failures=0; retryAfter=DateTime.MinValue; return result;
      } catch(Exception) {
        ResetNativeManager();nativeLease.Request();
        if(disposed) return new Snapshot { Detail="媒体源已关闭" };
        failures=Math.Min(failures+1,5); retryAfter=clock().AddSeconds(Math.Min(60,5*(1 << (failures-1))));
        result=new Snapshot { Detail="系统媒体重连中" };
      }
      return result;
    }
    async Task<Snapshot> ReadWindowsAsync(){await nativeReads.WaitAsync(stopped.Token);try{return await ReadWindowsSerialAsync();}finally{nativeReads.Release();}}
    async Task<Snapshot> ReadWindowsSerialAsync() {
        var result=new Snapshot();
        if(manager!=null&&nativeLease.Due(NativeNow,clock()<fastUntil))ResetNativeManager();if(manager==null){nativeLease.BeginAcquire();manager=await Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()).ConfigureAwait(false);if(disposed){manager=null;return result;}manager.SessionsChanged+=SessionsChanged;hasSessionsEvent=true;manager.CurrentSessionChanged+=CurrentSessionChangedHandler;hasCurrentSessionEvent=true;nativeLease.Acquired(NativeNow);Interlocked.Increment(ref nativeManagerRefreshes);}if(sessionDirty){BindSession(null);sessionDirty=false;}
        if(disposed) { manager=null; return result; }
        if(session==null){var selected=manager.GetCurrentSession();if(selected==null||!IsNetease(selected.SourceAppUserModelId))selected=manager.GetSessions().FirstOrDefault(s=>IsNetease(s.SourceAppUserModelId));BindSession(selected);}
        if(session==null) { result.Detail="未发现可识别的网易云系统媒体会话"; return result; }
        var current=session;
        result.SourceId=current.SourceAppUserModelId;
        var props=cachedProperties;if(props==null||metadataDirty||(clock()>=fastUntil&&(clock()-propertiesAt).TotalSeconds>=5)){metadataDirty=false;props=await Await(current.TryGetMediaPropertiesAsync()).ConfigureAwait(false);cachedProperties=props;propertiesAt=clock();}
        if(disposed) return new Snapshot { Detail="媒体源已关闭" };
        result.Title=String.IsNullOrWhiteSpace(props.Title)?"网易云未提供歌名":props.Title;
        result.Artist=props.Artist??"";result.Album=props.AlbumTitle??"";if(props.Genres!=null)foreach(var genre in props.Genres){var match=System.Text.RegularExpressions.Regex.Match(genre??"",@"^NCM-(\d{1,20})$");if(match.Success){result.TrackId=match.Groups[1].Value;break;}}
        var info=current.GetPlaybackInfo();result.ObservedTicks=System.Diagnostics.Stopwatch.GetTimestamp();result.PlaybackObservedAt=DateTimeOffset.UtcNow; var controls=info.Controls;
        result.Playing=info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        result.CanToggle=controls.IsPlayPauseToggleEnabled; result.CanPrevious=controls.IsPreviousEnabled; result.CanNext=controls.IsNextEnabled;result.CanSeek=controls.IsPlaybackPositionEnabled;
        try{var timeline=current.GetTimelineProperties();double duration=(timeline.EndTime-timeline.StartTime).TotalSeconds,position=(timeline.Position-timeline.StartTime).TotalSeconds;if(duration>0&&duration<=86400)result.DurationSeconds=duration;if(position>=0&&position<=86400&&timeline.LastUpdatedTime.Year>=2000&&(!result.DurationSeconds.HasValue||position<=result.DurationSeconds.Value)){result.PositionSeconds=position;result.PositionTimestamp=timeline.LastUpdatedTime;}}catch{} // Missing native timelines remain unknown; no invented progress.
        result.Detail=result.Playing?"正在播放":"已暂停";
        string artworkKey=AssetKey(result);result.Cover=assets.Get(artworkKey);assets.Begin(artworkKey,()=>ReadArtwork(props));
      return result;
    }
    static void ObserveSeekFault(Task pending){pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
    async Task<T> AwaitManaged<T>(Task<T> pending){using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopped.Token)){var delay=Task.Delay(3500,timeout.Token);if(await Task.WhenAny(pending,delay).ConfigureAwait(false)!=pending){ObserveSeekFault(pending);stopped.Token.ThrowIfCancellationRequested();throw new TimeoutException();}timeout.Cancel();return await pending.ConfigureAwait(false);}}
    public static string TrackKey(Snapshot s){return s==null?"":(s.SourceId??"")+"\n"+(s.Title??"").Trim()+"\n"+(s.Artist??"").Trim()+"\n"+(s.DurationSeconds.HasValue?s.DurationSeconds.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture):"");}
    static bool ValidSeek(Snapshot s,double seconds,string expected){return s!=null&&IsNetease(s.SourceId)&&s.CanSeek&&s.DurationSeconds.HasValue&&s.DurationSeconds.Value>0&&s.DurationSeconds.Value<=86400&&!Double.IsNaN(seconds)&&!Double.IsInfinity(seconds)&&seconds>=0&&seconds<=s.DurationSeconds.Value&&TrackKey(s)==expected;}
    public async Task<bool> SeekAsync(double seconds,string expectedTrack){
      if(disposed||String.IsNullOrEmpty(expectedTrack)||Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0||seconds>86400||Interlocked.CompareExchange(ref controlBusy,1,0)!=0)return false;
      fastUntil=clock().AddSeconds(4);
      try{
        if(seekProvider!=null){if(provider==null)return false;var before=await AwaitManaged(provider()).ConfigureAwait(false);if(disposed||!ValidSeek(before,seconds,expectedTrack))return false;if(!await AwaitManaged(seekProvider(seconds)).ConfigureAwait(false)||disposed)return false;var observed=await AwaitManaged(provider()).ConfigureAwait(false);return ValidSeek(observed,seconds,expectedTrack)&&observed.PositionSeconds.HasValue&&Math.Abs(observed.PositionSeconds.Value-seconds)<=2.5;}
        var target=session;if(target==null||!IsNetease(target.SourceAppUserModelId)||!target.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)return false;
        int generation=Volatile.Read(ref mediaGeneration);var props=await Await(target.TryGetMediaPropertiesAsync()).ConfigureAwait(false);var timeline=target.GetTimelineProperties();double duration=(timeline.EndTime-timeline.StartTime).TotalSeconds;
        var beforeNative=new Snapshot{SourceId=target.SourceAppUserModelId,Title=String.IsNullOrWhiteSpace(props.Title)?"网易云未提供歌名":props.Title,Artist=props.Artist??"",DurationSeconds=duration,CanSeek=true};
        if(!ValidSeek(beforeNative,seconds,expectedTrack)||(!Object.ReferenceEquals(target,session)||generation!=Volatile.Read(ref mediaGeneration)))return false;
        long requested=checked(timeline.StartTime.Ticks+TimeSpan.FromSeconds(seconds).Ticks);
        if(!await Await(target.TryChangePlaybackPositionAsync(requested)).ConfigureAwait(false))return false;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(!disposed&&watch.ElapsedMilliseconds<2400&&Object.ReferenceEquals(target,session)&&generation==Volatile.Read(ref mediaGeneration)){
          var now=target.GetTimelineProperties();if(Math.Abs((now.Position-now.StartTime).TotalSeconds-seconds)<=2.5)return true;
          await Task.Delay(80,stopped.Token).ConfigureAwait(false);
        }
        return false;
      }catch{return false;}finally{Interlocked.Exchange(ref controlBusy,0);RaiseChanged();}
    }

    public Task<bool> ControlAsync(string action){return ControlAsync(action,System.Diagnostics.Stopwatch.GetTimestamp());}
    public async Task<bool> ControlAsync(string action,long click){if(action!="toggle"&&action!="previous"&&action!="next")return false;if(disposed||Interlocked.CompareExchange(ref controlBusy,1,0)!=0)return false;var trace=new ControlTrace{Action=action,Click=click};LastControlTrace=trace;fastUntil=clock().AddSeconds(3);bool accepted=false;try{trace.Dispatch=System.Diagnostics.Stopwatch.GetTimestamp();if(controlProvider!=null){accepted=await controlProvider(action).ConfigureAwait(false);}else{var target=session;if(target==null||!IsNetease(target.SourceAppUserModelId)){trace.Outcome="unavailable";return false;}var c=target.GetPlaybackInfo().Controls;if(action=="toggle"&&c.IsPlayPauseToggleEnabled)accepted=await Await(target.TryTogglePlayPauseAsync()).ConfigureAwait(false);else if(action=="previous"&&c.IsPreviousEnabled)accepted=await Await(target.TrySkipPreviousAsync()).ConfigureAwait(false);else if(action=="next"&&c.IsNextEnabled)accepted=await Await(target.TrySkipNextAsync()).ConfigureAwait(false);}trace.Outcome=accepted?"accepted":"rejected";return accepted;}catch(TimeoutException){trace.Outcome="unconfirmed-timeout";return false;}catch{trace.Outcome="unconfirmed-error";return false;}finally{trace.Acknowledged=System.Diagnostics.Stopwatch.GetTimestamp();nativeLease.Request();Interlocked.Exchange(ref controlBusy,0);RaiseChanged();}}
  }
}
