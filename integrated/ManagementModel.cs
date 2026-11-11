using System;
using System.Collections.Generic;
using System.Drawing;
namespace IslandIntegrated {
  public sealed class LayoutEditSession {
    readonly Layout original;
    public Layout Draft {get;private set;}
    public bool Completed {get;private set;}
    public LayoutEditSession(Layout source){if(source==null)throw new ArgumentNullException("source");original=source;Draft=Clone(source);}
    public static Layout Clone(Layout source){var copy=new Layout{RecentWeatherCities=WeatherCityHistory.Normalize(source.RecentWeatherCities),Pinned=source.Pinned,Theme=source.Theme,ReduceMotion=source.ReduceMotion,SolidCards=source.SolidCards,LegacyPalette=source.LegacyPalette,WeatherIntroduced=source.WeatherIntroduced,AihotIntroduced=source.AihotIntroduced,Cards=new List<Card>()};if(source.Cards!=null)foreach(var c in source.Cards)if(c!=null)copy.Cards.Add(new Card{Id=c.Id,Size=c.Size,ClockMode=c.ClockMode,ShortcutPath=c.ShortcutPath,MusicMode=c.MusicMode,SizeMode=c.SizeMode,SelectedDate=c.SelectedDate,DisplayMonth=c.DisplayMonth,CityName=c.CityName,WeatherMode=c.WeatherMode,AutoloopMode=c.AutoloopMode,WeatherLatitude=c.WeatherLatitude,WeatherLongitude=c.WeatherLongitude});return copy;}
    public void Cancel(){Completed=true;}
    public void Save(string path){
      if(Completed)throw new InvalidOperationException("Edit session is closed");
      var snapshot=Clone(Draft);snapshot.Normalize();
      // Persist first: a failed write leaves both the live model and draft intact.
      LayoutStore.Save(path,snapshot);
      original.RecentWeatherCities=snapshot.RecentWeatherCities;original.Pinned=snapshot.Pinned;original.Theme=snapshot.Theme;original.ReduceMotion=snapshot.ReduceMotion;original.SolidCards=snapshot.SolidCards;original.LegacyPalette=snapshot.LegacyPalette;original.WeatherIntroduced=snapshot.WeatherIntroduced;original.AihotIntroduced=snapshot.AihotIntroduced;original.Cards=snapshot.Cards;Completed=true;
    }
  }
  public sealed class ManagementBarGeometry {
    public int Width,Height;
    public Rectangle Add,Save,Cancel;
    public static ManagementBarGeometry Create(int width,bool editing){
      width=Math.Max(34,width);var value=new ManagementBarGeometry{Width=editing?width:34,Height=34,Add=new Rectangle(0,0,34,30)};
      if(editing){if(width>=218){value.Save=new Rectangle(42,0,80,30);value.Cancel=new Rectangle(130,0,80,30);}else{value.Height=68;int half=(width-8)/2;value.Save=new Rectangle(0,34,half,30);value.Cancel=new Rectangle(half+8,34,width-half-8,30);}}
      return value;
    }
  }
  public static class AnchoredColumnGeometry {
    public static Rectangle FromSurface(Rectangle area,Rectangle surface,int width){
      width=Math.Max(1,Math.Min(width,area.Width));
      int right=surface.Width>0?surface.Right:area.Right-50;
      int left=Math.Max(area.Left,Math.Min(right-width,area.Right-width));
      int top=surface.Height>0?surface.Bottom+CornerLayout.Gap:area.Top+16+CornerLayout.CompactHeight+CornerLayout.Gap;
      top=Math.Max(area.Top,Math.Min(top,area.Bottom-Math.Min(48,area.Height)));
      return new Rectangle(left,top,width,Math.Max(1,area.Bottom-top-16));
    }
  }
}
