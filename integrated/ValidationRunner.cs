using System;
using System.IO;
using System.Reflection;
using IslandIntegrated;
using IslandMedia;
using System.Threading.Tasks;
using System.Threading;
public class ValidationRunner {
  public static int Main(string[] args) {
    if(args.Length==1 && args[0]=="--normal-update-state-backup") return NormalUpdateStateBackupChecks();
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
  static int NormalUpdateStateBackupChecks() {
    string root=Path.Combine(Path.GetTempPath(),"SidebarNormalUpdateState-"+Guid.NewGuid().ToString("N"));
    try {
      string state=Path.Combine(root,"state");Directory.CreateDirectory(state);
      File.WriteAllText(Path.Combine(root,"FILE-MANIFEST.json"),"{}",new System.Text.UTF8Encoding(false));
      File.WriteAllText(Path.Combine(state,"layout.xml"),"<layout/>",new System.Text.UTF8Encoding(false));
      File.WriteAllText(Path.Combine(state,"layout.xml.bak"),"<layout backup/>",new System.Text.UTF8Encoding(false));
      File.WriteAllText(Path.Combine(state,"pos.json"),"{\"x\":1}",new System.Text.UTF8Encoding(false));
      string monitor="{\"unread\":\""+new string('\u4E2D',524288)+"\"}";
      File.WriteAllText(Path.Combine(state,"aihot-monitor.json"),monitor,new System.Text.UTF8Encoding(false));
      string monitorPath=Path.Combine(state,"aihot-monitor.json");
      if(new FileInfo(monitorPath).Length<=1048576||new FileInfo(monitorPath).Length>=33554432)throw new Exception("AIHOT fixture byte size is outside its special limit");
      string nonce=Guid.NewGuid().ToString("N");BackupState(root,nonce);
      string backup=Path.Combine(state,"update","backups",nonce);
      foreach(string name in new[]{"layout.xml","layout.xml.bak","pos.json","aihot-monitor.json"}) {
        string source=Path.Combine(state,name),copy=Path.Combine(backup,name);
        if(!File.Exists(copy)||HashFile(source)!=HashFile(copy))throw new Exception("Missing or mismatched normal-update backup: "+name);
      }

      string oversized=Path.Combine(Path.GetTempPath(),"SidebarNormalUpdateOversize-"+Guid.NewGuid().ToString("N"));
      try {
        string oversizedState=Path.Combine(oversized,"state");Directory.CreateDirectory(oversizedState);
        File.WriteAllText(Path.Combine(oversized,"FILE-MANIFEST.json"),"{}",new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(oversizedState,"layout.xml"),"<layout/>",new System.Text.UTF8Encoding(false));
        using(var stream=new FileStream(Path.Combine(oversizedState,"aihot-monitor.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.SetLength(33554433);
        bool rejected=false;try{BackupState(oversized,Guid.NewGuid().ToString("N"));}catch(TargetInvocationException e){rejected=e.InnerException is IOException&&e.InnerException.Message=="State backup too large";}
        if(!rejected||Directory.Exists(Path.Combine(oversizedState,"update","backups")))throw new Exception("Oversized AIHOT state was not rejected before backup creation");
      } finally {if(Directory.Exists(oversized))Directory.Delete(oversized,true);}
      string oversizedLayout=Path.Combine(Path.GetTempPath(),"SidebarNormalUpdateLayoutLimit-"+Guid.NewGuid().ToString("N"));
      try {
        string oversizedState=Path.Combine(oversizedLayout,"state");Directory.CreateDirectory(oversizedState);
        File.WriteAllText(Path.Combine(oversizedLayout,"FILE-MANIFEST.json"),"{}",new System.Text.UTF8Encoding(false));
        using(var stream=new FileStream(Path.Combine(oversizedState,"layout.xml"),FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.SetLength(1048577);
        bool rejected=false;try{BackupState(oversizedLayout,Guid.NewGuid().ToString("N"));}catch(TargetInvocationException e){rejected=e.InnerException is IOException&&e.InnerException.Message=="State backup too large";}
        if(!rejected||Directory.Exists(Path.Combine(oversizedState,"update","backups")))throw new Exception("Oversized layout state did not retain the 1 MiB limit");
      } finally {if(Directory.Exists(oversizedLayout))Directory.Delete(oversizedLayout,true);}
      Console.WriteLine("Normal update state backup checks passed (four files, AIHOT 32 MiB limit, other state 1 MiB limit, and preflight failure consistency).");return 0;
    } catch(Exception ex) {Console.WriteLine("Normal update state backup checks failed: "+ex);return 1;}
    finally {if(Directory.Exists(root))Directory.Delete(root,true);}
  }
  static void BackupState(string root,string nonce) {
    var handoff=new NormalUpdateHandoff(root);
    var method=typeof(NormalUpdateHandoff).GetMethod("Backup",BindingFlags.Instance|BindingFlags.NonPublic);
    if(method==null)throw new MissingMethodException("NormalUpdateHandoff.Backup");
    method.Invoke(handoff,new object[]{nonce});
  }
  static string HashFile(string path) {
    using(var stream=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
  }
}
