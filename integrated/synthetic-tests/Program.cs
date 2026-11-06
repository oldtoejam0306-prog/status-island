using System;
using System.Threading.Tasks;
using IslandMedia;
class Program {
 static void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
 static void Main() {
  var now=new DateTime(2026,1,1); int calls=0, mode=0;
  var source=new NeteaseSource(()=>now,()=> {calls++; if(mode==0) throw new Exception("fixture"); return Task.FromResult(new Snapshot { SourceId=mode==1?"":"cloudmusic", Title="A" });});
  foreach(int delay in new[]{5,10,20,40,60,60}) { source.ReadAsync().GetAwaiter().GetResult(); int n=calls; now=now.AddSeconds(delay-1); source.ReadAsync().GetAwaiter().GetResult(); Check(calls==n,"failure gate "+delay+"s"); now=now.AddSeconds(1); }
  mode=1; source.ReadAsync().GetAwaiter().GetResult(); int count=calls; now=now.AddSeconds(9); source.ReadAsync().GetAwaiter().GetResult(); Check(calls==count,"empty session gate"); now=now.AddSeconds(1); mode=2; Check(source.ReadAsync().GetAwaiter().GetResult().Title=="A","empty session requery");
  mode=0; source.ReadAsync().GetAwaiter().GetResult(); count=calls; now=now.AddSeconds(4); source.ReadAsync().GetAwaiter().GetResult(); Check(calls==count,"success resets backoff"); now=now.AddSeconds(1); source.ReadAsync().GetAwaiter().GetResult(); Check(calls==count+1,"reset retries after 5s"); source.Dispose();
  var pending=new TaskCompletionSource<Snapshot>(); var inflight=new NeteaseSource(()=>now,()=>pending.Task); var read=inflight.ReadAsync(); inflight.Dispose(); Check(read.Wait(1000) && read.Result.Detail=="媒体源已关闭","dispose releases in-flight read"); pending.SetException(new Exception("late fault")); Check(inflight.ReadAsync().Result.Detail=="媒体源已关闭","no read after dispose");
  Check(NeteaseSource.IsNetease("cloudmusic.exe") && !NeteaseSource.IsNetease("fake-cloudmusic.exe"),"source identity");
 }
}
