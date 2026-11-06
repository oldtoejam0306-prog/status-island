using System;using System.Collections.Generic;
namespace IslandIntegrated {
 public sealed class WeatherPlace {
  public string Name="",Region="",Country="";public double Latitude,Longitude;
  public string DisplayName {get{return Name+(String.IsNullOrEmpty(Region)||Region==Name?"":" · "+Region)+(String.IsNullOrEmpty(Country)?"":" · "+Country);}}
  public bool Valid {get{return WeatherSnapshot.ValidCity(Name)&&!Double.IsNaN(Latitude)&&!Double.IsInfinity(Latitude)&&Latitude>=-90&&Latitude<=90&&!Double.IsNaN(Longitude)&&!Double.IsInfinity(Longitude)&&Longitude>=-180&&Longitude<=180;}}
 }
 public sealed class WeatherHour {public DateTimeOffset Time;public double? Temperature,PrecipitationProbability,Precipitation;public WeatherKind Kind;}
 public sealed class WeatherDay {public DateTimeOffset Time;public double? High,Low,Precipitation;public WeatherKind Kind;public string Description="";}
 public sealed class WeatherMemoryCache {
  sealed class Entry {public WeatherSnapshot Data;public DateTimeOffset Fetched;}
  readonly object gate=new object();readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
  public WeatherSnapshot Read(string key,DateTimeOffset now,bool allowOld){lock(gate){Entry e;if(!entries.TryGetValue(key,out e))return null;var age=now-e.Fetched;return age.TotalSeconds>=0&&age.TotalMinutes<=(allowOld?120:10)?e.Data:null;}}
  public void Put(string key,WeatherSnapshot data,DateTimeOffset now){if(data==null||data.EffectiveState(now)!="fresh")return;lock(gate){if(!entries.ContainsKey(key)&&entries.Count>=8){string oldest=null;DateTimeOffset stamp=DateTimeOffset.MaxValue;foreach(var pair in entries)if(pair.Value.Fetched<stamp){oldest=pair.Key;stamp=pair.Value.Fetched;}if(oldest!=null)entries.Remove(oldest);}entries[key]=new Entry{Data=data,Fetched=now};}}
  public int Count {get{lock(gate)return entries.Count;}}
 }
 public static class WeatherOnboarding {public static bool Apply(Layout layout){if(layout.WeatherIntroduced)return false;layout.WeatherIntroduced=true;return layout.Add("weather");}}
 public sealed class WeatherDetailNavigation {
  public int HourOffset {get;private set;}
  public static int PageSize {get{return 8;}}
  public void Move(int delta,int count){HourOffset=Math.Max(0,Math.Min(Math.Max(0,count-PageSize),HourOffset+delta));}
  public void Reset(){HourOffset=0;}
 }
}
