using System;using System.Drawing;using System.Windows.Forms;
namespace IslandIntegrated {
 // Application-local pointer capture only; no OLE system drag cursor, global
 // hooks, synthetic input, accessibility settings or mouse-highlighter changes.
 public sealed class CardDragInput {
  readonly Action<string,Point> begin;readonly Action<Point> move;readonly Action<bool> end;
  Control owner;string id;Point origin;bool started,finishing;
  public Exception LastError{get;private set;}
  public bool Armed {get{return owner!=null;}}
  public bool Dragging {get{return started;}}
  public CardDragInput(Action<string,Point> onBegin,Action<Point> onMove,Action<bool> onEnd){begin=onBegin;move=onMove;end=onEnd;}
  public void Bind(Control root,string key){if(root is ButtonBase)return;root.Cursor=Cursors.SizeAll;root.MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left||owner!=null)return;LastError=null;owner=root;id=key;origin=root.PointToScreen(e.Location);owner.Capture=true;if(!owner.Capture)Finish(false);};
   root.MouseMove+=(s,e)=>{if(owner!=root||finishing)return;if((e.Button&MouseButtons.Left)==0){Finish(false);return;}Point p=root.PointToScreen(e.Location);try{if(!started){Size threshold=SystemInformation.DragSize;if(Math.Abs(p.X-origin.X)<threshold.Width&&Math.Abs(p.Y-origin.Y)<threshold.Height)return;started=true;begin(id,origin);}move(p);}catch(Exception error){LastError=error;Finish(false);}};
   root.MouseUp+=(s,e)=>{if(owner!=root||e.Button!=MouseButtons.Left)return;try{if(started)move(root.PointToScreen(e.Location));Finish(true);}catch(Exception error){LastError=error;Finish(false);}};
   root.MouseCaptureChanged+=(s,e)=>{if(owner==root&&!root.Capture&&!finishing)Finish(false);};
   root.Disposed+=(s,e)=>{if(owner==root)Finish(false);};
   foreach(Control child in root.Controls)Bind(child,key);
  }
  public void Cancel(){Finish(false);}
  void Finish(bool commit){if(owner==null||finishing)return;finishing=true;Control old=owner;bool notify=started;owner=null;id=null;started=false;try{if(!old.IsDisposed)old.Capture=false;if(notify)end(commit);}catch(Exception error){LastError=error;}finally{finishing=false;}}
 }
}
