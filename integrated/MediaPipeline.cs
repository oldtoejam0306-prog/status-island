using System;using System.Threading;using System.Threading.Tasks;using System.Diagnostics;
namespace IslandMedia {
 public sealed class MediaRefreshPolicy {
  int requested;long completed=-1000;
  public void Request(){System.Threading.Interlocked.Exchange(ref requested,1);}
  public bool Due(long now,bool visible,bool reading,bool fast){if(!visible||reading)return false;long elapsed=now-System.Threading.Interlocked.Read(ref completed);if(elapsed<0)elapsed=1000;bool eventPending=System.Threading.Volatile.Read(ref requested)!=0;if(elapsed<(eventPending||fast?80:800))return false;System.Threading.Interlocked.Exchange(ref requested,0);return true;}
  public void Completed(long now){System.Threading.Interlocked.Exchange(ref completed,now);}
 }

 public sealed class ControlTrace {
  public string Action,Outcome="pending";public long Click,Dispatch,Acknowledged,Observed,Painted;
  public static double? Ms(long a,long b){return a>0&&b>=a?(double?)(1000d*(b-a)/Stopwatch.Frequency):null;}
  public double? ClickToDispatch {get{return Ms(Click,Dispatch);}}public double? DispatchToAck {get{return Ms(Dispatch,Acknowledged);}}public double? ClickToObserved {get{return Ms(Click,Observed);}}public double? ObservedToUi {get{return Ms(Observed,Painted);}}
 }
 public sealed class MediaAssetCache:IDisposable {
  readonly object gate=new object();string current="",cached="",pending="";byte[] value;bool disposed;public event Action Changed;
  public byte[] Get(string key){lock(gate){current=key??"";return cached==current?value:null;}}
  public void Begin(string key,Func<Task<byte[]>> loader){lock(gate){if(disposed||pending.Length>0||cached==key)return;current=key;pending=key;}Load(key,loader);}
  async void Load(string key,Func<Task<byte[]>> loader){byte[] data=null;try{data=await loader().ConfigureAwait(false);if(data!=null&&data.Length>4194304)data=null;}catch{}bool changed=false;lock(gate){pending="";if(!disposed&&current==key){cached=key;value=data;changed=true;}}var h=Changed;if(!disposed&&h!=null&&(changed||current!=key))try{h();}catch{}}
  public void Dispose(){lock(gate){disposed=true;value=null;current=cached=pending="";}Changed=null;}
 }
}

namespace IslandMedia {
 // Native state getters are only as current as their retained session object.
 // Limit that object's lifetime; events and control acknowledgements also invalidate it.
 public sealed class MediaSessionLease {
  long renewed=-1;int requested;
  public void Request(){Interlocked.Exchange(ref requested,1);}
  public bool Due(long now,bool fast){long last=Interlocked.Read(ref renewed);return last<0||now<last||Volatile.Read(ref requested)!=0||now-last>=(fast?120:1000);}
  public void Acquired(long now){Interlocked.Exchange(ref renewed,now);}
  // Consume before an acquisition, so a new event arriving during the await survives.
  public void BeginAcquire(){Interlocked.Exchange(ref requested,0);}
 }

}
