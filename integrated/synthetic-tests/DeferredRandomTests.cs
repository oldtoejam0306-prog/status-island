using System;
using System.Linq;
using System.Windows.Forms;
using IslandIntegrated;

public static class DeferredRandomTests {
  static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

  [STAThread]
  public static void Run(){
    var request=new DeferredRandomThemeRequest(delegate(int count){return 0;});
    using(var menu=new ThemePicker()){
      menu.CurrentTheme="tide";int requests=0,selections=0;
      menu.Selected+=(sender,args)=>selections++;
      menu.RandomRequested+=(sender,args)=>{requests++;request.Request(false);menu.RandomPending=request.Pending;};
      var random=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Tag as string=="__random__");
      random.PerformClick();
      Check(requests==1,"Random menu item must send one deferred request");
      Check(menu.CurrentTheme=="tide"&&selections==0,"Random request must not change theme or raise Selected immediately");
      Check(request.Pending&&menu.RandomPending&&random.Text.Contains("已安排"),"Menu must show an in-memory scheduled request");

      request.ObservePointer(false);
      Check(request.ConsumeOnEdgeOpen(false,"tide")==null&&request.Pending,"Non-edge/timer/menu wait must not consume request");
      Check(request.ConsumeOnEdgeOpen(false,"tide")==null&&request.Pending,"Repeated non-edge ticks must leave request pending");
      string first=request.ConsumeOnEdgeOpen(true,"tide");
      Check(first!=null&&first!="tide"&&!request.Pending,"Closed-to-open edge must consume once and exclude current theme");
      Check(request.ConsumeOnEdgeOpen(true,first)==null,"Remaining at the same edge must not consume again");

      request.Request(true);request.ObservePointer(true);
      Check(request.ConsumeOnEdgeOpen(true,first)==null&&request.Pending,"Edge already occupied at scheduling time must not count as the next edge");
      request.ObservePointer(false);
      Check(request.ConsumeOnEdgeOpen(false,first)==null&&request.Pending,"Leaving the edge alone must not consume request");
      string second=request.ConsumeOnEdgeOpen(true,first);
      Check(second!=null&&second!=first&&!request.Pending,"A fresh edge transition after leaving must consume once");

      Check(!DeferredRandomThemeRequest.IsActualEdgeActivation(false,true,false,true,true,true),"Hidden status without an edge-wake signal must not consume");
      Check(!DeferredRandomThemeRequest.IsActualEdgeActivation(true,false,false,true,true,true),"Hidden status without the external right edge must not consume");
      Check(!DeferredRandomThemeRequest.IsActualEdgeActivation(true,true,true,true,true,true),"Fullscreen-blocked hidden edge must not consume");
      Check(!DeferredRandomThemeRequest.IsActualEdgeActivation(true,true,false,true,true,false),"Open state with visible status is not a new edge activation");
      bool hiddenOpenWake=DeferredRandomThemeRequest.IsActualEdgeActivation(true,true,false,true,true,true);
      Check(hiddenOpenWake,"EdgeWakeGate hidden-status wake must count even if widget state is already open");
      request.Request(false);request.ObservePointer(false);
      string hiddenPick=request.ConsumeOnEdgeOpen(hiddenOpenWake,second);
      Check(hiddenPick!=null&&hiddenPick!=second&&!request.Pending,"Hidden-status edge wake consumes the deferred request once");
      Check(request.ConsumeOnEdgeOpen(hiddenOpenWake,hiddenPick)==null,"Repeated hidden-status wake while holding the same edge cannot consume twice");

      request.Request(false);menu.RandomPending=true;request.Cancel();menu.RandomPending=request.Pending;
      Check(request.ConsumeOnEdgeOpen(true,second)==null&&!menu.RandomPending,"Manual selection cancellation must discard deferred request");
      request.Request(false);menu.RandomPending=true;
      menu.CurrentTheme=second;
      menu.Selected+=(sender,args)=>{request.Cancel();menu.RandomPending=request.Pending;};
      var manual=menu.Items.OfType<ToolStripMenuItem>().First(item=>item.Tag as string=="pine");
      manual.PerformClick();
      Check(menu.CurrentTheme=="pine"&&!request.Pending&&!menu.RandomPending,"Explicit theme selection must cancel pending random request");
    }
  }
  [STAThread] public static int Main(){Run();Console.WriteLine("PASS deferred random behavior");return 0;}
}
