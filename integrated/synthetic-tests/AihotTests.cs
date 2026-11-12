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
  static bool HasInk(Bitmap image,Rectangle area) {
    area=Rectangle.Intersect(area,new Rectangle(0,0,image.Width,image.Height));
    for(int y=area.Top;y<area.Bottom;y++)for(int x=area.Left;x<area.Right;x++)if(image.GetPixel(x,y).A>0)return true;
    return false;
  }
  static bool FramePixelsDiffer(Bitmap a,Bitmap b) {
    if(a.Size!=b.Size)return true;
    for(int y=0;y<a.Height;y+=2)for(int x=0;x<a.Width;x+=2)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return true;
    return false;
  }
  static void CheckNotificationLayout() {
    var work=new Rectangle(-1920,-200,1920,1080);
    foreach(int statusHeight in new[]{80,120,200,320,424}) {
      var column=CornerLayout.Column(work,16,17,statusHeight);
      Rectangle popup;
      Check(AihotNotificationLayout.TryGetBounds(work,column,40,0,80,false,out popup),"notification slot fits compact/expanded/intermediate height "+statusHeight);
      var popupBody=new Rectangle(popup.Left+16,popup.Top+16,CornerLayout.Width,CornerLayout.CompactHeight);
      var firstCard=new Rectangle(column.Right-CornerLayout.Width,column.Top+CornerLayout.CompactHeight+CornerLayout.Gap,CornerLayout.Width,80);
      Check(!popupBody.IntersectsWith(firstCard)&&popupBody.Bottom+CornerLayout.Gap==firstCard.Top,"popup body and first card keep the 24px gap at height "+statusHeight);
      Check(work.Contains(popup),"popup and shadow stay on the negative-coordinate display");
    }
    var shortWork=new Rectangle(0,0,800,480);
    var shortColumn=CornerLayout.Column(shortWork,16,17,424);
    Rectangle delayed;
    Check(!AihotNotificationLayout.TryGetBounds(shortWork,shortColumn,40,0,80,false,out delayed),"short work area defers popup when the card cannot fit below it");
    var normalColumn=CornerLayout.Column(work,16,17,80);
    Check(!AihotNotificationLayout.TryGetBounds(work,normalColumn,40,180,80,true,out delayed),"Pinky request keeps priority over AIHOT top slot");
    Check(AihotNotificationLayout.ReservedHeight(true,false)==104,"open popup reserves 80px body plus 24px card gap");
    Check(AihotNotificationLayout.ReservedHeight(false,false)==0,"closing popup restores the original card top");
    Check(AihotNotificationLayout.ReservedHeight(true,true)==0,"Pinky priority does not double-reserve the top slot");
  }
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
  static void ClickFrame(AihotSidebarControl card,Point point) {
    typeof(AihotSidebarControl).GetMethod("HandleFrameClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card,new object[]{point});
  }
  static void CheckUnreadUi() {
    Check(AihotLayout.CardHeight==176&&AihotLayout.InboxHeight(0,false)==176&&AihotLayout.InboxHeight(9,true)==176,"overview, list and empty states keep the compact fixed height");
    foreach(int width in new[]{320,360}) {
      Rectangle b0=AihotLayout.ButtonHit(width,0),b1=AihotLayout.ButtonHit(width,1),b2=AihotLayout.ButtonHit(width,2),body=AihotLayout.CardBody(width,176);
      Check(b0.Width==b1.Width&&b1.Width==b2.Width&&body.Contains(b0)&&body.Contains(b1)&&body.Contains(b2),"three equal-width buttons stay inside the "+width+"px card body");
      Check(!b0.IntersectsWith(b1)&&!b1.IntersectsWith(b2),"three button paint/hit rectangles keep gaps at "+width+"px");
    }
    for(int i=0;i<5;i++) {
      Check(!AihotLayout.UnreadStoryHit(360,i).IntersectsWith(AihotLayout.UnreadReadHit(360,i)),"story and read hit targets do not overlap row "+i);
      if(i>0)Check(!AihotLayout.UnreadReadHit(360,i).IntersectsWith(AihotLayout.UnreadReadHit(360,i-1)),"read targets do not overlap adjacent rows");
    }
    Point rightmostHeadlinePoint=new Point(40+360-64-1,56+20);
    Check(AihotLayout.StoryHit(360).Contains(rightmostHeadlinePoint)&&!AihotLayout.CompactReadHit(360).Contains(rightmostHeadlinePoint),"the compact headline's rightmost text remains clickable and separate from read");
    for(int i=0;i<3;i++)Check(!AihotLayout.CompactReadHit(360).IntersectsWith(AihotLayout.ButtonHit(360,i)),"compact read target stays above footer action "+i);
    var longTitle=new AihotItem{Id="long",Revision="r1",Rank=2,Title=new string('长',100),Summary=new string('摘',160),SourceCount=12,LatestAt=new DateTime(2026,11,12,10,42,0)};
    using(var longFrame=AihotRenderer.RenderInboxCard(360,AihotLayout.CardHeight,new List<AihotItem>{longTitle},"最近检查 10:45",false,false,false,false,true))
      Check(longFrame.Width==392&&longFrame.Height==208,"compact inbox renders at the legacy 360x176 body size");
    using(var twoLineFrame=AihotRenderer.RenderInboxCard(360,AihotLayout.CardHeight,new List<AihotItem>{longTitle},"最近检查 10:45",false,false,false,false,false)) {
      Check(HasInk(twoLineFrame,new Rectangle(40,88,296,13)),"long headline paints glyphs in its second line");
    }
    using(var idle=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"最近检查",false,false,false,false,false,0,false,false,false,false,false,false))
    using(var hovered=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"最近检查",false,false,false,false,false,0,true,false,true,false,true,false))
    using(var pressed=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"最近检查",false,false,false,false,false,0,false,true,false,true,false,true))
      Check(FramePixelsDiffer(idle,hovered)&&FramePixelsDiffer(idle,pressed),"hover and pressed states visibly change the three footer buttons");
    using(var baseButtons=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"最近检查",false,false,false,false,false,0,false,false,false,false,false,false))
    using(var disabled=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"检查中…",false,true,false,false,false,0,false,false,false,false,false,false))
    using(var active=AihotRenderer.RenderInboxCard(360,176,new List<AihotItem>{longTitle},"最近检查",false,false,true,false,false,0,false,false,false,false,false,false))
      Check(FramePixelsDiffer(baseButtons,disabled)&&FramePixelsDiffer(baseButtons,active),"busy and list-active button states visibly differ from default");
    using(var narrow=AihotRenderer.RenderInboxCard(320,176,new List<AihotItem>{longTitle},"最近检查",false,false,false,false,false,0,false,false,false,false,false,false))
      Check(narrow.Width==352,"the 320px responsive card paints all three action slots");
    using(var emptyFrame=AihotRenderer.RenderInboxCard(360,AihotLayout.InboxHeight(0,false),new List<AihotItem>(),"最近检查 10:45",false,false,false,false,true))
      Check(emptyFrame.Height==208,"empty inbox remains at the compact fixed height and shows check time");
    string successDir=Path.Combine(Path.GetTempPath(),"AihotUiRead-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(successDir)) using(var card=CreateInjectedCard(monitor,url=>{})) {
        ClickFrame(card,AihotLayout.CompactReadHit(360).Location);
        var current=(AihotItem)typeof(AihotSidebarControl).GetField("displayedItem",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);
        Check(monitor.UnreadCount==2&&current.Id=="fixture-2","manual read removes the displayed revision and fills the next row");
        int viewChanges=0;card.ViewChanged+=(s,e)=>viewChanges++;ClickControl(FieldControl(card,"expand"));
        Check(card.Expanded&&card.PreferredCardHeight==176,"native list toggle switches mode without increasing card height");
        Check(viewChanges==1,"expanded state notifies the host even when the viewport height stays the same");
      }
    } finally { if(Directory.Exists(successDir))Directory.Delete(successDir,true); }
    string scrollDir=Path.Combine(Path.GetTempPath(),"AihotUiScroll-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(scrollDir)) {
        var state=typeof(AihotMonitor).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(monitor);
        var inbox=(List<AihotInboxRecord>)state.GetType().GetField("Inbox").GetValue(state);
        for(int i=3;i<8;i++){var item=monitor.Snapshot.Items[i];inbox.Add(new AihotInboxRecord{Key="item:"+item.Id,Revision=item.Revision,Item=item});}
        var opened=new List<string>();
        using(var card=CreateInjectedCard(monitor,url=>opened.Add(url))) {
          card.Expanded=true;Check(card.PreferredCardHeight==176,"eight unread items use the fixed-height list mode");
          typeof(AihotSidebarControl).GetMethod("ScrollUnread",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card,new object[]{5});
          int offset=(int)typeof(AihotSidebarControl).GetProperty("ScrollOffset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card,null);
          Check(offset==5&&((AihotItem)typeof(AihotSidebarControl).GetField("displayedItem",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card)).Id=="fixture-6","scroll updates the native first-row story identity");
          var rows=(Control[])typeof(AihotSidebarControl).GetField("rowLabels",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);
          ClickControl(rows[0]);
          Check(opened.Last()=="https://aihot.news/fixture/6","native story click opens the first item on the scrolled page");
          var rowReads=(Control[])typeof(AihotSidebarControl).GetField("rowReads",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);
          ClickControl(rowReads[0]);
          var firstAfterPageClamp=(AihotItem)typeof(AihotSidebarControl).GetField("displayedItem",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);
          offset=(int)typeof(AihotSidebarControl).GetProperty("ScrollOffset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card,null);
          Check(offset==3&&firstAfterPageClamp.Id=="fixture-4","reading the clamped last page's first row updates the native read identity");
          card.Expanded=false;
          var firstAfterCollapse=(AihotItem)typeof(AihotSidebarControl).GetField("displayedItem",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);
          offset=(int)typeof(AihotSidebarControl).GetProperty("ScrollOffset",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card,null);
          Check(offset==0&&firstAfterCollapse.Id=="fixture-1","collapsing returns the inbox to the newest three items");
        }
      }
    } finally { if(Directory.Exists(scrollDir))Directory.Delete(scrollDir,true); }
    string failureDir=Path.Combine(Path.GetTempPath(),"AihotUiReadFail-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(failureDir);Directory.CreateDirectory(Path.Combine(failureDir,"aihot-monitor.json.tmp"));
    try {
      using(var monitor=AihotMonitor.CreateRenderFixture(failureDir)) using(var card=CreateInjectedCard(monitor,url=>{})) {
        ClickFrame(card,AihotLayout.UnreadReadHit(360,0).Location);
        Check(monitor.UnreadCount==3&&card.AccessibleName.Contains("保存失败"),"failed read save retains the unread item and exposes an accessible error");
      }
    } finally { Directory.Delete(failureDir,true); }
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
    var popup=(Form)ctor.Invoke(new object[] { item,opener });
    popup.Location=new Point(SystemInformation.VirtualScreen.Left-10000,SystemInformation.VirtualScreen.Top-10000);
    return popup;
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
      Console.WriteLine("AihotTests failed: "+e.ToString());
      return 1;
    }
  }
  public static void RunAll() {
    assertions=0; DateTime now=new DateTime(2026,11,10,3,0,0,DateTimeKind.Utc);
    CheckNotificationLayout();
    CheckUnreadUi();
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
      new AihotItem{Id="a",Rank=1},new AihotItem{Id="b",Rank=2},new AihotItem{Id="c",Rank=3},new AihotItem{Id="d",Rank=4},
      new AihotItem{Id="e",Rank=5},new AihotItem{Id="f",Rank=6},new AihotItem{Id="g",Rank=7},new AihotItem{Id="h",Rank=8},
      new AihotItem{Id="i",Rank=9},new AihotItem{Id="j",Rank=10},new AihotItem{Id="k",Rank=11}}};
    var seen=new List<string>();var pending=new List<string>();
    AihotMonitor.ObserveTopTen(observed,seen,pending,true);
    Check(seen.Count==10&&pending.Count==0,"first run quietly seeds the current top ten");
    observed.Items[10].Rank=10;
    observed.Items[9].Rank=11;
    Check(AihotMonitor.NewTopTenIds(observed,seen).Count==1&&AihotMonitor.NewTopTenIds(observed,seen)[0]=="k","promotion into top ten queues rank ten, excluding rank eleven");
    AihotMonitor.ObserveTopTen(observed,seen,pending,false);
    Check(pending.Count==1&&pending[0]=="k","new top ten item is retained as pending");
    AihotMonitor.ObserveTopTen(observed,seen,pending,false);
    Check(pending.Count==1&&pending[0]=="k","repeated refresh does not duplicate a pending ID");
    var boundarySnapshot=new AihotSnapshot{Items=new List<AihotItem>{
      new AihotItem{Id="rank4",Rank=4},new AihotItem{Id="rank10",Rank=10},new AihotItem{Id="rank11",Rank=11}}};
    var boundaryPending=new List<string>();
    var boundaryNew=AihotMonitor.NewTopTenIds(boundarySnapshot,new List<string>());
    Check(boundaryNew.Contains("rank4"),"NewTopTenIds independently includes rank four");
    Check(boundaryNew.Contains("rank10")&&!boundaryNew.Contains("rank11"),"NewTopTenIds includes rank ten and excludes rank eleven");
    AihotMonitor.ObserveTopTen(boundarySnapshot,new List<string>(),boundaryPending,false);
    Check(boundaryPending.Contains("rank4"),"a new rank four event is independently queued");
    Check(boundaryPending.Contains("rank10"),"a new rank ten event is independently queued");
    Check(!boundaryPending.Contains("rank11")&&boundaryPending.Count==2,"rank eleven is excluded from top ten alerts");
    Check(!AihotMonitor.CanAlert(true,false,false,true,true,now,now.AddMinutes(-20),now),"fullscreen blocks popups without deleting pending candidate");
    Check(pending.Count==1,"blocked pending candidate remains queued");
    Check(!AihotMonitor.CanAlert(true,false,true,true,false,now,now.AddMinutes(-20),now),"stale cache cannot notify");
    Check(!AihotMonitor.CanAlert(true,false,true,true,true,now,now.AddSeconds(-14),now),"14 second cooldown remains enforced");
    Check(AihotMonitor.CanAlert(true,false,true,true,true,now,now.AddSeconds(-15),now),"15 second cooldown boundary is eligible");
    Check(AihotMonitor.CanAlert(true,false,true,true,true,now.AddMinutes(-15),now.AddSeconds(-15),now),"15 minute data freshness boundary is independent of popup cooldown");
    Check(!AihotMonitor.CanAlert(true,false,true,true,true,now.AddMinutes(-15).AddSeconds(-1),now.AddMinutes(-1),now),"data older than 15 minutes is rejected independently of cooldown");
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

    string legacyStateDirectory=Path.Combine(Path.GetTempPath(),"AihotLegacyState-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(legacyStateDirectory);
    try {
      File.WriteAllText(Path.Combine(legacyStateDirectory,"aihot-monitor.json"),"{\"Paused\":true,\"Seen\":[\"old-seen\"],\"Pending\":[\"old-pending\"],\"LastPopupUtc\":\"2026-11-10T02:59:00Z\"}");
      using(var legacy=new AihotMonitor(legacyStateDirectory)) {
        Check(legacy.Paused,"legacy persisted pause remains honored");
        var saved=typeof(AihotMonitor).GetField("state",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(legacy);
        Check(((List<string>)saved.GetType().GetField("Seen").GetValue(saved)).Contains("old-seen"),"legacy seen IDs remain loaded");
        Check(((List<string>)saved.GetType().GetField("Pending").GetValue(saved)).Contains("old-pending"),"legacy pending IDs remain loaded");
        typeof(AihotMonitor).GetMethod("SetPaused").Invoke(legacy,new object[]{false});
        var unpausedState=typeof(AihotMonitor).GetField("state",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(legacy);
        Check(((List<string>)unpausedState.GetType().GetField("Seen").GetValue(unpausedState)).Contains("old-seen")&&((List<string>)unpausedState.GetType().GetField("Pending").GetValue(unpausedState)).Contains("old-pending"),"saving a legacy preference preserves both old deduplication lists");
      }
    } finally { if(Directory.Exists(legacyStateDirectory))Directory.Delete(legacyStateDirectory,true); }

    using(var host=new NoActivateTestForm()) {
      host.StartPosition=FormStartPosition.Manual;
      host.Location=new System.Drawing.Point(SystemInformation.VirtualScreen.Left-10000,SystemInformation.VirtualScreen.Top-10000);
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
        Check(opened.Last()=="https://aihot.news/fixture/1","first unread title opens its matching article");
        typeof(AihotMonitor).GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(monitor,new AihotSnapshot {
          Items=new List<AihotItem> { new AihotItem { Rank=1,Id="newer",Title="New headline",AihotUrl="https://aihot.news/story/newer" } }
        });
        ClickControl(FieldControl(injected,"summary"));
        Check(opened.Last()=="https://aihot.news/fixture/2","after reading the first item, the next unread title fills the row");
        Check(Application.OpenForms.Count==count,"header and summary navigation create no desktop form");
        injected.Dispose();
      }
    }
    string emptyDirectory=Path.Combine(Path.GetTempPath(),"AihotEmpty-"+Guid.NewGuid().ToString("N"));
    try {
      using(var monitor=new AihotMonitor(emptyDirectory)) {
        typeof(AihotMonitor).GetField("message",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(monitor,"网络连接失败 · 稍后重试");
        var opened=new List<string>();
        using(var card=CreateInjectedCard(monitor,url=>opened.Add(url))) {
          string emptyStatus=(string)typeof(AihotSidebarControl).GetMethod("CurrentCardStatus",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card,null);
          Check(emptyStatus.Contains("网络连接失败"),"empty inbox retains monitor network errors in the status line");
          int beforeEmpty=opened.Count;ClickControl(FieldControl(card,"summary"));
          Check(opened.Count==beforeEmpty,"empty inbox does not navigate to a nonexistent story");
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
      var popupType=typeof(AihotMonitor).Assembly.GetType("IslandIntegrated.AihotPopupForm",true);
      Check((bool)popupType.GetProperty("HasVisiblePopup",BindingFlags.Public|BindingFlags.Static).GetValue(null,null),"visible popup is exposed to notification gate");
      string pendingDirectory=Path.Combine(Path.GetTempPath(),"AihotVisiblePopup-"+Guid.NewGuid().ToString("N"));
      try {
        using(var gated=new AihotMonitor(pendingDirectory)) {
          var flags=BindingFlags.Instance|BindingFlags.NonPublic;
          var item=new AihotItem{Id="must-remain-pending",Rank=1,LatestAt=DateTime.UtcNow};
          typeof(AihotMonitor).GetField("snapshot",flags).SetValue(gated,new AihotSnapshot{Items=new List<AihotItem>{item}});
          typeof(AihotMonitor).GetField("checkedUtc",flags).SetValue(gated,DateTime.UtcNow);
          typeof(AihotMonitor).GetField("freshData",flags).SetValue(gated,true);
          typeof(AihotMonitor).GetField("enabled",flags).SetValue(gated,true);
          var state=typeof(AihotMonitor).GetField("state",flags).GetValue(gated);
          ((List<string>)state.GetType().GetField("Pending").GetValue(state)).Add(item.Id);
          state.GetType().GetField("LastPopupUtc").SetValue(state,DateTime.UtcNow.AddSeconds(-20));
          typeof(AihotMonitor).GetMethod("TryNotify",flags).Invoke(gated,null);
          Check(((List<string>)state.GetType().GetField("Pending").GetValue(state)).Contains(item.Id),"an existing visible popup prevents consuming the next pending item");
        }
      } finally { if(Directory.Exists(pendingDirectory))Directory.Delete(pendingDirectory,true); }
      ClickAt(popup,new Point(40,34));
      Check(popupUrl==popupItem.AihotUrl,"notification title opens the associated item URL");
      Check(popup.IsDisposed,"notification closes after successful browser dispatch");
      Check(!(bool)popupType.GetProperty("HasVisiblePopup",BindingFlags.Public|BindingFlags.Static).GetValue(null,null),"closed popup releases notification gate");
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
