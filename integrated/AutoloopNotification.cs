using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace IslandIntegrated {
 public static class AutoloopNotificationPolicy {
  public static bool IsReserved(bool exists,bool disposed){
   return exists&&!disposed;
  }
  public static bool IsDelivered(bool exists,bool disposed,bool visible,bool pushSucceeded){
   return IsReserved(exists,disposed)&&visible&&pushSucceeded;
  }
 }

 public static class AutoloopPollingPolicy {
  public const int NotificationRetrySeconds=2;
  public static int IntervalSeconds(bool sidebarVisible){return sidebarVisible?5:15;}
  public static bool CanPoll(bool cardEnabled,bool requestInFlight,DateTime nowUtc,DateTime nextPollUtc){return cardEnabled&&!requestInFlight&&nowUtc>=nextPollUtc;}
  public static bool CanAttemptNotification(DateTime nowUtc,DateTime nextTryUtc){return nowUtc>=nextTryUtc;}
  public static bool NotificationBlocked(bool shuttingDown,bool editing,bool modal,bool fullscreen,bool pinkyRequested){return shuttingDown||editing||modal||fullscreen||pinkyRequested;}
 }

 public sealed class AutoloopReminderState {
  readonly HashSet<string> shown=new HashSet<string>(StringComparer.Ordinal);
  string pending;
  public string PendingKey {get{return pending;}}
  public int ShownCount {get{return shown.Count;}}
  public bool HasPending {get{return !String.IsNullOrEmpty(pending);}}
  public bool BlocksAihot(bool noticeVisible){return HasPending||noticeVisible;}
  public bool CanPresent(bool blocked,bool boundsAvailable){return HasPending&&!blocked&&boundsAvailable;}
  public bool HasBeenShown(string key){return !String.IsNullOrEmpty(key)&&shown.Contains(key);}
  public bool Observe(AutoloopSnapshot snapshot,bool answerLocked,DateTimeOffset now){
   string key=Identity(snapshot,now);
   if(answerLocked||key==null||shown.Contains(key)){pending=null;return false;}
   pending=key;return true;
  }
  public bool MarkDisplayed(string key,bool showReturned,bool isVisible,bool pushSucceeded){
   if(!showReturned||!isVisible||!pushSucceeded)return false;
   if(String.IsNullOrEmpty(key)||pending==null||!String.Equals(pending,key,StringComparison.Ordinal)||shown.Contains(key))return false;
   shown.Add(key);pending=null;return true;
  }
  public bool ReleaseDisplayedForResume(string key,AutoloopSnapshot snapshot,bool answerLocked,DateTimeOffset now){
   if(answerLocked||String.IsNullOrEmpty(key)||!String.Equals(Identity(snapshot,now),key,StringComparison.Ordinal))return false;
   shown.Remove(key);
   pending=key;
   return true;
  }
  public void ClearPending(){pending=null;}
  public static string Identity(AutoloopSnapshot snapshot){return Identity(snapshot,DateTimeOffset.UtcNow);}
  public static string Identity(AutoloopSnapshot snapshot,DateTimeOffset now){
   if(!IsEligibleAsk(snapshot,false,now))return null;
   return snapshot.QuestionId+"\u001f"+snapshot.CardFingerprint;
  }
  public static bool IsEligibleAsk(AutoloopSnapshot snapshot,bool answerLocked,DateTimeOffset now){
   if(answerLocked||snapshot==null||snapshot.State!="connected"||snapshot.CardKind!="ask"||snapshot.PendingCount<1||
      !AutoloopSidebarControl.IsFresh(snapshot,now)||
      String.IsNullOrEmpty(snapshot.QuestionId)||String.IsNullOrEmpty(snapshot.CardFingerprint)||snapshot.CardFingerprint.Length!=64||
      !snapshot.CardFingerprint.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'))||snapshot.Options==null||
      !snapshot.Options.Any(x=>x!=null&&!String.IsNullOrEmpty(x.Id)))return false;
   long id;return long.TryParse(snapshot.QuestionId,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0&&id<=9007199254740991L;
  }
 }

 internal sealed class AutoloopNotificationForm : NoActivateForm {
  readonly AutoloopSnapshot snapshot;
  readonly string identity;
  readonly Action<string> openQuestion;
  Bitmap frame;
  bool pushing,cleaned;
  Rectangle notificationBounds;
  public bool LastPushSucceeded {get;private set;}
  public int LastPushError {get;private set;}
  public bool HasNotificationBounds {get{return !notificationBounds.IsEmpty;}}
  public string Identity {get{return identity;}}
  public event EventHandler NotificationClosed;

  public AutoloopNotificationForm(AutoloopSnapshot value,Action<string> open){
   if(!AutoloopReminderState.IsEligibleAsk(value,false,DateTimeOffset.UtcNow))throw new ArgumentException("A fresh actionable Autoloop ask is required.","value");
   snapshot=value;identity=AutoloopReminderState.Identity(value);openQuestion=open;
   PerPixelLayered=true;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Size=new Size(CornerLayout.Width+2*AihotLayout.Margin,CornerLayout.CompactHeight+2*AihotLayout.Margin);BackColor=Color.Fuchsia;
   GeometryContextChanged+=OnSurfaceChanged;LayeredSurfaceChanged+=OnSurfaceChanged;
   AccessibleName="Autoloop 等待你的回答";AccessibleDescription=value.TaskTitle??value.QuestionText;
  }
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00080000;return p;}}
  public bool SetNotificationBounds(Rectangle bounds){if(IsDisposed)return false;if(notificationBounds==bounds)return !bounds.IsEmpty;notificationBounds=bounds;if(bounds.IsEmpty)return false;Bounds=bounds;return !Visible||PushFrame();}
  public void ShowLayered(){IntPtr unused=Handle;if(!PushFrame()){int error=LastPushError;Dispose();throw new InvalidOperationException("Autoloop notification surface could not be presented ("+error+").");}Show();}
  protected override void OnShown(EventArgs e){base.OnShown(e);if(!LastPushSucceeded&&!PushFrame())Close();}
  protected override void OnPaintBackground(PaintEventArgs e){}
  protected override void OnPaint(PaintEventArgs e){}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button!=MouseButtons.Left||!QuestionHit(AihotLayout.Margin).Contains(e.Location))return;var action=openQuestion;string key=identity;Close();if(action!=null)try{action(key);}catch{}}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);Cursor=QuestionHit(AihotLayout.Margin).Contains(e.Location)?Cursors.Hand:Cursors.Default;}
  void OnSurfaceChanged(object sender,EventArgs e){if(!IsDisposed&&Visible&&!PushFrame())Close();}
  bool PushFrame(){if(pushing||IsDisposed||!IsHandleCreated)return false;pushing=true;try{Bitmap next=RenderFrame(snapshot);if(frame!=null)frame.Dispose();frame=next;LastPushSucceeded=GlassWin.Push(Handle,frame,Location,(byte)255);LastPushError=LastPushSucceeded?0:GlassWin.LastPushError;return LastPushSucceeded;}finally{pushing=false;}}
  void Cleanup(){if(cleaned)return;cleaned=true;GeometryContextChanged-=OnSurfaceChanged;LayeredSurfaceChanged-=OnSurfaceChanged;if(frame!=null){frame.Dispose();frame=null;}var h=NotificationClosed;if(h!=null)try{h(this,EventArgs.Empty);}catch{}}
  protected override void OnFormClosed(FormClosedEventArgs e){Cleanup();base.OnFormClosed(e);}
  protected override void Dispose(bool disposing){if(disposing)Cleanup();base.Dispose(disposing);}
  internal static Rectangle QuestionHit(int margin){return new Rectangle(margin+CornerLayout.Width-116,margin+48,100,26);}
  internal static Bitmap RenderFrame(AutoloopSnapshot value){
   int margin=AihotLayout.Margin;var image=GlassSurface.StatusBackground(CornerLayout.Width,CornerLayout.CompactHeight,margin,AihotLayout.Radius);
   using(var header=new Font("Microsoft YaHei UI",12,FontStyle.Bold,GraphicsUnit.Pixel))
   using(var story=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var action=new Font("Microsoft YaHei UI",12,FontStyle.Bold,GraphicsUnit.Pixel))
   using(var g=Graphics.FromImage(image)){
    g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
    var body=AihotLayout.NotificationBody(margin);using(var clip=GlassSurface.Path(body,AihotLayout.Radius))g.SetClip(clip);
    DrawText(g,"Autoloop · 等待你的回答",header,Palette.Secondary,new Rectangle(body.Left+16,body.Top+8,CornerLayout.Width-32,20));
    string title=String.IsNullOrWhiteSpace(value.QuestionText)?(value.TaskTitle??""):value.QuestionText;
    DrawText(g,title,story,Palette.Text,new Rectangle(body.Left+16,body.Top+32,CornerLayout.Width-148,32));
    Rectangle button=QuestionHit(margin);
    using(var shape=GlassSurface.Path(button,12))using(var fill=new SolidBrush(Color.FromArgb(28,Palette.Accent)))using(var edge=new Pen(Color.FromArgb(120,Palette.Accent),1)){g.FillPath(fill,shape);g.DrawPath(edge,shape);}
    DrawText(g,"查看问题",action,Palette.Accent,button);
   }
   return image;
  }
  static void DrawText(Graphics g,string value,Font font,Color color,Rectangle bounds){using(var brush=new SolidBrush(color))using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap,LineAlignment=StringAlignment.Center})g.DrawString(value??"",font,brush,bounds,format);}
 }
}
