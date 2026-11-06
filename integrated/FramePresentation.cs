using System;
using System.Drawing;
using System.Diagnostics;
namespace IslandIntegrated {
  public sealed class FirstFrameGate {
    bool geometry,controls,pixels;
    Rectangle prepared;
    public Rectangle PreparedBounds {get{return prepared;}}
    public bool CanShow {get{return geometry&&controls&&pixels;}}
    public void GeometryPrepared(Rectangle bounds){if(bounds.Width<=0||bounds.Height<=0)throw new ArgumentOutOfRangeException("bounds");prepared=bounds;geometry=true;pixels=false;}
    public void ControlsPrepared(){controls=true;pixels=false;}
    public void PixelsCommitted(Size size,bool success){pixels=false;if(!geometry||!controls||size!=prepared.Size)throw new InvalidOperationException("Frame dimensions or controls do not match prepared bounds");pixels=success;}
    public void RequireReady(){if(!CanShow)throw new InvalidOperationException("Complete first frame has not been committed");}
    public void InvalidatePixels(){pixels=false;}
  }
  public static class PreviewRefresh {
    // The candidate's peon source is explicitly offline; it cannot cause redraw.
    public static bool Due(bool enabled,DateTime now,DateTime previous){return enabled&&(now-previous).TotalSeconds>=4;}
  }
  public static class PreviewClock {
    static readonly Stopwatch watch=Stopwatch.StartNew();
    public static long NowMs {get{return watch.ElapsedMilliseconds;}}
  }
  public sealed class RenderGate {
    bool busy,closed;
    public bool TryEnter(){if(busy||closed)return false;busy=true;return true;}
    public void Exit(){busy=false;}
    public void Close(){closed=true;}
    public bool Closed {get{return closed;}}
  }
  public sealed class PlacementKey {
    bool valid;string device;Rectangle area;int dpi;
    public bool Matches(string nextDevice,Rectangle nextArea,int nextDpi){return valid&&device==nextDevice&&area==nextArea&&dpi==nextDpi;}
    public void Commit(string nextDevice,Rectangle nextArea,int nextDpi){if(nextDpi<=0)throw new ArgumentOutOfRangeException("nextDpi");device=nextDevice;area=nextArea;dpi=nextDpi;valid=true;}
    public void Invalidate(){valid=false;}
  }
  public sealed class PreviewCleanup {
    bool done;
    public bool Done {get{return done;}}
    public int Run(params Action[] actions){if(done)return 0;done=true;int errors=0;foreach(var action in actions){try{if(action!=null)action();}catch{errors++;}}return errors;}
  }
}
