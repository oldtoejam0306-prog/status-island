using System;
using System.Drawing;
namespace IslandIntegrated {
 // One clock drives position and opacity, independent from decorative highlights.
 public sealed class RevealMotion {
  double from,to; long started; int duration;
  public double Amount {get;private set;}
  public bool Moving {get;private set;}
  public bool TargetOpen {get{return to==1;}}
  public void Target(bool open,long now,bool reduced){
   Advance(now); double next=open?1:0;
   if(reduced){from=to=Amount=next;Moving=false;return;}
   if(next==to)return;
   from=Amount;to=next;started=now;
   duration=Math.Max(1,(int)Math.Round((open?220:180)*Math.Abs(to-from)));Moving=true;
  }
  public void Advance(long now){
   if(!Moving)return;
   double t=Math.Max(0,Math.Min(1,(now-started)/(double)duration));
   Amount=from+(to-from)*(1-Math.Pow(1-t,3));
   if(t>=1){Amount=to;Moving=false;}
  }
  public int Offset(int dpi){return (int)Math.Round(36*Math.Max(.5,dpi/96.0)*(1-Amount));}
 }
 public sealed class StatusHoverState {
  long entered=-1,left=-1;
  public bool Open {get;private set;}
  public bool Tick(long now,Point cursor,Rectangle body,bool blocked){
   if(blocked){Open=false;entered=left=-1;return false;}
   // Coordinates are the submitted layered raster, never a cached Form.Bounds.
   bool inside=body.Contains(cursor);
   if(inside){left=-1;if(entered<0)entered=now;if(now-entered>=100)Open=true;}
   else{entered=-1;if(left<0)left=now;if(now-left>=240)Open=false;}
   return Open;
  }
 }
}
