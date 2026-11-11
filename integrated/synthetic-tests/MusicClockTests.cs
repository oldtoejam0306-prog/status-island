using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using IslandIntegrated;using IslandMedia;
class MusicClockTests {
 static void Check(bool b,string m){if(!b)throw new Exception(m);Console.WriteLine("PASS "+m);}
 [STAThread] static int Main(){var now=DateTimeOffset.UtcNow; using(var host=new Form{StartPosition=FormStartPosition.Manual,Location=new Point(-12000,-12000)})using(var card=new MusicCardControl{Size=new Size(344,208)}){
 host.Controls.Add(card);host.Show();Application.DoEvents();card.Clock=()=>now;card.ReduceActivationMotion=()=>false;
 card.Present(new Snapshot{SourceId="cloudmusic.exe",Title="one",Artist="a",Playing=true,PositionSeconds=0,DurationSeconds=1000,PositionTimestamp=now,PlaybackObservedAt=now},null);
 card.PresentLyrics(new LyricResult("ready",new LyricDocument("",new[]{new LyricLine(0,"first"),new LyricLine(1,"second")},1000)));
 var tick=typeof(MusicCardControl).GetMethod("RefreshPlaybackFrame",BindingFlags.Instance|BindingFlags.NonPublic);tick.Invoke(card,null);int count=0;card.Invalidated+=(s,e)=>count++;
 now=now.AddMilliseconds(500);tick.Invoke(card,null);Check(count==0,"no repaint for subpixel progress");
 now=now.AddMilliseconds(600);tick.Invoke(card,null);Check(count>0,"lyrics repaint at next timestamp");
 var timer=(Timer)typeof(MusicCardControl).GetField("clock",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(card);Check(timer.Interval==100,"playback timer bounded to 100 ms");
 host.Close();}return 0;}
}
