namespace IslandIntegrated {
 public static class TransitionMetrics {
  static readonly object gate=new object();static readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.Queue<double>> data=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.Queue<double>>();
  static readonly System.Collections.Generic.Dictionary<string,long> previous=new System.Collections.Generic.Dictionary<string,long>();
  public static string Directory;static long lastFlush;static bool writing;
  public static void Record(string stage,double ms){if(PreviewClock.NowMs>180000)return;lock(gate){System.Collections.Generic.Queue<double> q;if(!data.TryGetValue(stage,out q)){q=new System.Collections.Generic.Queue<double>();data[stage]=q;}q.Enqueue(ms);while(q.Count>256)q.Dequeue();}}
  public static void MarkTick(string stage,long now){lock(gate){long prior;if(previous.TryGetValue(stage,out prior)&&now-prior<1000)Record(stage+"-interval",now-prior);previous[stage]=now;}}
  public static void FlushIfDue(long now){if(now>180000||now-lastFlush<3000||writing||Directory==null)return;lastFlush=now;string folder=Directory;var snapshot=new System.Collections.Generic.Dictionary<string,object>();lock(gate){foreach(var pair in data){var values=pair.Value.ToArray();System.Array.Sort(values);if(values.Length>0)snapshot[pair.Key]=new {samples=values.Length,p50=values[values.Length/2],p95=values[(int)((values.Length-1)*.95)],max=values[values.Length-1]};}}writing=true;System.Threading.Tasks.Task.Run(()=>{try{System.IO.Directory.CreateDirectory(folder);var json=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {schema=1,utc=System.DateTime.UtcNow.ToString("o"),scope="real application UI-thread timings; native-submit is API duration, not photon latency",stages=snapshot});System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"performance-latest.json"),json);}catch{}finally{writing=false;}});}
 }
 public sealed class LegacySnapshot {
  public AgentActivity Activity=new AgentActivity();public NetworkReadonlySnapshot Network=new NetworkReadonlySnapshot();public BalanceReadonlySnapshot Balance=new BalanceReadonlySnapshot();
  public string CodexVoice="",ClaudeVoice="";public bool VoiceAvailable;public double? GpuUsedMb,GpuTotalMb;
  public bool Fresh,MacOnline,PortUp,SocksOk,ExitOk,CodexRun,ClaudeUp;
  public string Source="legacy-aggregate";public bool LocalOnly,NetworkKnown,NetworkAvailable,CodexKnown,ClaudeKnown;
  public bool IsFresh(System.DateTimeOffset now){double age=(now-Timestamp).TotalSeconds;return Fresh&&age>=-5&&age<=(LocalOnly?10:45);}public double? Cpu,Memory,DiskC,DiskD,Gpu;public string State="offline";public System.DateTimeOffset Timestamp;
 }
 public static class LegacyStatusReader {
  public static string FixedPath {get{return System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"StatusIsland","status.json");}}
  static bool Bool(System.Collections.Generic.Dictionary<string,object> d,string key){object o;return d.TryGetValue(key,out o)&&o is bool&&(bool)o;}
  static double? Number(System.Collections.Generic.Dictionary<string,object> d,string key){object o;double value;if(!d.TryGetValue(key,out o)||o==null||o is string||o is bool||!double.TryParse(System.Convert.ToString(o,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||value<0||value>100)return null;return value;}
  public static LegacySnapshot Parse(string text,System.DateTimeOffset now){var result=new LegacySnapshot();try{
   if(text==null||text.Length>65536){result.State="invalid";return result;}var serializer=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=6};var d=serializer.DeserializeObject(text) as System.Collections.Generic.Dictionary<string,object>;object ts;System.DateTimeOffset time;
   if(d==null||!d.TryGetValue("ts",out ts)||!(ts is string)||!System.DateTimeOffset.TryParse((string)ts,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out time)){result.State="invalid";return result;}
   result.Timestamp=time;double age=(now-time).TotalSeconds;if(age < -5){result.State="invalid";return result;}if(age>45){result.State="stale";return result;}
   result.State="fresh";result.Fresh=true;result.MacOnline=Bool(d,"macKnown")&&Bool(d,"macOnline");result.PortUp=Bool(d,"portUp");result.SocksOk=Bool(d,"socksOk");result.ExitOk=Bool(d,"exitOk");result.CodexRun=Bool(d,"codexRun");result.ClaudeUp=Bool(d,"claudeUp");result.Cpu=Number(d,"cpu");result.Memory=Number(d,"mem");result.DiskD=Number(d,"diskD");result.Gpu=Number(d,"gpuUtil");return result;
  }catch{result.State="invalid";return result;}}
  public static LegacySnapshot ReadFixed(){try{var path=FixedPath;var info=new System.IO.FileInfo(path);if(!info.Exists)return new LegacySnapshot();if(info.Length>65536||(info.Attributes&System.IO.FileAttributes.ReparsePoint)!=0||(System.IO.File.GetAttributes(info.DirectoryName)&System.IO.FileAttributes.ReparsePoint)!=0)return new LegacySnapshot{State="invalid"};using(var file=new System.IO.FileStream(path,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite|System.IO.FileShare.Delete)){var bytes=new byte[65537];int read=0,count;while(read<bytes.Length&&(count=file.Read(bytes,read,bytes.Length-read))>0)read+=count;if(read>65536)return new LegacySnapshot{State="invalid"};return Parse(System.Text.Encoding.UTF8.GetString(bytes,0,read),System.DateTimeOffset.UtcNow);}}catch{return new LegacySnapshot();}}
 }
}
