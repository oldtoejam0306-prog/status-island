using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;

namespace IslandIntegrated {
  public sealed class WeatherCityChoice {
    public string Name="",Region="",Country="";
    public double? Latitude,Longitude;
    [XmlIgnore] public bool HasCoordinates {get{return Latitude.HasValue&&Longitude.HasValue;}}
    [XmlIgnore] public string DisplayName {get{return Name+(String.IsNullOrEmpty(Region)||Region==Name?"":" · "+Region)+(String.IsNullOrEmpty(Country)?"":" · "+Country);}}
    [XmlIgnore] public bool Valid {
      get{
        if(!WeatherSnapshot.ValidCity(Name)||!SafeLabel(Region)||!SafeLabel(Country)||Latitude.HasValue!=Longitude.HasValue)return false;
        return !HasCoordinates||new WeatherPlace{Name=Name,Latitude=Latitude.Value,Longitude=Longitude.Value}.Valid;
      }
    }
    [XmlIgnore] public string Key {get{return Name.Trim().ToUpperInvariant()+"|"+(HasCoordinates?Latitude.Value.ToString("F6",CultureInfo.InvariantCulture)+"|"+Longitude.Value.ToString("F6",CultureInfo.InvariantCulture):"name");}}
    static bool SafeLabel(string value){return value!=null&&value.Length<=120&&!value.Any(Char.IsControl);}
    public WeatherCityChoice Copy(){return new WeatherCityChoice{Name=Name,Region=Region,Country=Country,Latitude=Latitude,Longitude=Longitude};}
    public override string ToString(){return DisplayName;}
    public WeatherPlace ToPlace(){return Valid&&HasCoordinates?new WeatherPlace{Name=Name,Region=Region,Country=Country,Latitude=Latitude.Value,Longitude=Longitude.Value}:null;}
    public static WeatherCityChoice FromPlace(WeatherPlace place){return place==null?null:new WeatherCityChoice{Name=place.Name,Region=place.Region??"",Country=place.Country??"",Latitude=place.Latitude,Longitude=place.Longitude};}
    public static WeatherCityChoice FromName(string name){return new WeatherCityChoice{Name=(name??"").Trim()};}
  }
  public sealed class WeatherCityHistory {
    public const int Limit=12;
    List<WeatherCityChoice> entries;
    public WeatherCityHistory(IEnumerable<WeatherCityChoice> values){entries=Normalize(values);}
    public List<WeatherCityChoice> Items {get{return entries.Select(x=>x.Copy()).ToList();}}
    public static List<WeatherCityChoice> Normalize(IEnumerable<WeatherCityChoice> values){
      var result=new List<WeatherCityChoice>();var seen=new HashSet<string>(StringComparer.Ordinal);
      foreach(var value in values??Enumerable.Empty<WeatherCityChoice>()){
        if(value==null||!value.Valid)continue;
        var copy=value.Copy();copy.Name=copy.Name.Trim();
        if(!seen.Add(copy.Key))continue;
        result.Add(copy);if(result.Count==Limit)break;
      }
      return result;
    }
    public bool Touch(WeatherCityChoice value){
      if(value==null||!value.Valid)return false;
      if(entries.Count>0&&entries[0].Key==value.Key&&entries[0].DisplayName==value.DisplayName)return false;
      entries=Normalize(new[]{value}.Concat(entries.Where(x=>x.Key!=value.Key)));
      return true;
    }
  }
}
