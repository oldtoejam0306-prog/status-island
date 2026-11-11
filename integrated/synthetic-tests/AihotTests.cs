using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Windows.Forms;
using IslandIntegrated;

static class AihotTests {
  static int assertions;
  sealed class NoActivateTestForm : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
  }
  static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
  static string Payload(string at, string id) {
    return "{\"schemaVersion\":1,\"count\":1,\"items\":[{\"rank\":1,\"id\":\""+id+"\",\"title\":\"Synthetic fixture\",\"source\":{\"name\":\"Fixture\"},\"links\":{\"aihot\":\"https://aihot.news/story/"+id+"\",\"original\":\"https://example.org/story\"},\"sourceCount\":2,\"signalCount\":3,\"sourceNames\":[\"Fixture\"],\"latestAt\":\""+at+"\"}]}";
  }
  static bool Throws(Action a) { try { a(); return false; } catch(InvalidDataException) { return true; } }
  static void ClickCard(AihotSidebarControl card,string fieldName) {
    var field=typeof(AihotSidebarControl).GetField(fieldName,BindingFlags.Instance|BindingFlags.NonPublic);
    var control=(Control)field.GetValue(card);
    var button=control as Button;
    if(button!=null) button.PerformClick();
    else typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[] { EventArgs.Empty });
  }
  static void ClickAt(Form form,Point point) {
    var method=form.GetType().GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic);
    method.Invoke(form,new object[] { new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0) });
  }
  static void ClickControl(Control control) {
    typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[] { EventArgs.Empty });
  }
  static Control FieldControl(object owner,string name) {
    return (Control)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
  }
  static AihotSidebarControl CreateInjectedCard(AihotMonitor monitor,Action<string> opener) {
    var ctor=typeof(AihotSidebarControl).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[] { typeof(AihotMonitor),typeof(Action<string>) },null);
    return (AihotSidebarControl)ctor.Invoke(new object[] { monitor,opener });
  }
  static Form CreatePopup(AihotItem item,Action<string> opener) {
    var type=typeof(AihotMonitor).Assembly.GetType("IslandIntegrated.AihotPopupForm",true);
    var ctor=type.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[] { typeof(AihotItem),typeof(Action<string>) },null);
    return (Form)ctor.Invoke(new object[] { item,opener });
  }
  static void CheckNoDetailWindows() {
    Check(typeof(AihotMonitor).Assembly.GetType("IslandIntegrated.AihotDetailForm",false)==null,"detail/list window type was removed");
  }
  [STAThread]
  public static int Main(string[] args) {
    try {
      RunAll();
      if(args.Length==2 && args[0]=="--render") AihotSidebarControl.RenderEvidence(args[1]);
      Console.WriteLine("AihotTests passed ("+assertions+" assertions).");
      return 0;
    } catch(Exception e) {
      Console.WriteLine("AihotTests failed: "+e.GetType().FullName+" / "+e.Message);
      return 1;
    }
  }
  public static void RunAll() {
    assertions=0; DateTime now=new DateTime(2026,11,10,3,0,0,DateTimeKind.Utc);
    var good=AihotMonitor.Parse(Payload(now.AddMinutes(-3).ToString("o"),"event-1"),now);
    Check(good.Items.Count==1 && good.Items[0].Rank==1,"fresh payload parses");
    Check(good.Items[0].AihotUrl=="https://aihot.news/story/event-1","official URL accepted");
    Check(good.Items[0].OriginalUrl!=null && good.Items[0].OriginalUrl.StartsWith("https://example.org/story",StringComparison.Ordinal),"https source link accepted: "+(good.Items[0].OriginalUrl??"null"));
    Check(Throws(()=>AihotMonitor.Parse("{}",now)),"missing schema rejected");
    Check(Throws(()=>AihotMonitor.Parse(Payload(now.AddHours(-25).ToString("o"),"old"),now)),"stale event rejected");
    Check(Throws(()=>AihotMonitor.Parse(Payload(now.AddMinutes(6).ToString("o"),"future"),now)),"future event rejected");
    Check(Throws(()=>AihotMonitor.Parse("{\"schemaVersion\":1,\"count\":1,\"items\":[null]}",now)),"malformed item rejected");
    var hostile=AihotMonitor.Parse(Payload(now.ToString("o"),"hostile").Replace("https://aihot.news/story/hostile","https://user:pass@aihot.news:444/story/hostile"),now);
    Check(hostile.Items[0].AihotUrl==null,"userinfo and nonstandard port rejected");
    var duplicates=Payload(now.ToString("o"),"same").Replace("\"count\":1,\"items\":[{","\"count\":2,\"items\":[{").Replace("}]}","},{\"rank\":2,\"id\":\"same\",\"title\":\"duplicate\",\"latestAt\":\""+now.ToString("o")+"\"}]}");
    var duplicateResult=AihotMonitor.Parse(duplicates,now);
    Check(duplicateResult.Items.Count==1 && duplicateResult.Items[0].Id=="same","duplicate IDs are ignored");

    var observed=new AihotSnapshot{Items=new List<AihotItem>{
      new AihotItem{Id="a",Rank=1},new AihotItem{Id="b",Rank=2},new AihotItem{Id="c",Rank=3},new AihotItem{Id="d",Rank=4}}};
    var seen=new List<string>();var pending=new List<string>();
    AihotMonitor.ObserveTopThree(observed,seen,pending,true);
    Check(seen.Count==3&&pending.Count==0,"first run quietly seeds the current top three");
    observed.Items[2].Rank=4;observed.Items[3].Rank=3;
    Check(AihotMonitor.NewTopThreeIds(observed,seen).Count==1&&AihotMonitor.NewTopThreeIds(observed,seen)[0]=="d","promotion into top three queues a new item");
    AihotMonitor.ObserveTopThree(observed,seen,pending,false);
    Check(pending.Count==1&&pending[0]=="d","new top three is retained as pending");
    Check(!AihotMonitor.CanAlert(true,false,false,true,true,now,now.AddMinutes(-20),now),"fullscreen blocks popups without deleting pending candidate");
    Check(pending.Count==1,"blocked pending candidate remains queued");
    Check(!AihotMonitor.CanAlert(true,false,true,true,false,now,now.AddMinutes(-20),now),"stale cache cannot notify");
    Check(!AihotMonitor.CanAlert(true,false,true,true,true,now,now.AddMinutes(-14),now),"cooldown is enforced");
    Check(AihotMonitor.CanAlert(true,false,true,true,true,now,now.AddMinutes(-16),now),"pending candidate becomes eligible after unblock and cooldown");
    Check(!AihotMonitor.CanStartPoll(true,true,now,now,now),"overlapping poll is rejected");
    Check(!AihotMonitor.CanStartPoll(true,false,now,now.AddMinutes(5),now),"normal five minute poll interval is enforced");
    Check(AihotMonitor.CanStartPoll(true,false,now,now,now.AddSeconds(-1)),"poll starts when due and backoff elapsed");
    Check(AihotMonitor.RetryDeadline("7200",now)==now.AddHours(2),"Retry-After seconds are honored beyond one hour");
    Check(AihotMonitor.RetryDeadline(now.AddHours(3).ToString("R"),now)==now.AddHours(3),"Retry-After HTTP date is honored");

    string failureDirectory=Path.Combine(Path.GetTempPath(),"AihotSaveFailure-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(failureDirectory);
    Directory.CreateDirectory(Path.Combine(failureDirectory,"aihot-monitor.json.tmp"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(failureDirectory)) {
        monitor.SetPaused(true);
        Check(!monitor.Paused,"failed preference save rolls back the pause setting");
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var stateField=typeof(AihotMonitor).GetField("state",flags);
        object savedState=stateField.GetValue(monitor);
        var pendingField=savedState.GetType().GetField("Pending");
        var seenField=savedState.GetType().GetField("Seen");
        var lastPopupField=savedState.GetType().GetField("LastPopupUtc");
        ((List<string>)pendingField.GetValue(savedState)).Add("fixture-1");
        typeof(AihotMonitor).GetField("enabled",flags).SetValue(monitor,true);
        typeof(AihotMonitor).GetMethod("TryNotify",flags).Invoke(monitor,null);
        Check(((List<string>)pendingField.GetValue(savedState)).Contains("fixture-1"),"failed alert save retains the pending event");
        Check(!((List<string>)seenField.GetValue(savedState)).Contains("fixture-1"),"failed alert save leaves event unacknowledged");
        Check((DateTime)lastPopupField.GetValue(savedState)==DateTime.MinValue,"failed alert save preserves the cooldown");
      }
    } finally { Directory.Delete(failureDirectory,true); }

    string stateDirectory=Path.Combine(Path.GetTempPath(),"AihotTests-"+Guid.NewGuid().ToString("N"));
    try {
      var first=new AihotMonitor(stateDirectory);first.SetPaused(true);first.Dispose();
      var restarted=new AihotMonitor(stateDirectory);
      Check(restarted.Paused,"paused preference survives monitor restart");restarted.Dispose();
    } finally { if(Directory.Exists(stateDirectory))Directory.Delete(stateDirectory,true); }

    using(var host=new NoActivateTestForm()) {
      host.StartPosition=FormStartPosition.Manual;
      host.Location=new System.Drawing.Point(-1000,-1000);
      using(var monitor=AihotMonitor.CreateRenderFixture(Path.Combine(Path.GetTempPath(),"AihotClick-"+Guid.NewGuid().ToString("N")))) {
        var card=new AihotSidebarControl(monitor);
        host.Controls.Add(card);
        host.Show();
        Application.DoEvents();
        var opened=new List<string>();
        var injected=CreateInjectedCard(monitor,url=>opened.Add(url));
        host.Controls.Add(injected);
        injected.Location=new System.Drawing.Point(0,140);
        var count=Application.OpenForms.Count;
        ClickCard(injected,"inspect");
        Check(opened.Last()=="https://aihot.news/","打开AIHOT opens the homepage");
        Check(Application.OpenForms.Count==count,"card navigation creates no desktop form");
        Check(FieldControl(injected,"title").Cursor==Cursors.Hand&&FieldControl(injected,"summary").Cursor==Cursors.Hand,"headline and summary advertise clickability");
        CheckNoDetailWindows();
        host.Hide();
        Application.DoEvents();
        host.Show();
        Application.DoEvents();
        ClickControl(FieldControl(injected,"title"));
        Check(opened.Last()=="https://aihot.news/","AIHOT header opens homepage");
        ClickControl(FieldControl(injected,"summary"));
        Check(opened.Last()=="https://aihot.news/fixture/1","summary opens its displayed item's article");
        typeof(AihotMonitor).GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(monitor,new AihotSnapshot {
          Items=new List<AihotItem> { new AihotItem { Rank=1,Id="newer",Title="New headline",AihotUrl="https://aihot.news/story/newer" } }
        });
        ClickControl(FieldControl(injected,"summary"));
        Check(opened.Last()=="https://aihot.news/fixture/1","summary retains displayed item identity across snapshot changes");
        Check(Application.OpenForms.Count==count,"header and summary navigation create no desktop form");
        injected.Dispose();
      }
    }
    string emptyDirectory=Path.Combine(Path.GetTempPath(),"AihotEmpty-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=new AihotMonitor(emptyDirectory)) {
        var opened=new List<string>();
        using(var card=CreateInjectedCard(monitor,url=>opened.Add(url))) {
          ClickControl(FieldControl(card,"summary"));
          Check(opened.Last()=="https://aihot.news/","summary falls back to homepage when there is no snapshot");
          ClickCard(card,"inspect");
          Check(opened.Last()=="https://aihot.news/","homepage button works before the first snapshot");
        }
        using(var failed=CreateInjectedCard(monitor,url=>{throw new InvalidOperationException("synthetic browser launch failure");})) {
          ClickCard(failed,"inspect");
          Check(failed.DisplayedStatus.Contains("无法打开默认浏览器")&&FieldControl(failed,"status").Text==failed.DisplayedStatus,"homepage launch failure is shown in shared renderer and accessible status text");
        }
      }
    } finally { if(Directory.Exists(emptyDirectory))Directory.Delete(emptyDirectory,true); }
    var popupItem=new AihotItem { Rank=1,Id="notice",Title="Synthetic notification",AihotUrl="https://aihot.news/items/notice",LatestAt=now };
    string popupUrl=null;
    int formCount=Application.OpenForms.Count;
    using(var popup=CreatePopup(popupItem,url=>popupUrl=url)) {
      popup.Show();
      ClickAt(popup,new Point(40,34));
      Check(popupUrl==popupItem.AihotUrl,"notification title opens the associated item URL");
      Check(popup.IsDisposed,"notification closes after successful browser dispatch");
    }
    Check(Application.OpenForms.Count==formCount,"notification title creates no detail/list window");
    using(var popup=CreatePopup(popupItem,url=>popupUrl=url)) {
      popupUrl=null;popup.Show();
      ClickAt(popup,new Point(90,78));
      Check(popupUrl==popupItem.AihotUrl&&popup.IsDisposed,"notification body opens that same item and closes on success");
    }
    using(var popup=CreatePopup(popupItem,url=>popupUrl=url)) {
      popupUrl=null;popup.Show();
      ClickAt(popup,new Point(190,70));
      Check(popupUrl==popupItem.AihotUrl&&popup.IsDisposed,"notification background opens its associated item and closes on success");
    }
    using(var popup=CreatePopup(popupItem,url=>popupUrl=url)) {
      popupUrl=null;popup.Show();
      ClickAt(popup,new Point(346,34));
      Check(popupUrl==null&&popup.IsDisposed,"close button closes without browser dispatch");
    }
    foreach(string unsafeUrl in new[] { "http://aihot.news/story/x", "https://evil.example/story/x", "https://user@aihot.news/story/x", "https://aihot.news:444/story/x" }) {
      string dispatch=null;
      using(var popup=CreatePopup(new AihotItem { AihotUrl=unsafeUrl },url=>dispatch=url)) {
        popup.Show();ClickAt(popup,new Point(40,34));
        Check(dispatch=="https://aihot.news/","unsafe item URL falls back to the AIHOT homepage");
        Check(popup.IsDisposed,"fallback homepage dispatch closes notification successfully");
      }
    }
    string failedUrl=null;
    using(var popup=CreatePopup(popupItem,url=>{ failedUrl=url;throw new InvalidOperationException("synthetic browser launch failure"); })) {
      popup.Show();ClickAt(popup,new Point(90,78));
      Check(failedUrl==popupItem.AihotUrl&&!popup.IsDisposed,"failed browser launch keeps the notification open");
      var popupType=typeof(AihotMonitor).Assembly.GetType("IslandIntegrated.AihotPopupForm",true);
      Check(((string)popupType.GetField("feedback",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup)).Contains("无法打开默认浏览器"),"browser failure is shown in popup text");
    }
    CheckNoDetailWindows();
  }
}
