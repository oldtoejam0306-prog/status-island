using System;
using System.IO;
using IslandIntegrated;
class LayoutTests {
 static void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
 static void Main() {
  string dir=Path.Combine(Path.GetTempPath(),"sidebar-layout-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string path=Path.Combine(dir,"layout.xml");
  try {
   var l=Layout.Default(); Check(!l.Add("music") && !l.Add("unknown") && l.Cards.Count==3,"reject duplicate and unknown cards");
   Check(l.Move("shortcuts","music") && l.Cards[0].Id=="shortcuts" && !l.Move("missing","music") && !l.Move("music","music"),"move cards and reject invalid moves");
   Check(l.Remove("music") && !l.Remove("music") && l.Add("music") && l.Cards[2].Id=="music","remove and re-add card");
   l.Cards[0].ShortcutPath=@"C:\Apps\tool.lnk"; l.Cards[0].Size="大"; l.Cards[2].MusicMode="square"; l.Pinned=true;
   LayoutStore.Save(path,l); var restored=LayoutStore.Load(path);
   Check(restored.Pinned && restored.Cards[0].Id=="shortcuts" && restored.Cards[0].ShortcutPath==l.Cards[0].ShortcutPath && restored.Cards[0].Size=="大" && restored.Cards[2].MusicMode=="square","round trip order, settings, and shape");
   restored.Cards[2].MusicMode="strip"; LayoutStore.Save(path,restored); File.WriteAllText(path,"broken xml"); var recovered=LayoutStore.Load(path);
   Check(recovered.Cards[2].MusicMode=="square" && recovered.Cards[0].Id=="shortcuts","recover last valid backup");
   LayoutStore.Save(path,recovered); File.WriteAllText(path,"broken again"); Check(LayoutStore.Load(path).Cards[2].MusicMode=="square","recovery save preserves backup");
   File.WriteAllText(path+".bak","bad too"); Check(LayoutStore.Load(path).Cards.Count==3 && LayoutStore.Load(path).Cards[0].Id=="music","invalid files fall back to defaults");
   var malformed=new Layout(); malformed.Cards.Add(new Card{Id="music", Size="bad", MusicMode="bad"}); malformed.Cards.Add(new Card{Id="music"}); malformed.Cards.Add(new Card{Id="unknown"}); malformed.Normalize(); Check(malformed.Cards.Count==1 && malformed.Cards[0].Size=="中" && malformed.Cards[0].MusicMode=="strip","normalize corrupt card values");
   Check(l.Remove("clock") && l.Add("clock") && !l.Add("clock") && l.Move("clock","shortcuts"),"clock add, remove, duplicate, and reorder");
   var clockNormalized=new Layout(); clockNormalized.Cards.Add(new Card{Id="clock",Size="bad"}); clockNormalized.Cards.Add(new Card{Id="clock"}); clockNormalized.Normalize(); Check(clockNormalized.Cards.Count==1 && clockNormalized.Cards[0].Id=="clock" && clockNormalized.Cards[0].Size=="中","clock normalization");
   Check(Layout.Default().Cards.Exists(c=>c.Id=="clock"),"clock visible by default");
   var empty=new Layout(); LayoutStore.Save(path,empty); Check(LayoutStore.Load(path).Cards.Count==0,"empty user layout persists");
  } finally { Directory.Delete(dir,true); }
 }
}
