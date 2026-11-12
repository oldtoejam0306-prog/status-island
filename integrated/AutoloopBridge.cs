using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IslandIntegrated {
 public sealed class AutoloopOption { public string Id {get;set;} public string Label {get;set;} public AutoloopOption(){Id="";Label="";} }
 public sealed class AutoloopSnapshot {
  public string State {get;set;} public string Summary {get;set;} public string TaskTitle {get;set;} public string CardKind {get;set;} public string QuestionId {get;set;} public string QuestionText {get;set;} public string CardFingerprint {get;set;} public AutoloopOption[] Options {get;set;} public int PendingCount {get;set;} public DateTimeOffset? ObservedAt {get;set;}
  public AutoloopSnapshot(){State="offline";Summary="Autoloop 暂不可用";TaskTitle=CardKind=QuestionId=QuestionText=CardFingerprint="";Options=new AutoloopOption[0];}
 }
 public sealed class AutoloopActionResult { public string State {get;set;} public string Message {get;set;} public AutoloopActionResult(){State="unknown";Message="结果未知，请刷新查看";} }
 public sealed class AutoloopBridge : IDisposable {
  private sealed class Settings { public bool Enabled; public bool Invalid; public string Host,Account,KeyPath,Node,Script; }
  private readonly Settings settings;
  private readonly bool configured;
  private readonly Func<string[],CancellationToken,Task<string>> invoke;
  public AutoloopBridge():this(ReadSettings()){}
  public AutoloopBridge(string sshKeyPath):this(ReadSettings()){if(!String.IsNullOrWhiteSpace(sshKeyPath)){settings.KeyPath=sshKeyPath;Validate(settings);configured=settings.Enabled&&!settings.Invalid;}}
  private AutoloopBridge(Settings value){settings=value;Validate(settings);configured=settings.Enabled&&!settings.Invalid;invoke=InvokeProcessAsync;}
  internal AutoloopBridge(Func<string[],CancellationToken,Task<string>> fixture){settings=new Settings{Enabled=true};configured=true;invoke=fixture;}
  private static Settings ReadSettings(){
   var s=new Settings();try{string home=Environment.GetEnvironmentVariable("STATUS_ISLAND_HOME");if(String.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StatusIsland");string file=Path.Combine(home,"config.json");if(!File.Exists(file)||new FileInfo(file).Length>65536)return s;var d=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=8}.DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;if(d==null)return new Settings{Invalid=true};object enabled;if(!d.TryGetValue("autoloopEnabled",out enabled)||!(enabled is bool))return new Settings{Invalid=true};s.Enabled=(bool)enabled;if(!s.Enabled)return s;s.Host=Setting(d,"autoloopHost");s.Account=Setting(d,"autoloopAccount");s.KeyPath=Setting(d,"autoloopKeyFile");s.Node=Setting(d,"autoloopNode");s.Script=Setting(d,"autoloopRemoteScript");return s;}catch{return new Settings{Invalid=true};}
  }
  private static string Setting(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?value as string:null;}
  private static void Validate(Settings s){if(s==null||!s.Enabled||s.Invalid)return;try{if(String.IsNullOrWhiteSpace(s.Host)||!System.Text.RegularExpressions.Regex.IsMatch(s.Host,"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$")||String.IsNullOrWhiteSpace(s.Account)||!System.Text.RegularExpressions.Regex.IsMatch(s.Account,"^[A-Za-z_][A-Za-z0-9_-]{0,31}$")||String.IsNullOrWhiteSpace(s.KeyPath)||!Path.IsPathRooted(s.KeyPath)||s.KeyPath.IndexOfAny(new char[]{'"','\r','\n'})>=0||!File.Exists(s.KeyPath)||String.IsNullOrWhiteSpace(s.Node)||!System.Text.RegularExpressions.Regex.IsMatch(s.Node,"^(/[A-Za-z0-9._-]+)+$")||String.IsNullOrWhiteSpace(s.Script)||!System.Text.RegularExpressions.Regex.IsMatch(s.Script,"^(/[A-Za-z0-9._-]+)+$"))s.Invalid=true;else s.KeyPath=Path.GetFullPath(s.KeyPath);}catch{s.Invalid=true;}}
  public void Dispose(){}
  private static Dictionary<string,object> Object(object x){var d=x as Dictionary<string,object>;if(d==null)throw new InvalidDataException();return d;}
  private static string Str(Dictionary<string,object> d,string k){object x;if(!d.TryGetValue(k,out x)||!(x is string))throw new InvalidDataException();return (string)x;}
  private static bool Fingerprint(string s){return s!=null && s.Length==64 && s.All(c => (c>='0'&&c<='9')||(c>='a'&&c<='f'));}
  private static Dictionary<string,object> Parse(string json){var ser=new JavaScriptSerializer();ser.MaxJsonLength=524288; return Object(ser.DeserializeObject(json));}
  public async Task<AutoloopSnapshot> ReadAsync(CancellationToken token){
   if(!configured)return new AutoloopSnapshot{State=settings!=null&&settings.Invalid?"unavailable":"offline",Summary=settings!=null&&settings.Invalid?"Autoloop 配置无效":"Autoloop 未配置"};
   try{
    var root=Parse(await invoke(new[]{"-Operation","snapshot"},token).ConfigureAwait(false));
    if(Str(root,"schema")!="sidebar.autoloop-bridge.v1" || !(root["online"] is bool) || !(bool)root["online"] || !(root["running"] is bool))throw new InvalidDataException();
    DateTimeOffset observed=DateTimeOffset.Parse(Str(root,"observedUtc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
    TimeSpan age=DateTimeOffset.UtcNow-observed;
    if(age>TimeSpan.FromMinutes(2)||age<TimeSpan.FromMinutes(-2))return new AutoloopSnapshot{State="stale",Summary="Autoloop 状态已过期",ObservedAt=observed};
    var cards=root["cards"] as object[]; int pending=Convert.ToInt32(root["pendingCount"],CultureInfo.InvariantCulture); if(cards==null||pending<0||pending>50||cards.Length!=pending)throw new InvalidDataException();
    var snap=new AutoloopSnapshot{State="connected",Summary=((bool)root["running"]?"运行中":"空闲")+" · "+pending+" 张待处理卡片",PendingCount=pending,ObservedAt=observed};
    Dictionary<string,object> selected=null; foreach(var c in cards){var card=Object(c);if(Str(card,"kind")=="ask"){selected=card;break;}} if(selected==null&&cards.Length>0)selected=Object(cards[0]);
    if(selected!=null){long id=Convert.ToInt64(selected["id"],CultureInfo.InvariantCulture);string fp=Str(selected,"fingerprint");if(id<1||id>9007199254740991L||!Fingerprint(fp)||Str(selected,"state")!="open")throw new InvalidDataException();
     snap.CardKind=Str(selected,"kind");snap.QuestionId=id.ToString(CultureInfo.InvariantCulture);snap.CardFingerprint=fp;snap.TaskTitle=Str(selected,"title");snap.QuestionText=Str(selected,"text");
     if(snap.CardKind=="ask"){var opts=selected["options"] as object[];if(opts==null||opts.Length>32)throw new InvalidDataException();var list=new List<AutoloopOption>();for(int i=0;i<opts.Length;i++){if(!(opts[i] is string))throw new InvalidDataException();list.Add(new AutoloopOption{Id=i.ToString(CultureInfo.InvariantCulture),Label=(string)opts[i]});}snap.Options=list.ToArray();}
    } return snap;
   }catch(OperationCanceledException){return new AutoloopSnapshot{Summary="Autoloop 查询已中断"};}catch{return new AutoloopSnapshot();}
  }
  public async Task<AutoloopActionResult> AnswerAsync(string questionId,string optionId,string cardFingerprint,CancellationToken token){
   if(!configured)return new AutoloopActionResult{State="unavailable",Message=settings!=null&&settings.Invalid?"Autoloop 配置无效":"Autoloop 未配置"};
   long id;int choice;if(!long.TryParse(questionId,NumberStyles.None,CultureInfo.InvariantCulture,out id)||id<1||id>9007199254740991L||!int.TryParse(optionId,NumberStyles.None,CultureInfo.InvariantCulture,out choice)||choice<0||!Fingerprint(cardFingerprint))return new AutoloopActionResult{State="rejected",Message="卡片或选项无效，请刷新"};
   try{var r=Parse(await invoke(new[]{"-Operation","answer","-CardId",id.ToString(CultureInfo.InvariantCulture),"-Choice",choice.ToString(CultureInfo.InvariantCulture),"-Fingerprint",cardFingerprint},token).ConfigureAwait(false));switch(Str(r,"status")){
    case "accepted":return new AutoloopActionResult{State="confirmed",Message="回答已提交"};
    case "confirmed_choice":return new AutoloopActionResult{State="confirmed",Message="卡片最终选择与所选项一致；无法确认由本次提交产生"};
    case "already_closed":return new AutoloopActionResult{State="rejected",Message="卡片已处理，请刷新查看"};
    case "stale":return new AutoloopActionResult{State="rejected",Message="卡片已变化，请刷新后重新选择"};
    default:return new AutoloopActionResult();
   }}catch{return new AutoloopActionResult();}
  }
  private static string ReadBounded(StreamReader reader,int max){var b=new char[4096];var s=new StringBuilder();int n;while((n=reader.Read(b,0,b.Length))!=0){if(s.Length+n>max)throw new InvalidDataException();s.Append(b,0,n);}return s.ToString();}
  private Task<string> InvokeProcessAsync(string[] args,CancellationToken token){return Task.Run(()=>InvokeProcess(args,token));}
  private string InvokeProcess(string[] args,CancellationToken token){
   string payload;
   if(args.Length==2 && args[0]=="-Operation" && args[1]=="snapshot") payload="{\"op\":\"snapshot\"}";
   else if(args.Length==8 && args[0]=="-Operation" && args[1]=="answer" && args[2]=="-CardId" && args[4]=="-Choice" && args[6]=="-Fingerprint") {
    long id; int choice;
    if(!long.TryParse(args[3],NumberStyles.None,CultureInfo.InvariantCulture,out id)||id<1||id>9007199254740991L||!int.TryParse(args[5],NumberStyles.None,CultureInfo.InvariantCulture,out choice)||choice<0||!Fingerprint(args[7]))throw new InvalidDataException();
    payload=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"op","answer"},{"id",id},{"choice",choice},{"fingerprint",args[7]}});
   } else throw new InvalidDataException();
   // Only the known local key path is quoted; all remote command text is fixed.
   if(settings==null||!configured)throw new InvalidOperationException("Autoloop is not configured");
   string remote=settings.Node+" "+settings.Script;
   string cmd="-i \""+settings.KeyPath+"\" -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=5 -o ServerAliveInterval=3 -o ServerAliveCountMax=2 "+settings.Account+"@"+settings.Host+" \""+remote+"\"";
   string ssh=Path.Combine(Environment.SystemDirectory,@"OpenSSH\ssh.exe");
   var start=new ProcessStartInfo(ssh,cmd){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   token.ThrowIfCancellationRequested();
   using(var p=Process.Start(start)){if(p==null)throw new IOException();var stdout=Task.Run(()=>ReadBounded(p.StandardOutput,524288));var stderr=Task.Run(()=>ReadBounded(p.StandardError,8192));var input=Task.Run(()=>{p.StandardInput.Write(payload);p.StandardInput.Close();});var watch=Stopwatch.StartNew();
    try{while(!p.WaitForExit(50)){token.ThrowIfCancellationRequested();if(watch.Elapsed>TimeSpan.FromSeconds(23))throw new TimeoutException();if(stdout.IsFaulted||stderr.IsFaulted||input.IsFaulted)throw new InvalidDataException();}
     while(!stdout.IsCompleted||!stderr.IsCompleted){token.ThrowIfCancellationRequested();if(watch.Elapsed>TimeSpan.FromSeconds(23))throw new TimeoutException();if(stdout.IsFaulted||stderr.IsFaulted)throw new InvalidDataException();Thread.Sleep(20);}
     if(p.ExitCode!=0||stdout.IsFaulted||stderr.IsFaulted||input.IsFaulted)throw new IOException();return stdout.Result;
    }finally{try{if(!p.HasExited)p.Kill();}catch{}}
   }
  }
 }
}
