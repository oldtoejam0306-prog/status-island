using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using IslandIntegrated;

internal static class AutoloopNoticeTests {
  static int checks;
  static void Check(bool value,string name){checks++;if(!value)throw new Exception("AutoloopNoticeTests: "+name);}
  static AutoloopSnapshot Ask(string id,string fingerprint,DateTimeOffset now,int pending=1){return new AutoloopSnapshot{State="connected",Summary="Fixture",TaskTitle="Synthetic task",CardKind="ask",QuestionId=id,QuestionText="Synthetic question",CardFingerprint=fingerprint,PendingCount=pending,ObservedAt=now,Options=new[]{new AutoloopOption{Id="0",Label="Synthetic option"}}};}
  static Dictionary<string,object> Card(string kind,long id,string fingerprint){var card=new Dictionary<string,object>{{"kind",kind},{"id",id},{"fingerprint",fingerprint},{"state","open"},{"title","Synthetic task"},{"text","Synthetic question"}};if(kind=="ask")card["options"]=new object[]{"Synthetic option"};return card;}
  static string Json(DateTimeOffset observed,string kind,long id,string fingerprint,int pending){var data=new Dictionary<string,object>{{"schema","sidebar.autoloop-bridge.v1"},{"online",true},{"running",true},{"observedUtc",observed.ToString("o")},{"pendingCount",pending},{"cards",pending==0?new object[0]:new object[]{Card(kind,id,fingerprint)}}};return new JavaScriptSerializer().Serialize(data);}
  static AutoloopBridge FixtureBridge(string json){var invoke=new Func<string[],CancellationToken,Task<string>>((args,token)=>Task.FromResult(json));var ctor=typeof(AutoloopBridge).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Func<string[],CancellationToken,Task<string>>)},null);if(ctor==null)throw new MissingMethodException("AutoloopBridge fixture constructor");return (AutoloopBridge)ctor.Invoke(new object[]{invoke});}
  static void BridgeParsing(){var now=DateTimeOffset.UtcNow;string fp=new string('a',64);using(var bridge=FixtureBridge(Json(now,"ask",42,fp,1))){var snapshot=bridge.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();Check(snapshot.State=="connected"&&snapshot.CardKind=="ask"&&snapshot.PendingCount==1,"bridge preserves real ask kind and pending count");Check(snapshot.QuestionId=="42"&&snapshot.CardFingerprint==fp&&snapshot.Options.Length==1,"bridge preserves actual ask identity and choices");Check(AutoloopReminderState.IsEligibleAsk(snapshot,false,now),"fresh actionable ask qualifies");}
    using(var bridge=FixtureBridge(Json(now,"task",43,fp,1))){var snapshot=bridge.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();Check(snapshot.CardKind=="task"&&snapshot.Options.Length==0&&!AutoloopReminderState.IsEligibleAsk(snapshot,false,now),"ordinary fallback card is not an ask notification");}
    using(var bridge=FixtureBridge(Json(now,"ask",0,fp,0))){var snapshot=bridge.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();Check(snapshot.PendingCount==0&&snapshot.CardKind==""&&!AutoloopReminderState.IsEligibleAsk(snapshot,false,now),"empty first snapshot creates no reminder");}
  }
  static void IdentityAndReconnect(){
    var now=DateTimeOffset.UtcNow;
    string fp=new string('b',64);
    var ask=Ask("71",fp,now);
    var state=new AutoloopReminderState();
    Check(state.Observe(ask,false,now)&&state.HasPending,"first fresh snapshot with ask queues a reminder");
    string key=AutoloopReminderState.Identity(ask,now);
    Check(state.BlocksAihot(false),"pending Autoloop reminder holds AIHOT lower priority");
    Check(!state.CanPresent(true,true)&&state.HasPending,"Pinky/modal blocking defers without marking seen");
    Check(!state.CanPresent(false,false)&&state.HasPending,"insufficient notification geometry defers and preserves pending");
    Check(state.CanPresent(false,true),"unblocked fitting toast can be presented");
    Check(!state.MarkDisplayed(key,false,false,false)&&state.HasPending,"failed or unseen PushFrame does not mark reminder shown");
    Check(state.MarkDisplayed(key,true,true,true)&&!state.HasPending,"successful visible PushFrame marks the identity once");
    Check(state.BlocksAihot(true)&&!state.BlocksAihot(false),"AIHOT stays queued only while the Autoloop toast is visible");
    Check(!state.Observe(ask,false,now),"repeated polling of same identity does not notify again");
    Check(state.ReleaseDisplayedForResume(key,ask,false,now)&&state.HasPending,"Pinky or fullscreen blocker restores the same fresh pending ask");
    Check(!state.HasBeenShown(key),"resumed ask can be presented again after a temporary blocker");
    Check(state.MarkDisplayed(key,true,true,true),"resumed ask is marked delivered after the next successful push");
    Check(state.ReleaseDisplayedForResume(key,ask,false,now)&&state.HasPending,"bounds or layered-push failure releases dedupe for the same fresh pending ask");
    Check(AutoloopPollingPolicy.NotificationRetrySeconds==2,"failed bounds or push retries after two seconds");
    Check(state.MarkDisplayed(key,true,true,true),"failed surface can be presented and delivered again after retry");
    Check(!state.ReleaseDisplayedForResume(key,ask,true,now),"answer-locked ask cannot resume a reminder");
    var stale=Ask("71",fp,now.AddSeconds(-31));
    Check(!state.ReleaseDisplayedForResume(key,stale,false,now),"stale ask cannot resume a reminder");
    state.Observe(null,false,now);
    Check(!state.HasPending&&state.HasBeenShown(key),"offline/source disappearance clears pending but retains shown key");
    Check(!state.Observe(ask,false,now),"same key after reconnect does not repeat in the process");
    var changed=Ask("71",new string('c',64),now);
    Check(state.Observe(changed,false,now),"changed fingerprint is a distinct real ask identity");
    Check(state.MarkDisplayed(AutoloopReminderState.Identity(changed,now),true,true,true),"changed ask can be shown once");
    var next=Ask("72",fp,now);
    Check(state.Observe(next,false,now),"new real QuestionId queues independently");
    Check(!state.Observe(next,true,now)&&!state.HasPending,"answer-locked ask is not prompted again");
  }
  static void FreshnessAndPolling(){
    var now=DateTimeOffset.UtcNow;
    string fp=new string('d',64);
    var old=Ask("81",fp,now.AddSeconds(-31));
    Check(!AutoloopReminderState.IsEligibleAsk(old,false,now),"expired ask cannot be notified");
    var offline=Ask("81",fp,now);
    offline.State="offline";
    Check(!AutoloopReminderState.IsEligibleAsk(offline,false,now),"disconnected ask cannot be notified");
    var pending=Ask("81",fp,now,0);
    Check(!AutoloopReminderState.IsEligibleAsk(pending,false,now),"pendingCount zero blocks reminder");
    Check(AutoloopNotificationPolicy.IsReserved(true,false),"created notification reserves the slot before it is shown");
    Check(!AutoloopNotificationPolicy.IsDelivered(true,false,false,false),"prepared notification is not delivered before its first frame");
    Check(!AutoloopNotificationPolicy.IsDelivered(true,false,true,false),"visible notification with failed push is not delivered");
    Check(AutoloopNotificationPolicy.IsDelivered(true,false,true,true),"visible notification with successful push is delivered");
    Check(!AutoloopNotificationPolicy.IsReserved(false,false)&&!AutoloopNotificationPolicy.IsDelivered(false,false,true,true),"closed notification releases its slot and is not delivered");
    Check(AutoloopPollingPolicy.IntervalSeconds(true)==5&&AutoloopPollingPolicy.IntervalSeconds(false)==15,"visible and collapsed polls use 5s/15s cadence");
    Check(!AutoloopPollingPolicy.CanPoll(false,false,DateTime.UtcNow,DateTime.MinValue),"disabled card is not polled");
    Check(!AutoloopPollingPolicy.CanPoll(true,true,DateTime.UtcNow,DateTime.MinValue),"single in-flight request is maintained");
    Check(!AutoloopPollingPolicy.CanPoll(true,false,DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow),"poll interval gate is honored");
    Check(AutoloopPollingPolicy.CanPoll(true,false,DateTime.UtcNow,DateTime.MinValue),"enabled card starts a due poll");
    var retryAt=DateTime.UtcNow.AddSeconds(AutoloopPollingPolicy.NotificationRetrySeconds);
    Check(AutoloopPollingPolicy.NotificationRetrySeconds<=3&&
      !AutoloopPollingPolicy.CanAttemptNotification(retryAt.AddMilliseconds(-1),retryAt)&&
      AutoloopPollingPolicy.CanAttemptNotification(retryAt,retryAt),"failed first-frame retry is short and rate-limited");
    Check(AutoloopPollingPolicy.NotificationBlocked(false,false,false,false,true)&&
      AutoloopPollingPolicy.NotificationBlocked(false,false,false,true,false)&&
      AutoloopPollingPolicy.NotificationBlocked(false,false,true,false,false)&&
      AutoloopPollingPolicy.NotificationBlocked(false,true,false,false,false),"Pinky, fullscreen, modal, and edit block toast display");
    Check(!AutoloopPollingPolicy.NotificationBlocked(false,false,false,false,false),"visible glass toast is allowed when unblocked");
  }
  static void GeometryAndRender(){
    var area=new Rectangle(-1920,0,1920,1040);
    var column=new Rectangle(-420,100,360,800);
    Rectangle bounds;
    Check(AihotNotificationLayout.TryGetBounds(area,column,34,0,80,false,out bounds),"shared notification slot supports negative-origin work area");
    Check(area.Contains(bounds)&&bounds.Size==new Size(CornerLayout.Width+32,CornerLayout.CompactHeight+32),"notice bounds stay in the selected work area");
    Check(!AihotNotificationLayout.TryGetBounds(area,column,34,0,80,true,out bounds),"Pinky request has strict notification priority");
    var now=DateTimeOffset.UtcNow;
    var ask=Ask("91",new string('e',64),now);
    var formType=typeof(AutoloopSidebarControl).Assembly.GetType("IslandIntegrated.AutoloopNotificationForm",true);
    Check(formType.GetField("life",BindingFlags.Instance|BindingFlags.NonPublic)==null,"pending notice has no automatic timeout");
    var render=formType.GetMethod("RenderFrame",BindingFlags.Static|BindingFlags.NonPublic);
    using(var bitmap=(Bitmap)render.Invoke(null,new object[]{ask})){
      Check(bitmap.Size==new Size(CornerLayout.Width+32,CornerLayout.CompactHeight+32),"notification renders the 360x80 glass body plus shared margin");
      Check(bitmap.GetPixel(30,30).A>0,"glass notification render is nonempty");
    }
    var hit=formType.GetMethod("QuestionHit",BindingFlags.Static|BindingFlags.NonPublic);
    var question=(Rectangle)hit.Invoke(null,new object[]{AihotLayout.Margin});
    var expectedQuestion=new Rectangle(AihotLayout.Margin+CornerLayout.Width-116,AihotLayout.Margin+48,100,26);
    Check(question==expectedQuestion,"question hit target matches the rendered button bounds");
    Check(AihotLayout.NotificationBody(AihotLayout.Margin).Contains(question),"only the view-question hit region is inside the toast body");
    var sizeMethod=typeof(AutoloopDetailView).GetMethod("DetailSizeForWorkingArea",BindingFlags.Static|BindingFlags.NonPublic);
    var place=typeof(AutoloopDetailView).GetMethod("BoundsInWorkingArea",BindingFlags.Static|BindingFlags.NonPublic);
    var detailSize=(Size)sizeMethod.Invoke(null,new object[]{area});
    Check(detailSize.Width==CornerLayout.Width,"detail dialog uses the shared 360px width on a normal screen");
    var dialog=(Rectangle)place.Invoke(null,new object[]{area,new Rectangle(-32000,-32000,360,80),360,true,detailSize});
    Check(area.Contains(dialog),"toast-opened detail is clamped to the visible work area despite hidden offscreen owner");
    var smallArea=new Rectangle(1920,0,300,250);
    var smallSize=(Size)sizeMethod.Invoke(null,new object[]{smallArea});
    var smallDialog=(Rectangle)place.Invoke(null,new object[]{smallArea,new Rectangle(-32000,-32000,360,80),360,true,smallSize});
    Check(smallSize.Width==smallArea.Width-32&&smallArea.Contains(smallDialog),"toast-opened detail clamps to a short narrow work area");}
  static void DiagnosticWriteDebounce(){
    string directory=Path.Combine(Path.GetTempPath(),"autoloop-notice-diagnostic-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var app=new App(Path.Combine(directory,"layout.xml"));
    try{
      var timer=(System.Windows.Forms.Timer)typeof(App).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
      timer.Stop();
      var ask=Ask("93",new string('f',64),DateTimeOffset.UtcNow);
      typeof(App).GetField("autoloopSnapshot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,ask);
      var write=typeof(App).GetMethod("RecordAutoloopNotificationState",BindingFlags.Instance|BindingFlags.NonPublic);
      write.Invoke(app,new object[]{"pending","fullscreen","waiting",0});
      string path=Path.Combine(directory,"logs","autoloop-notice-latest.json");
      Check(File.Exists(path),"notice transition writes the latest diagnostic under the state logs directory");
      string first=File.ReadAllText(path);
      var record=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(first);
      Check(Convert.ToString(record["questionId"])=="93"&&Convert.ToInt32(record["pendingCount"])==1&&Convert.ToBoolean(record["eligible"]),"diagnostic records the eligible ask identity and pending count");
      Check(!first.Contains("Synthetic question")&&!first.Contains("Synthetic task"),"diagnostic omits question and task text");
      write.Invoke(app,new object[]{"pending","fullscreen","waiting",0});
      Check(File.ReadAllText(path)==first,"unchanged timer state does not rewrite the diagnostic file");
      write.Invoke(app,new object[]{"pending","pinky","waiting",0});
      Check(File.ReadAllText(path)!=first&&File.ReadAllText(path).Contains("pinky"),"a blocker transition replaces the diagnostic state");
    }finally{
      app.Shutdown();
      if(Directory.Exists(directory))Directory.Delete(directory,true);
    }
  }
  [STAThread] static int Main(){
    try{
      BridgeParsing();
      IdentityAndReconnect();
      FreshnessAndPolling();
      GeometryAndRender();
      DiagnosticWriteDebounce();
      Console.WriteLine("AutoloopNoticeTests passed ("+checks+" assertions; synthetic fixtures only).");
      return 0;
    }catch(Exception error){
      Console.WriteLine(error);
      return 1;
    }
  }
}
