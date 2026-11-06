using System;using System.Runtime.InteropServices;
namespace IslandIntegrated {
 // Same frequency-aware Windows counter as the standalone version, sampled
 // natively in the existing background collector; no PowerShell/CIM subprocess.
 public sealed class CpuUtilitySource:IDisposable {
  [StructLayout(LayoutKind.Explicit,Size=16)]struct Value{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Number;}
  [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhOpenQueryW")]static extern uint Open(string source,UIntPtr user,out IntPtr query);
  [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")]static extern uint Add(IntPtr query,string path,UIntPtr user,out IntPtr counter);
  [DllImport("pdh.dll",EntryPoint="PdhCollectQueryData")]static extern uint Collect(IntPtr query);
  [DllImport("pdh.dll",EntryPoint="PdhGetFormattedCounterValue")]static extern uint Format(IntPtr counter,uint flags,out uint type,out Value value);
  [DllImport("pdh.dll",EntryPoint="PdhCloseQuery")]static extern uint Close(IntPtr query);
  readonly object gate=new object();IntPtr query,counter;bool stopped;DateTime next=DateTime.MinValue;double? value;public string State {get;private set;}public const string CounterPath=@"\Processor Information(_Total)\% Processor Utility";
  public static double? Valid(double n){return Double.IsNaN(n)||Double.IsInfinity(n)||n<0?null:(double?)Math.Min(100,n);}
  public double? Read(){lock(gate){if(stopped)return null;if(DateTime.UtcNow<next)return value;next=DateTime.UtcNow.AddSeconds(1);try{uint code;if(query==IntPtr.Zero){code=Open(null,UIntPtr.Zero,out query);if(code==0)code=Add(query,CounterPath,UIntPtr.Zero,out counter);if(code!=0){Stop(code);return null;}code=Collect(query);if(code!=0){Stop(code);return null;}State="warming";return null;}code=Collect(query);if(code!=0){Stop(code);return null;}uint type;Value result;code=Format(counter,0x200,out type,out result);if(code!=0||(result.Status!=0&&result.Status!=1)){value=null;State="unavailable";return null;}value=Valid(result.Number);State=value.HasValue?"fresh":"unavailable";return value;}catch{Stop(1);return null;}}}
  void Stop(uint code){State=code==5?"access-denied":"unavailable";value=null;stopped=true;if(query!=IntPtr.Zero){Close(query);query=IntPtr.Zero;}}
  public void Dispose(){lock(gate){if(query!=IntPtr.Zero){Close(query);query=IntPtr.Zero;}stopped=true;value=null;}}
 }
}
