using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace IslandIntegrated {
  public class Layout {
    public List<Card> Cards = new List<Card>();
    public bool Pinned;
    public bool ReduceMotion;
    public bool SolidCards;
    public bool LegacyPalette;public bool WeatherIntroduced;
    public static Layout Default() { return new Layout { Cards = new List<Card> { new Card { Id="music", Size="中" }, new Card { Id="shortcuts", Size="中" }, new Card { Id="clock", Size="中", SizeMode="small" } } }; }
    public void Normalize() {
      if (Cards == null) Cards = new List<Card>();
      var seen = new HashSet<string>();var normalized=new List<Card>();
      // The first legacy time slot owns placement. Map calendar-only layouts to
      // the same time card; retain an existing calendar view if the first lacks it.
      foreach(var card in Cards){if(card==null||!new[]{"music","shortcuts","clock","calendar","weather","autoloop"}.Contains(card.Id))continue;
        if(card.Id=="calendar"){card.Id="clock";card.SizeMode="large";}
        if(!seen.Add(card.Id)){if(card.Id=="clock"){var time=normalized.First(c=>c.Id=="clock");if(String.IsNullOrEmpty(time.SelectedDate))time.SelectedDate=card.SelectedDate;if(String.IsNullOrEmpty(time.DisplayMonth))time.DisplayMonth=card.DisplayMonth;}continue;}
        normalized.Add(card);
      }
      Cards=normalized;
      foreach(var c in Cards) { if(c.SizeMode!="small")c.SizeMode="large"; if (!new[]{"小","中","大"}.Contains(c.Size)) c.Size="中"; if(c.MusicMode!="square") c.MusicMode="strip";if(c.WeatherMode!="strip")c.WeatherMode="square";if(c.AutoloopMode!="strip")c.AutoloopMode="square";c.CityName=(c.CityName??"").Trim();if(!WeatherSnapshot.ValidCity(c.CityName)){c.CityName="";c.WeatherLatitude=null;c.WeatherLongitude=null;}if(c.WeatherLatitude.HasValue&&c.WeatherLongitude.HasValue&&!new WeatherPlace{Name=c.CityName,Latitude=c.WeatherLatitude.Value,Longitude=c.WeatherLongitude.Value}.Valid){c.WeatherLatitude=null;c.WeatherLongitude=null;} }
    }
    public bool Add(string id) {
      // calendar is accepted only as a legacy alias, never as another component.
      bool oldCalendar=id=="calendar";if(oldCalendar)id="clock";
      if ((id!="music" && id!="shortcuts" && id!="clock" && id!="weather" && id!="autoloop") || Cards.Any(c=>c.Id==id||id=="clock"&&c.Id=="calendar")) return false;
      Cards.Add(new Card { Id=id, Size="中",SizeMode=id=="clock"&&!oldCalendar?"small":"large" }); return true;
    }
    public bool Remove(string id) { var c=Cards.FirstOrDefault(x=>x.Id==id); return c!=null && Cards.Remove(c); }
    public bool Move(string source, string target) {
      int from=Cards.FindIndex(card=>card.Id==source), to=Cards.FindIndex(card=>card.Id==target);
      if(from<0 || to<0 || from==to) return false;
      Card c=Cards[from]; Cards.RemoveAt(from); Cards.Insert(to,c); return true;
    }
  }
  public class Card { public string Id; public string Size; public string SizeMode="large"; public string ShortcutPath; public string MusicMode="strip"; public string SelectedDate; public string DisplayMonth; public string CityName=""; public string WeatherMode="square";public string AutoloopMode="square";public double? WeatherLatitude,WeatherLongitude; }
  public static class LayoutStore {
    static Layout Read(string path) {
      try { using(var f=File.OpenRead(path)) { var l=(Layout)new XmlSerializer(typeof(Layout)).Deserialize(f); if(l==null) return null; l.Normalize(); return l; } }
      catch { return null; }
    }
    public static Layout Load(string path) { return Read(path) ?? Read(path+".bak") ?? Layout.Default(); }
    public static void Save(string path, Layout layout) {
      string tmp=path+".tmp";
      try {
        using(var f=File.Create(tmp)) new XmlSerializer(typeof(Layout)).Serialize(f,layout);
        if(File.Exists(path)) {
          // Keep a known-good backup when replacing a damaged primary after recovery.
          File.Replace(tmp,path,Read(path)!=null ? path+".bak" : null);
        } else File.Move(tmp,path);
      } finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }
  }
}
