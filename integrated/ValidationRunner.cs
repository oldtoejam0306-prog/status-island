using System;
using IslandIntegrated;
using IslandMedia;
using System.Threading.Tasks;
using System.Threading;
public class ValidationRunner {
  public static int Main(string[] args) {
    if(args.Length==3 && args[0]=="--render-check") {App.RenderCheck(args[1],args[2]);return 0;}
    if(args.Length==1 && args[0]=="--media-read-only") {
      var pending=new NeteaseSource().ReadAsync();
      if(!pending.Wait(5000)) { Console.WriteLine("Read-only media query timed out"); return 2; }
      Console.WriteLine(pending.Result.Detail);
      Console.WriteLine("Recognized Netease session: "+(!String.IsNullOrEmpty(pending.Result.SourceId)));
      return 0;
    }
    int result=App.SelfTest(); Console.WriteLine("Controller tests: "+result); if(result!=0)return result;
    var now=new DateTime(2026,1,1); int calls=0; int mode=0;
    var source=new NeteaseSource(()=>now,()=> { calls++; if(mode==0)throw new Exception("fixture"); return Task.FromResult(new Snapshot { SourceId=mode==1?"":"cloudmusic", Title=mode==2?"A":"B" }); });
    source.ReadAsync().GetAwaiter().GetResult(); source.ReadAsync().GetAwaiter().GetResult(); if(calls!=1)return 21;
    now=now.AddSeconds(5); mode=1; source.ReadAsync().GetAwaiter().GetResult(); now=now.AddSeconds(9); source.ReadAsync().GetAwaiter().GetResult(); if(calls!=2)return 22;
    now=now.AddSeconds(1); mode=2; if(source.ReadAsync().GetAwaiter().GetResult().Title!="A")return 23;
    mode=3; if(source.ReadAsync().GetAwaiter().GetResult().Title!="B")return 24;
    source.Dispose(); source.ReadAsync().GetAwaiter().GetResult(); if(calls!=4)return 25;
    now=new DateTime(2026,1,1); calls=0;
    var failing=new NeteaseSource(()=>now,()=>{calls++; throw new Exception("fixture");});
    foreach(int delay in new[]{5,10,20,40,60,60}) {
      failing.ReadAsync().GetAwaiter().GetResult(); int count=calls;
      now=now.AddSeconds(delay-1); failing.ReadAsync().GetAwaiter().GetResult(); if(calls!=count)return 26;
      now=now.AddSeconds(1);
    }
    var pendingFixture=new TaskCompletionSource<Snapshot>(); var inflight=new NeteaseSource(()=>now,()=>pendingFixture.Task);
    var running=inflight.ReadAsync(); inflight.Dispose(); if(!running.Wait(1000) || running.Result.SourceId!="")return 27;
    pendingFixture.SetResult(new Snapshot{SourceId="cloudmusic"});
    int afterCloseCalls=0;
    var wired=new NeteaseSource(()=>now,()=>{afterCloseCalls++;return Task.FromResult(new Snapshot());});
    if(!App.TestShutdown(wired) || afterCloseCalls!=0)return 31;
    string mutexName="Local\\StatusIslandTest-"+Guid.NewGuid();
    using(var first=new Mutex(false,mutexName)) {
      if(!first.WaitOne(0))return 28;
      bool blocked=Task.Run(()=>{using(var second=Mutex.OpenExisting(mutexName)) return !second.WaitOne(0);}).Result;
      if(!blocked)return 29;
      first.ReleaseMutex();
      bool acquired=Task.Run(()=>{using(var second=Mutex.OpenExisting(mutexName)) { bool ok=second.WaitOne(0); if(ok)second.ReleaseMutex(); return ok; }}).Result;
      if(!acquired)return 30;
    }
    Console.WriteLine("Media fixture checks: 13 passed"); return 0;
  }
}
