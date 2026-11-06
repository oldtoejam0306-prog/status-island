// Read-only, bounded SMTC diagnostic. No process/token/account/notification query.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
public static class MediaContextDiagnostic {
  static bool Recognized(string id) {
    return String.Equals(id,"cloudmusic",StringComparison.OrdinalIgnoreCase) || String.Equals(id,"cloudmusic.exe",StringComparison.OrdinalIgnoreCase) || String.Equals(id,@"C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe",StringComparison.OrdinalIgnoreCase);
  }
  static string Safe(string value) {
    if(String.IsNullOrEmpty(value))return "";
    var text=value.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
    text=Regex.Replace(text,@"(?i)(?:[a-z]:\\|\\\\)[^\r\n]*","[path]");
    text=Regex.Replace(text,@"(?i)https?://\S+|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}","[address]");
    return text.Length>160?text.Substring(0,160):text;
  }
  static Task<T> Read<T>(IAsyncOperation<T> operation) {
    var done=new TaskCompletionSource<T>();
    operation.Completed=(o,status)=> {try {done.TrySetResult(o.GetResults());}catch(Exception e){done.TrySetException(e);}};
    return Finish(operation,done.Task);
  }
  static void ObserveLateFault(Task pending) { pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted); }
  static async Task<T> Finish<T>(IAsyncOperation<T> operation,Task<T> pending) {
    if(await Task.WhenAny(pending,Task.Delay(2200))!=pending) {
      try{operation.Cancel();}catch{}
      ObserveLateFault(pending);
      throw new TimeoutException();
    }
    return await pending;
  }
  static async Task<int> Inspect() {
    var manager=await Read(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
    var sessions=manager.GetSessions();int recognized=0;
    Console.WriteLine("SMTC_SESSION_COUNT="+sessions.Count());
    foreach(var session in sessions) {
      if(!Recognized(session.SourceAppUserModelId))continue;
      recognized++;Console.WriteLine("SOURCE=NETEASE_RECOGNIZED");
      var info=session.GetPlaybackInfo();Console.WriteLine("PLAYBACK="+info.PlaybackStatus);
      var timeline=session.GetTimelineProperties();Console.WriteLine("START_SECONDS="+timeline.StartTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));Console.WriteLine("END_SECONDS="+timeline.EndTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));Console.WriteLine("POSITION_SECONDS="+timeline.Position.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));Console.WriteLine("LAST_UPDATED_UTC="+timeline.LastUpdatedTime.ToString("o"));
      // Player-public title and artist only: no path, source ID, artwork or account fields.
      var metadata=await Read(session.TryGetMediaPropertiesAsync());
      Console.WriteLine("SUBTITLE_PRESENT="+(!String.IsNullOrEmpty(metadata.Subtitle)));Console.WriteLine("COVER_PRESENT="+(metadata.Thumbnail!=null));
      break; // Bounded: inspect at most one recognized player.
    }
    Console.WriteLine("NETEASE_FOUND="+(recognized>0?"YES":"NO"));
    return 0;
  }
  [MTAThread] public static int Main() {
    Console.WriteLine("READ_ONLY_SMTC_DIAGNOSTIC_V1");
    Console.WriteLine("UTC="+DateTime.UtcNow.ToString("o"));
    try {var task=Inspect();if(!task.Wait(5500)){Console.WriteLine("RESULT=TIMEOUT");return 2;}return task.Result;}
    catch(Exception e) {var a=e as AggregateException;var cause=a!=null?a.GetBaseException():e;Console.WriteLine("RESULT=FAILED HRESULT=0x"+cause.HResult.ToString("X8"));return 1;}
  }
}
