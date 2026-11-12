using System;using System.IO;using System.Diagnostics;using System.Threading.Tasks;using System.Collections.Generic;using System.Text.RegularExpressions;using System.Security.Cryptography;using System.Web.Script.Serialization;
namespace IslandIntegrated {
 // App-owned cooperative lifecycle only. Never starts code, changes policies,
 // sends window messages, kills processes, or touches another application's UI.
 public sealed class NormalUpdateHandoff {
  readonly string root,state,manifest,candidate;readonly Func<DateTimeOffset> clock;readonly Func<long> elapsed;string last="",preparedNonce,preparedNext,preparedHash;DateTimeOffset preparedAt;Task<Request> pending;long nextPoll;bool closing;Request closingRequest;
  sealed class Request {public string Operation,Nonce,Next,NextHash,Error;}
  sealed class BackupPlan {public string Source,Target,Temporary,Hash;public long Length,Limit;public bool Committed;}
  static string Hash(string file){using(var f=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
  static void Safe(string path){var full=Path.GetFullPath(path);if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected update file");for(var d=new DirectoryInfo(Directory.Exists(full)?full:Path.GetDirectoryName(full));d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected update directory");}
  static string Read(string file,int max){Safe(file);var info=new FileInfo(file);if(!info.Exists||info.Length>max)throw new IOException("Missing/oversized update data");return File.ReadAllText(file);}
  static Dictionary<string,object> Json(string s){return new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=12}.DeserializeObject(s) as Dictionary<string,object>;}
  static string Str(Dictionary<string,object>d,string key){object o;return d!=null&&d.TryGetValue(key,out o)&&o is string?(string)o:"";}
  public NormalUpdateHandoff(string releaseRoot,Func<DateTimeOffset> now=null,Func<long> ticks=null){root=Path.GetFullPath(releaseRoot);Safe(root);candidate=Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));state=Path.Combine(root,"state","update");manifest=Hash(Path.Combine(root,"FILE-MANIFEST.json"));clock=now??(()=>DateTimeOffset.UtcNow);elapsed=ticks??(()=>PreviewClock.NowMs);}
  public bool Closing {get{return closing;}}
  // Called only by the close helper after an observed FormClosingEventArgs.Cancel=true.
  public void NotifyCloseCancelled(string reason){if(!closing||closingRequest==null)return;var request=closingRequest;closingRequest=null;closing=false;preparedNonce=preparedNext=preparedHash=null;Reply(request,"failed",String.IsNullOrWhiteSpace(reason)?"Normal close was cancelled":reason);}
  public void PublishOwner(){Safe(state);Directory.CreateDirectory(state);Safe(state);Write(Path.Combine(state,"owner.json"),new{schema=1,processId=Process.GetCurrentProcess().Id,processBirthUtc=Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("o"),candidate,manifestSha256=manifest,protocol="sidebar.normal-update.v1",utc=clock().ToString("o")});}
  static void Write(string path,object value){Safe(path);string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(value),new System.Text.UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
  Request Parse(string text){var r=new Request();try{if(Hash(Path.Combine(root,"FILE-MANIFEST.json"))!=manifest)throw new IOException("Running manifest changed");var d=Json(text);r.Operation=Str(d,"operation");r.Nonce=Str(d,"nonce");r.Next=Str(d,"nextCandidate");r.NextHash=Str(d,"nextManifestSha256");Guid nonce;if(!Guid.TryParseExact(r.Nonce,"N",out nonce))throw new IOException("Invalid nonce");if(Str(d,"currentManifestSha256")!=manifest)throw new IOException("Wrong running manifest");if(r.Operation!="prepare-update"&&r.Operation!="commit-update")throw new IOException("Unsupported lifecycle operation");DateTimeOffset created;if(!DateTimeOffset.TryParse(Str(d,"utc"),out created)||(clock()-created).TotalSeconds>60||(created-clock()).TotalSeconds>5)throw new IOException("Expired lifecycle request");if(!Regex.IsMatch(r.Next??"","^[a-z0-9][a-z0-9-]{0,79}$")||!Regex.IsMatch(r.NextHash??"","^[0-9a-f]{64}$")||r.Next==candidate)throw new IOException("Invalid next identity");string releases=Path.GetDirectoryName(root),next=Path.Combine(releases,r.Next);string file=Path.Combine(next,"FILE-MANIFEST.json");string raw=Read(file,1048576);if(Hash(file)!=r.NextHash)throw new IOException("Next manifest mismatch");var nextData=Json(raw);object all;if(!nextData.TryGetValue("files",out all)||!(all is object[])||((object[])all).Length<1||((object[])all).Length>256)throw new IOException("Invalid source list");foreach(var entry in (object[])all){var item=entry as Dictionary<string,object>;string relative=Str(item,"path"),expected=Str(item,"sha256");if(Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new IOException("Invalid next file path");string target=Path.GetFullPath(Path.Combine(next,relative));if(!target.StartsWith(next+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Next path escapes release");Safe(target);var info=new FileInfo(target);if(!info.Exists||info.Length>8388608||Hash(target)!=expected)throw new IOException("Next source failed integrity");}return r;}catch(Exception e){r.Error=e.Message;return r;}}
  void Reply(Request r,string status,string reason){if(!Regex.IsMatch(r.Nonce??"","^[0-9a-f]{32}$"))return;var process=Process.GetCurrentProcess();string receipts=Path.Combine(state,"receipts");Safe(receipts);Directory.CreateDirectory(receipts);Write(Path.Combine(receipts,r.Nonce+".json"),new{schema=1,utc=clock().ToString("o"),nonce=r.Nonce,status,reason,candidate,manifestSha256=manifest,nextCandidate=r.Next,nextManifestSha256=r.NextHash,processId=process.Id,processBirthUtc=process.StartTime.ToUniversalTime().ToString("o")});}
  string LayoutHash(){string file=Path.Combine(root,"state","layout.xml");return File.Exists(file)?Hash(file):"absent";}
  void Backup(string nonce){
   string backup=Path.Combine(state,"backups",nonce);Safe(backup);
   var plans=new List<BackupPlan>();
   foreach(var name in new[]{"layout.xml","layout.xml.bak","pos.json","aihot-monitor.json"}){
    string file=Path.Combine(root,"state",name);Safe(file);
    if(Directory.Exists(file))throw new IOException("State backup source is not a file");
    if(!File.Exists(file))continue;
    long limit=name=="aihot-monitor.json"?33554432L:1048576L,length=new FileInfo(file).Length;
    if(length>limit)throw new IOException("State backup too large");
    string to=Path.Combine(backup,name);Safe(to);
    if(File.Exists(to)||Directory.Exists(to))throw new IOException("State backup destination already exists");
    string before=Hash(file);
    if(new FileInfo(file).Length>limit||new FileInfo(file).Length!=length)throw new IOException("State changed while backing up");
    plans.Add(new BackupPlan{Source=file,Target=to,Hash=before,Length=length,Limit=limit});
   }
   bool createdDirectory=!Directory.Exists(backup),succeeded=false;
   Directory.CreateDirectory(backup);Safe(backup);
   try{
    foreach(var plan in plans){
     plan.Temporary=Path.Combine(backup,".handoff-"+Guid.NewGuid().ToString("N")+".tmp");Safe(plan.Temporary);
     File.Copy(plan.Source,plan.Temporary,false);
     if(new FileInfo(plan.Source).Length>plan.Limit||new FileInfo(plan.Temporary).Length>plan.Limit||new FileInfo(plan.Source).Length!=plan.Length||new FileInfo(plan.Temporary).Length!=plan.Length||Hash(plan.Source)!=plan.Hash||Hash(plan.Temporary)!=plan.Hash)throw new IOException("State changed while backing up");
    }
    foreach(var plan in plans)if(new FileInfo(plan.Source).Length>plan.Limit||new FileInfo(plan.Source).Length!=plan.Length||Hash(plan.Source)!=plan.Hash)throw new IOException("State changed while backing up");
    foreach(var plan in plans){
     if(File.Exists(plan.Target)||Directory.Exists(plan.Target))throw new IOException("State backup destination changed");
     File.Move(plan.Temporary,plan.Target);plan.Temporary=null;plan.Committed=true;
     if(new FileInfo(plan.Target).Length>plan.Limit||new FileInfo(plan.Target).Length!=plan.Length||Hash(plan.Target)!=plan.Hash)throw new IOException("State backup integrity failed");
    }
    succeeded=true;
   }catch{
    foreach(var plan in plans)if(plan.Committed&&File.Exists(plan.Target)&&Hash(plan.Target)==plan.Hash)File.Delete(plan.Target);
    throw;
   }finally{
    foreach(var plan in plans)if(plan.Temporary!=null&&File.Exists(plan.Temporary))File.Delete(plan.Temporary);
    if(!succeeded&&createdDirectory&&Directory.Exists(backup)&&Directory.GetFileSystemEntries(backup).Length==0)Directory.Delete(backup,false);
   }
  }
  public void Tick(bool busy,Action saveLayout,Action normalClose){if(closing)return;if(pending==null){long now=elapsed();if(now<nextPoll)return;nextPoll=now+500;string file=Path.Combine(state,"request.json");if(!File.Exists(file))return;string raw;try{raw=Read(file,4096);}catch{return;}if(raw==last)return;last=raw;pending=Task.Run(()=>Parse(raw));return;}if(!pending.IsCompleted)return;Request r;try{r=pending.Result;}catch{pending=null;return;}pending=null;if(r.Error!=null){Reply(r,"rejected",r.Error);return;}if(busy){Reply(r,"busy","Editing, dragging or modal UI is active; nothing closed");return;}
   try{if(r.Operation=="prepare-update"){saveLayout();Backup(r.Nonce);preparedNonce=r.Nonce;preparedNext=r.NextHash;preparedHash=LayoutHash();preparedAt=clock();Reply(r,"prepared","Layout saved and backed up; waiting for commit");return;}if(r.Nonce!=preparedNonce||r.NextHash!=preparedNext||(clock()-preparedAt).TotalSeconds>60){Reply(r,"rejected","No matching fresh prepared handoff");return;}if(LayoutHash()!=preparedHash){Reply(r,"conflict","Layout changed since prepare; current preview retained");return;}closingRequest=r;closing=true;Reply(r,"closing","Normal application close requested; process exit not yet confirmed");normalClose();}catch(Exception e){closing=false;closingRequest=null;Reply(r,"failed",e.Message);}}
 }
}
