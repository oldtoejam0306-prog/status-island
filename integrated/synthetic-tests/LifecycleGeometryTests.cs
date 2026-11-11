using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using IslandIntegrated;

class LifecycleGeometryTests {
 static int checks,children;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 static bool Acquire(Mutex mutex){try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}}
 static int Child(string mode,string name){
  var mutex=new Mutex(false,name);bool owned=Acquire(mutex);
  if(mode=="--abandon")return owned?0:61; // Exit without release: parent keeps a handle to the named object.
  try{return owned==(mode=="--expect-owned")?0:62;}finally{if(owned)mutex.ReleaseMutex();mutex.Dispose();}
 }
 static void Spawn(string mode,string name){
  var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,mode+" "+name){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
  using(var child=Process.Start(info)){children++;if(!child.WaitForExit(5000)){child.Kill();throw new Exception("Owned console fixture timeout");}Check(child.ExitCode==0,"Child "+mode);}
 }
 public static int Main(string[] args){
  if(args.Length==2)return Child(args[0],args[1]);
  if(args.Length!=0)return 64;
  try {
   // These production classes have no Forms, HWND or desktop operations.
   var render=new RenderGate();Check(render.TryEnter(),"First frame entry");Check(!render.TryEnter(),"Nested paint refused");render.Exit();Check(render.TryEnter(),"Next frame after completion");
   try{throw new Exception("drawing fixture");}catch{}finally{render.Exit();}
   Check(render.TryEnter(),"Next frame after exception");render.Close();render.Exit();Check(!render.TryEnter(),"No frame after close");render.Close();Check(render.Closed,"Repeated close safe");
   var restarted=new RenderGate();Check(restarted.TryEnter(),"New session can render");restarted.Exit();
   var cleanup=new PreviewCleanup();int releases=0;
   int failures=cleanup.Run(()=>{releases++;Check(cleanup.Run(()=>releases++)==0,"Reentrant cleanup refused");},()=>{throw new Exception("resource fixture");},()=>releases++);
   Check(failures==1&&releases==2,"Cleanup continues past failure");Check(cleanup.Run(()=>releases++)==0&&releases==2,"Repeated cleanup safe");
   var placement=new PlacementKey();var area=new Rectangle(-1920,-200,1920,1040);
   Check(!placement.Matches("A",area,96),"Initial placement required");placement.Commit("A",area,96);Check(placement.Matches("A",area,96),"Stable placement reusable");
   Check(!placement.Matches("A",area,144),"DPI invalidates same bounds");Check(!placement.Matches("B",area,96),"Device identity invalidates cloned bounds");
   Check(!placement.Matches("A",new Rectangle(-1920,-200,1920,1000),96),"Work-area change invalidates");placement.Invalidate();Check(!placement.Matches("A",area,96),"Native display message invalidates");
   placement.Commit("B",new Rectangle(0,0,1280,720),144);Check(placement.Matches("B",new Rectangle(0,0,1280,720),144),"Replacement display commits");
   var gate=new FirstFrameGate();gate.GeometryPrepared(new Rectangle(100,100,332,106));gate.ControlsPrepared();gate.PixelsCommitted(new Size(332,106),true);gate.InvalidatePixels();Check(!gate.CanShow,"Display-context change revokes old pixel proof");
   foreach(var work in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1920,-200,1920,1040),new Rectangle(1920,0,1280,720),new Rectangle(0,0,800,520)})Check(work.Contains(App.ColumnBounds(work)),"Column bounds within supported area");
   var hover=new HoverState();int transitions=0;bool previous=false;
   for(long now=0;now<60000;now+=30){hover.Tick(now,true,false,false);if(hover.Open!=previous)transitions++;previous=hover.Open;}
   Check(hover.Open&&transitions==1,"Stationary edge stays open for sixty seconds");hover.Tick(60000,false,false,false);hover.Tick(60649,false,false,false);Check(hover.Open,"Exit delay retained");hover.Tick(60650,false,false,false);Check(!hover.Open,"Leaves after delay");
   hover=new HoverState();long boundary=(long)Int32.MaxValue-100;hover.Tick(boundary,true,false,false);hover.Tick(boundary+250,true,false,false);Check(hover.Open,"Monotonic hover works across signed TickCount boundary");
   hover.Pinned=true;for(long now=boundary+300;now<boundary+60300;now+=30)hover.Tick(now,false,false,false);Check(hover.Open,"Pinned open across refresh periods");hover.Tick(boundary+60300,false,false,true);Check(!hover.Open,"Fullscreen blocks pinned panel");
   var first=new Rectangle(1500,100,332,106);var second=new Rectangle(1472,230,360,500);var screen=new Rectangle(0,0,1920,1080);
   Check(App.HoverContains(new Point(1600,212),first,second),"Status-to-card gap bridge");Check(App.EdgeBridgeContains(new Point(1890,250),screen,first,second),"Edge-to-card bridge");
   Check(!App.IsExternalEdge(new Point(1919,300),screen,new[]{new Rectangle(1920,0,1280,1080)}),"Internal monitor seam excluded");Check(App.IsExternalEdge(new Point(1919,300),screen,new Rectangle[0]),"External edge accepted");
   long before=PreviewClock.NowMs;Thread.SpinWait(1000);Check(PreviewClock.NowMs>=before&&before>=0,"Production monotonic clock nondecreasing");
   Check(App.SelfTest()==0,"Existing pure controller regression");
   string name="Local\\SidebarFixture-"+Guid.NewGuid().ToString("N");
   using(var outer=new Mutex(false,name))using(var inner=new Mutex(false,name)){
    Check(Acquire(outer)&&Acquire(inner),"Recursive same-thread production mutex contract");Spawn("--expect-denied",name);outer.ReleaseMutex();Spawn("--expect-denied",name);inner.ReleaseMutex();Spawn("--expect-owned",name);
    for(int i=0;i<3;i++){Check(Acquire(outer),"Repeat start lease acquired");Spawn("--expect-denied",name);outer.ReleaseMutex();Spawn("--expect-owned",name);}
    Spawn("--abandon",name);bool abandoned=false;try{outer.WaitOne(0);}catch(AbandonedMutexException){abandoned=true;}Check(abandoned,"Crash abandonment observable and acquired");outer.ReleaseMutex();Spawn("--expect-owned",name);
    Check(Acquire(outer),"Startup-failure lease");var failureCleanup=new PreviewCleanup();Check(failureCleanup.Run(()=>{throw new Exception("before-show fixture");},()=>outer.ReleaseMutex())==1,"Failure cleanup still releases lease");Spawn("--expect-owned",name);
   }
   string result=String.Format("{{\"checks\":{0},\"childProcesses\":{1},\"visibleUi\":false,\"hwndCreated\":false,\"realApplicationRestartTested\":false,\"simulatedEdgeHoldMs\":60000}}",checks,children);
   string evidence=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","lifecycle-review"));Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,"tests.json"),result);Console.WriteLine(result);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
