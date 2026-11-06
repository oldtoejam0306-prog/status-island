using System;using System.Diagnostics;using System.IO;using System.Net.NetworkInformation;using System.Runtime.InteropServices;
namespace IslandIntegrated {
 // Ordinary local counters only: no WMI/CIM, command lines, account/session logs,
 // credentials, network probes or legacy aggregate. Voice reads only existing peon preferences.
 public sealed class LocalSystemSource:IDisposable { readonly CpuUtilitySource utility=new CpuUtilitySource();readonly LegacyBalanceOwner balanceOwner=new LegacyBalanceOwner();public void Dispose(){utility.Dispose();balanceOwner.Dispose();}
  public string NetworkSnapshotPath,BalanceSnapshotPath,ActivitySnapshotPath,BalanceConnectionPath;
  [StructLayout(LayoutKind.Sequential)]struct FileTime {public uint Low,High;public ulong Value {get{return ((ulong)High<<32)|Low;}}}
  [StructLayout(LayoutKind.Sequential)]class MemoryStatus {public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));public uint Load;public ulong Total,Available,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatus value);
  bool codexDenied,claudeDenied;readonly object gate=new object();
  public static double? CpuDelta(ulong oldIdle,ulong oldTotal,ulong idle,ulong total){if(total<=oldTotal||idle<oldIdle||idle-oldIdle>total-oldTotal)return null;return Math.Max(0,Math.Min(100,100*(1-(idle-oldIdle)/(double)(total-oldTotal))));}
  static bool Exists(string name,ref bool denied,out bool known){known=false;if(denied)return false;Process[] p=null;try{p=Process.GetProcessesByName(name);known=true;return p.Length>0;}catch(UnauthorizedAccessException){denied=true;return false;}catch{return false;}finally{if(p!=null)foreach(var item in p)item.Dispose();}}
  public static bool NeedsCodexEstimate(AgentTaskState state,DateTimeOffset now){string value=state==null?"unknown":state.Effective(now);return value=="unknown"||value=="offline";}
  bool activityDenied;
  public static AgentTaskState EstimateActivity(DateTimeOffset now,DateTimeOffset[] writes,bool incomplete){if(incomplete||writes==null||writes.Length==0)return new AgentTaskState();foreach(var at in writes){double age=(now-at).TotalSeconds;if(age>=-3&&age<12)return new AgentTaskState{State="working",Source="codex-file-activity-estimate",ObservedAt=now};}return new AgentTaskState{State="idle",Source="codex-file-activity-estimate",ObservedAt=now};}
  AgentTaskState ReadCodexEstimate(DateTimeOffset now){if(activityDenied)return new AgentTaskState();var times=new System.Collections.Generic.List<DateTimeOffset>();bool incomplete=false;string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);string codexHome=Environment.GetEnvironmentVariable("CODEX_HOME");if(String.IsNullOrWhiteSpace(codexHome))codexHome=Path.Combine(home,".codex");string[] paths={Path.Combine(home,@".codex\state_5.sqlite-wal"),Path.Combine(home,@".codex\logs_2.sqlite-wal"),Path.Combine(codexHome,"state_5.sqlite-wal"),Path.Combine(codexHome,"logs_2.sqlite-wal"),Path.Combine(codexHome,"queue_1.sqlite-wal")};foreach(var file in paths){try{var attr=File.GetAttributes(file);if((attr&FileAttributes.ReparsePoint)!=0){incomplete=true;continue;}times.Add(new DateTimeOffset(File.GetLastWriteTimeUtc(file),TimeSpan.Zero));}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}catch(UnauthorizedAccessException){activityDenied=true;incomplete=true;break;}catch{incomplete=true;}}return EstimateActivity(now,times.ToArray(),incomplete);}
  public LegacySnapshot Read(){lock(gate){var s=new LegacySnapshot{Source="local-system",LocalOnly=true,State="fresh",Timestamp=DateTimeOffset.UtcNow,Fresh=true};
   s.Cpu=utility.Read();
   try{var m=new MemoryStatus();if(GlobalMemoryStatusEx(m))s.Memory=m.Load;}catch{}
   try{var c=new DriveInfo("C:\\");if(c.IsReady&&c.TotalSize>0)s.DiskC=100.0*(c.TotalSize-c.AvailableFreeSpace)/c.TotalSize;}catch{}
   try{var d=new DriveInfo("D:\\");if(d.IsReady&&d.TotalSize>0)s.DiskD=100.0*(d.TotalSize-d.AvailableFreeSpace)/d.TotalSize;}catch{}
   try{s.NetworkAvailable=NetworkInterface.GetIsNetworkAvailable();s.NetworkKnown=true;}catch{}
   bool known;s.CodexRun=Exists("ChatGPT",ref codexDenied,out known);s.CodexKnown=known;s.ClaudeUp=Exists("Claude",ref claudeDenied,out known);s.ClaudeKnown=known;
   // Signed, preverified local NVIDIA tool and existing voice preferences only.
   // Missing sensors never become fabricated zero utilization.
   var gpu=LocalGpuSensor.Read();s.Gpu=gpu.Usage;s.GpuUsedMb=gpu.UsedMb;s.GpuTotalMb=gpu.TotalMb;var voices=VoicePackStore.Read();s.VoiceAvailable=voices.Available;s.CodexVoice=voices.Codex;s.ClaudeVoice=voices.Claude;s.Activity=AgentActivity.Load(ActivitySnapshotPath);if(s.CodexKnown&&s.CodexRun&&NeedsCodexEstimate(s.Activity.Codex,DateTimeOffset.UtcNow))s.Activity=new AgentActivity{Codex=ReadCodexEstimate(DateTimeOffset.UtcNow),Claude=s.Activity.Claude};s.Network=NetworkReadonlySnapshot.Load(NetworkSnapshotPath);if(!String.IsNullOrEmpty(BalanceSnapshotPath))s.Balance=balanceOwner.ReadFast(BalanceSnapshotPath,BalanceConnectionPath);return s;
  }}
 }
}
