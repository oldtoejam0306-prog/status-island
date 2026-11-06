using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using IslandIntegrated;
class PositionManagementTests {
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static string Digest(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
 static Bitmap Button(string text,bool plus){var image=new Bitmap(plus?34:80,30,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(image))ActionButtonSurface.Draw(g,new Rectangle(Point.Empty,image.Size),text,plus,true,false,false);return image;}
 static int Main(){try{
  var review=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","position-review"));var fixture=Path.Combine(review,"management-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
  string path=Path.Combine(fixture,"layout.xml");var live=Layout.Default();live.Pinned=true;live.Cards[1].ShortcutPath=@"C:\Apps\unchanged.lnk";LayoutStore.Save(path,live);string initial=Digest(path);
  var cancel=new LayoutEditSession(live);cancel.Draft.Remove("music");cancel.Draft.Move("clock","shortcuts");cancel.Draft.Pinned=false;cancel.Draft.Cards.First(c=>c.Id=="shortcuts").ShortcutPath=@"C:\Apps\draft.lnk";
  Check(live.Cards.Count==3&&live.Cards[0].Id=="music"&&live.Pinned,"Draft does not mutate live object");Check(live.Cards[1].ShortcutPath==@"C:\Apps\unchanged.lnk","Draft card has independent references");cancel.Cancel();Check(Digest(path)==initial,"Cancel does not write file");Check(live.Cards.Count==3&&live.Pinned,"Cancel preserves live order and pin");bool rejected=false;try{cancel.Save(path);}catch(InvalidOperationException){rejected=true;}Check(rejected&&Digest(path)==initial,"Cancelled session cannot save later");
  var save=new LayoutEditSession(live);save.Draft.Move("clock","music");save.Draft.Cards.First(c=>c.Id=="music").MusicMode="square";save.Draft.Pinned=false;
  Check(Digest(path)==initial,"Draft changes do not autosave");save.Save(path);Check(save.Completed&&live.Cards[0].Id=="clock"&&!live.Pinned,"Explicit save updates live model");var loaded=LayoutStore.Load(path);Check(loaded.Cards[0].Id=="clock"&&loaded.Cards.First(c=>c.Id=="music").MusicMode=="square","Explicit save persists full draft");Check(loaded.Cards.First(c=>c.Id=="shortcuts").ShortcutPath==@"C:\Apps\unchanged.lnk","Unchanged setting survives commit");
  save.Draft.Cards.Clear();Check(live.Cards.Count==3,"Committed model does not alias discarded draft");Check(LayoutStore.Load(path+".bak").Pinned,"Known-good backup retained");
  var fail=new LayoutEditSession(live);fail.Draft.Remove("clock");string beforeFailure=Digest(path);string blocker=path+".tmp";Directory.CreateDirectory(blocker);bool failed=false;try{fail.Save(path);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
  Check(failed&&!fail.Completed&&fail.Draft.Cards.Count==2,"Save failure keeps editable draft");Check(live.Cards.Count==3&&Digest(path)==beforeFailure,"Save failure preserves live model and saved bytes");
  if(!Path.GetFullPath(blocker).StartsWith(Path.GetFullPath(fixture)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture boundary");Directory.Delete(blocker);fail.Save(path);Check(live.Cards.Count==2&&LayoutStore.Load(path).Cards.Count==2,"Save can retry after fixture error");
  var empty=new LayoutEditSession(live);empty.Draft.Cards.Clear();empty.Save(path);Check(LayoutStore.Load(path).Cards.Count==0,"Empty layout remains explicitly saved");
  var area=new Rectangle(0,0,2560,1392);var surface=new Rectangle(2210,120,300,74);var corrected=AnchoredColumnGeometry.FromSurface(area,surface,344);
  Check(App.ColumnBounds(area).Top-corrected.Top==350,"Reproduce and remove fixed expanded-height gap");Check(corrected.Top==surface.Bottom+12,"Column follows real compact bottom");Check(corrected.Right==surface.Right&&surface.Left-corrected.Left==44,"Keep right alignment rather than invent left offset");
  for(int height=74;height<=424;height+=25){var moving=new Rectangle(2210,120,300,height);Check(AnchoredColumnGeometry.FromSurface(area,moving,344).Top==moving.Bottom+12,"Actual height transition follows anchor");}
  foreach(var work in new[]{new Rectangle(-1920,-200,1920,1040),new Rectangle(1920,0,1280,720),new Rectangle(0,0,800,520)}){var body=new Rectangle(work.Right-350,work.Top+100,300,74);var column=AnchoredColumnGeometry.FromSurface(work,body,344);Check(work.Contains(column)&&column.Right==body.Right,"Anchored coordinates remain in supported work area");}
  foreach(int width in new[]{128,200,218,344,420}){var main=ManagementBarGeometry.Create(width,false);Check(main.Width==34&&main.Height==34,"Main contains only small plus control");var manage=ManagementBarGeometry.Create(width,true);var bounds=new Rectangle(0,0,manage.Width,manage.Height);Check(bounds.Contains(manage.Add)&&bounds.Contains(manage.Save)&&bounds.Contains(manage.Cancel),"Management controls not clipped");Check(!manage.Add.IntersectsWith(manage.Save)&&!manage.Save.IntersectsWith(manage.Cancel),"Management controls do not overlap");}
  using(var plus=Button("+",true))using(var saved=Button("\u4FDD\u5B58",false))using(var cancelled=Button("\u53D6\u6D88",false))using(var canvas=new Bitmap(340,110))using(var g=Graphics.FromImage(canvas)){
   g.Clear(Color.FromArgb(14,16,23));using(var brush=new SolidBrush(Color.FromArgb(218,226,238)))g.DrawString("Painter artifact, not desktop capture",SystemFonts.MessageBoxFont,brush,12,12);g.DrawImageUnscaled(plus,16,50);g.DrawImageUnscaled(saved,74,50);g.DrawImageUnscaled(cancelled,174,50);canvas.Save(Path.Combine(review,"action-controls-artifact.png"),ImageFormat.Png);
   Check(plus.GetPixel(17,15).R>200,"Plus stroke visible");Check(saved.GetPixel(8,8).R<100,"No white native button rectangle in custom painter");
  }
  string json="{\"checks\":"+checks+",\"fixture\":\""+Path.GetFileName(fixture)+"\",\"knownGapRemoved\":350,\"screenshotPixelsViewedLocally\":false,\"painterArtifactOnly\":true,\"hwndCreated\":false,\"realUiVerified\":false}";File.WriteAllText(Path.Combine(review,"management-tests.json"),json);Console.WriteLine(json);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
