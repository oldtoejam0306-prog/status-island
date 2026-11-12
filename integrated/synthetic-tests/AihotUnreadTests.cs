using System;
using System.IO;
using System.Linq;
using System.Reflection;
using IslandIntegrated;

internal static class AihotUnreadTests {
  static int checks;
  static void Check(bool ok,string message) { checks++; if(!ok)throw new Exception("AihotUnreadTests: "+message); }
  static object State(AihotMonitor monitor) { return typeof(AihotMonitor).GetField("state",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(monitor); }
  static void Reconcile(AihotMonitor monitor,AihotSnapshot current,bool seed,AihotSnapshot previous) {
    typeof(AihotMonitor).GetMethod("ReconcileInbox",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(monitor,new object[]{current,seed,previous});
  }
  static AihotSnapshot Snap(AihotItem item) { return new AihotSnapshot{Items=new System.Collections.Generic.List<AihotItem>{item}}; }
  public static int RunAll() {
    checks=0;
    string dir=Path.Combine(Path.GetTempPath(),"AihotUnread-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(dir)) {
        var items=monitor.UnreadItems;
        Check(items.Count==3&&monitor.UnreadCount==3,"fixture exposes three unread examples");
        Check(items[0].LatestAt>items[1].LatestAt&&items[1].LatestAt>items[2].LatestAt,"unread snapshot sorts by latest time");
        Check(items.All(x=>(x.Summary??"").Contains("示例")),"fixture summaries are marked examples");
        var stale=new AihotItem{Id=items[0].Id,StoryUrl=items[0].StoryUrl,Revision="old-revision"};
        Check(!monitor.MarkRead(stale),"stale UI version cannot consume unread");
        Check(monitor.MarkRead(items[0]),"current version marks read");
        Check(monitor.MarkRead(items[0]),"repeat read of the same revision is idempotent");
        Check(monitor.UnreadCount==2,"successful read removes exactly one unread item");
      }
      using(var restarted=new AihotMonitor(dir)) {
        Check(restarted.UnreadCount==2,"unread records persist across monitor restart");
      }
    } finally { if(Directory.Exists(dir))Directory.Delete(dir,true); }

    string failed=Path.Combine(Path.GetTempPath(),"AihotUnreadSaveFailure-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(failed);
    Directory.CreateDirectory(Path.Combine(failed,"aihot-monitor.json.tmp"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(failed)) {
        var displayed=monitor.UnreadItems[0];
        Check(!monitor.MarkRead(displayed),"failed persistence rejects read action");
        Check(monitor.UnreadCount==3,"failed persistence rolls unread state back");
        var state=State(monitor);
        var readHistory=(System.Collections.Generic.List<AihotInboxRecord>)state.GetType().GetField("ReadHistory").GetValue(state);
        for(int i=0;i<500;i++)readHistory.Add(new AihotInboxRecord{Key="old-"+i,Revision="r",Item=new AihotItem{Id="old-"+i}});
        Check(!monitor.MarkRead(displayed),"failed save after history eviction rejects read");
        Check(readHistory.Count==500&&readHistory[0].Key=="old-0"&&readHistory[499].Key=="old-499","failed save restores every evicted history entry");
      }
    } finally { Directory.Delete(failed,true); }

    string batchDir=Path.Combine(Path.GetTempPath(),"AihotUnreadBatch-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(batchDir)) {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        typeof(AihotMonitor).GetField("enabled",flags).SetValue(monitor,true);
        typeof(AihotMonitor).GetField("freshData",flags).SetValue(monitor,true);
        typeof(AihotMonitor).GetField("checkedUtc",flags).SetValue(monitor,DateTime.UtcNow);
        var state=State(monitor);
        var pending=(System.Collections.Generic.List<string>)state.GetType().GetField("Pending").GetValue(state);
        pending.Add("fixture-1");pending.Add("fixture-2");
        var seen=(System.Collections.Generic.List<string>)state.GetType().GetField("Seen").GetValue(state);
        seen.Add("fixture-1");
        var lastPopup=DateTime.UtcNow.AddMinutes(-4);
        state.GetType().GetField("LastPopupUtc").SetValue(state,lastPopup);
        monitor.NotificationChanged+=(sender,args)=>monitor.DeferNotification();
        typeof(AihotMonitor).GetMethod("TryNotify",flags).Invoke(monitor,null);
        Check(monitor.UnreadCount==3,"popup batch lifecycle never marks its unread records read");
        Check(pending.Contains("fixture-1")&&pending.Contains("fixture-2"),"failed bounds/defer path restores the whole pending batch");
        Check(seen.Contains("fixture-1")&&!seen.Contains("fixture-2"),"batch rollback restores original Seen membership");
        Check((DateTime)state.GetType().GetField("LastPopupUtc").GetValue(state)==lastPopup,"batch Defer restores original popup cooldown");
        Check(!monitor.NotificationOpen,"batch rollback test creates no visible popup window");
      }
    } finally { if(Directory.Exists(batchDir))Directory.Delete(batchDir,true); }

    string legacy=Path.Combine(Path.GetTempPath(),"AihotUnreadLegacy-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(legacy);
    try {
      File.WriteAllText(Path.Combine(legacy,"aihot-monitor.json"),"{\"Seen\":[\"legacy-id\"],\"Pending\":[\"legacy-pending\"]}");
      using(var monitor=new AihotMonitor(legacy)) {
        var item=new AihotItem{Rank=1,Id="legacy-id",Title="Old story",Summary="摘要未提供",Revision="Old story\n摘要未提供",LatestAt=DateTime.UtcNow};
        Reconcile(monitor,Snap(item),true,null);
        Check(monitor.UnreadCount==1,"legacy Seen is not interpreted as read during silent seed");
        Check(((System.Collections.Generic.List<string>)State(monitor).GetType().GetField("Pending").GetValue(State(monitor))).Contains("legacy-pending"),"legacy pending survives migration");
        var counts=new AihotItem{Rank=1,Id="legacy-id",Title="Old story",Summary="摘要未提供",Revision="Old story\n摘要未提供",LatestAt=item.LatestAt.AddMinutes(5),SourceCount=80,SignalCount=90};
        Reconcile(monitor,Snap(counts),false,Snap(item));
        Check(monitor.UnreadCount==1,"time and source/signal count changes do not clear unread or create content revision");
        var displayed=monitor.UnreadItems[0];Check(monitor.MarkRead(displayed),"read migrated item");Check(monitor.UnreadCount==0,"item is read");
        var progress=new AihotItem{Rank=1,Id="legacy-id",Title="Updated story",Summary="Claude发布了新版本。",Progress="Claude发布了新版本。",Revision="Updated story\nClaude发布了新版本。",LatestAt=counts.LatestAt.AddMinutes(1)};
        Reconcile(monitor,Snap(progress),false,Snap(counts));
        Check(monitor.UnreadCount==1,"clear title/progress change restores unread");
        Check(((System.Collections.Generic.List<string>)State(monitor).GetType().GetField("Pending").GetValue(State(monitor))).Contains("legacy-id"),"new progress is queued in Pending");
      }
    } finally { Directory.Delete(legacy,true); }

    string backfill=Path.Combine(Path.GetTempPath(),"AihotUnreadBackfill-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(backfill)) {
        var first=monitor.UnreadItems[0];Check(monitor.MarkRead(first),"read before initial summary completion");
        var old=new AihotItem{Rank=1,Id="read-id",Title="Already known",Summary="摘要未提供",Revision="old",LatestAt=DateTime.UtcNow};
        var next=new AihotItem{Rank=1,Id="read-id",Title="Already known",Summary="新补充的真实摘要。",Progress="",Revision="new",LatestAt=DateTime.UtcNow};
        var state=State(monitor);
        var inbox=(System.Collections.Generic.List<AihotInboxRecord>)state.GetType().GetField("Inbox").GetValue(state);
        inbox.Add(new AihotInboxRecord{Key="item:read-id",Revision="old",Item=old});
        var seen=(System.Collections.Generic.List<string>)state.GetType().GetField("Seen").GetValue(state);seen.Add("read-id");
        Check(monitor.MarkRead(monitor.UnreadItems.First(x=>x.Id=="read-id")),"mark known item read before summary backfill");
        Reconcile(monitor,Snap(next),false,Snap(old));
        Check(monitor.UnreadItems.All(x=>x.Id!="read-id"),"first summary backfill does not revive a read item");
        Reconcile(monitor,new AihotSnapshot{Items=new System.Collections.Generic.List<AihotItem>()},false,Snap(next));
        Check(monitor.UnreadCount==2,"leaving top ten does not silently drop other unread records");
      }
    } finally { if(Directory.Exists(backfill))Directory.Delete(backfill,true); }

    string history=Path.Combine(Path.GetTempPath(),"AihotReadHistory-"+Guid.NewGuid().ToString("N"));
    string storyUrl="https://aihot.news/story/760e0591-91c2-4b7a-aea8-5a33ba5d86b1";
    var known=new AihotItem{Rank=1,Id="raw-a",StoryUrl=storyUrl,Title="Known story",Summary="旧进展",Progress="旧进展",Revision="Known story\n旧进展",LatestAt=DateTime.UtcNow};
    try {
      using(var monitor=new AihotMonitor(history)) {
        Reconcile(monitor,Snap(known),true,null);
        Check(monitor.UnreadCount==1&&monitor.MarkRead(monitor.UnreadItems[0]),"seed and persist read history by story key");
      }
      using(var restarted=new AihotMonitor(history)) {
        var countOnly=new AihotItem{Rank=1,Id="raw-b",StoryUrl=storyUrl,Title="Known story",Summary="摘要未提供",Revision="Known story\n摘要未提供",LatestAt=known.LatestAt.AddHours(1),SourceCount=30,SignalCount=44};
        var findCached=typeof(AihotMonitor).GetMethod("FindCachedStoryItem",BindingFlags.NonPublic|BindingFlags.Instance);
        var cached=(AihotItem)findCached.Invoke(restarted,new object[]{"story:760e0591-91c2-4b7a-aea8-5a33ba5d86b1",null});
        Check(cached!=null,"restart retrieves detail baseline from persisted read history");
        countOnly.Summary=cached.Summary;countOnly.Progress=cached.Progress;
        var parse=typeof(AihotMonitor).GetMethod("ParseStoryMarkdown",BindingFlags.NonPublic|BindingFlags.Static);
        Check(!(bool)parse.Invoke(null,new object[]{"",countOnly}),"empty detail response is rejected without network");
        Check(countOnly.Progress=="旧进展","missing detail leaves persisted progress baseline intact");
        Reconcile(restarted,Snap(countOnly),false,null);
        Check(restarted.UnreadCount==0,"restart and representative raw ID change keep unchanged story read");
        var changed=new AihotItem{Rank=1,Id="raw-b",StoryUrl=storyUrl,Title="Known story",Summary="旧进展",Progress="旧进展",Revision="Known story\n旧进展",LatestAt=countOnly.LatestAt,SourceCount=30,SignalCount=44};
        Check((bool)parse.Invoke(null,new object[]{"最新进展（11-12 10:00）：新进展。\n\n事件综述：事件背景。",changed}),"valid detail update parses without network");
        Check(changed.Summary=="事件背景。"&&changed.Progress=="新进展。","summary prefers event synopsis while revision retains latest progress");
        Reconcile(restarted,Snap(changed),false,null);
        Check(restarted.UnreadCount==1,"restart detects true progress change from persisted read version");
      }
    } finally { if(Directory.Exists(history))Directory.Delete(history,true); }
    return checks;
  }
  [STAThread]
  public static int Main() {
    try { int n=RunAll();Console.WriteLine("AihotUnreadTests passed ("+n+" assertions).");return 0; }
    catch(Exception ex) { Console.WriteLine("AihotUnreadTests failed: "+ex);return 1; }
  }
}
