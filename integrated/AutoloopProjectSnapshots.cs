using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace IslandIntegrated {
  public static class AutoloopProjectSnapshots {
    public static AutoloopSnapshotData Read(string projectPath,string audiobookPath,DateTimeOffset now){return AutoloopSnapshotData.Read(projectPath,audiobookPath,now);}
  }

  public sealed class AutoloopProjectRow {
    public string Id {get;set;}
    public string Name {get;set;}
    public string DirectoryState {get;set;}
    public string LatestDocumentDate {get;set;}
  }

  public sealed class AutoloopSnapshotData {
    public string State {get;set;}
    public DateTimeOffset? ObservedAt {get;set;}
    public bool Expired {get;set;}
    public string Notice {get;set;}
    public string Progress {get;set;}
    public string ChatActivity {get;set;}
    public IList<AutoloopProjectRow> Projects {get;set;}
    public string AudiobookState {get;set;}
    public DateTimeOffset? AudiobookObservedAt {get;set;}
    public bool AudiobookExpired {get;set;}
    public string AudiobookNotice {get;set;}
    public int? Books {get;set;}
    public int? PlannedChapters {get;set;}
    public int? AttemptDirectories {get;set;}
    public string StageSummary {get;set;}

    public AutoloopSnapshotData() {
      State="unknown"; Notice="快照未知"; Progress="unknown"; ChatActivity="unknown"; Projects=new List<AutoloopProjectRow>();
      AudiobookState="unknown"; AudiobookNotice="快照未知"; StageSummary="阶段标记未知";
    }

    public static AutoloopSnapshotData Read(string projectPath,string audiobookPath,DateTimeOffset now) {
      var result=new AutoloopSnapshotData();
      ReadProjects(result,projectPath,now); ReadAudiobook(result,audiobookPath,now); return result;
    }
    static Dictionary<string,object> Parse(string path) {
      if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException();
      var info=new FileInfo(path);if(info.Length<2||info.Length>65536)throw new InvalidDataException();
      string json;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||stream.Length>65536)throw new InvalidDataException();using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true)){json=reader.ReadToEnd();if(json.Length>65536)throw new InvalidDataException();}}
      var serializer=new JavaScriptSerializer{MaxJsonLength=65536};
      var value=serializer.DeserializeObject(json) as Dictionary<string,object>;if(value==null)throw new InvalidDataException();return value;
    }
    static string String(Dictionary<string,object> row,string key){object value;if(!row.TryGetValue(key,out value)||!(value is string))throw new InvalidDataException();return (string)value;}
    static DateTimeOffset Time(string value){DateTimeOffset parsed;if(value==null||!Regex.IsMatch(value,@"(?:Z|[+-]\d{2}:\d{2})$",RegexOptions.IgnoreCase)||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed))throw new InvalidDataException();return parsed.ToUniversalTime();}
    static bool Freshness(DateTimeOffset observed,DateTimeOffset now,out bool expired) {
      var age=now.ToUniversalTime()-observed;expired=age>TimeSpan.FromHours(2);return age>=TimeSpan.FromMinutes(-5);
    }
    static void ReadProjects(AutoloopSnapshotData result,string path,DateTimeOffset now) {
      try {
        var root=Parse(path);if(String(root,"schema")!="project-meta.v1"||String(root,"progress")!="unknown"||String(root,"chat_activity")!="unknown")throw new InvalidDataException();
        var observed=Time(String(root,"observed_utc"));bool expired;if(!Freshness(observed,now,out expired))throw new InvalidDataException();
        var items=root["projects"] as object[];if(items==null)throw new InvalidDataException();
        var names=new Dictionary<string,string>{{"iphone","iPhone use"},{"bigfish","大肥鱼"},{"audiobook","本地声音"},{"photoshop","PS 修图"},{"pixinsight","PixInsight"},{"plugins","Codex 插件"},{"windows","Windows 设备修复"},{"cqs","量化模型"},{"dsh","DSH 辅助"},{"sidebar","侧边栏"}};
        var seen=new HashSet<string>();foreach(var item in items){var row=item as object[];if(row==null||row.Length!=3||!(row[0] is string)||!(row[1] is string))throw new InvalidDataException();string id=(string)row[0],state=(string)row[1];string date=row[2] as string;DateTime parsedDate;if(!names.ContainsKey(id)||!seen.Add(id)||!(state=="a"||state=="m"||state=="u")||(row[2]!=null&&(date==null||!DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsedDate))))throw new InvalidDataException();
          result.Projects.Add(new AutoloopProjectRow{Id=id,Name=names[id],DirectoryState=state,LatestDocumentDate=date==null?"无可信文档日期":date});}
        if(seen.Count>names.Count)throw new InvalidDataException();
        result.State="available";result.ObservedAt=observed;result.Expired=expired;result.Notice=expired?"项目快照已过期":"目录和固定文档日期快照；不代表项目运行或完成，也不证明 Hub 在线";
      }catch{result.State="unknown";result.ObservedAt=null;result.Projects.Clear();result.Notice="项目快照缺失、损坏、过期或格式未知";}
    }
    static void ReadAudiobook(AutoloopSnapshotData result,string path,DateTimeOffset now) {
      try {
        var root=Parse(path);if(String(root,"schema")!="audiobook-sequence-markers.v1"||String(root,"scope")!="known_sequence_root"||String(root,"progress")!="unknown"||String(root,"chat_activity")!="unknown"||!(root["markers_validated"] is bool)|| (bool)root["markers_validated"]!=false||String(root,"availability")!="observed")throw new InvalidDataException();
        var observed=Time(String(root,"observed_utc"));bool expired;if(!Freshness(observed,now,out expired))throw new InvalidDataException();
        int books=Number(root,"books"),chapters=Number(root,"planned_chapters"),attempts=Number(root,"attempt_directories");var stages=root["stages"] as Dictionary<string,object>;if(stages==null)throw new InvalidDataException();
        string[] names={"analyze","memory","stage","render"};string[] labels={"分析","记忆","整理","渲染"};var parts=new List<string>();for(int i=0;i<names.Length;i++){var pair=stages[names[i]] as object[];if(pair==null||pair.Length!=2)throw new InvalidDataException();int done=StrictInteger(pair[0]),reserved=StrictInteger(pair[1]);if(done<0||reserved<0||done>chapters||reserved>chapters||(long)done+(long)reserved>(long)chapters)throw new InvalidDataException();parts.Add(labels[i]+" "+done+" 已见标记 / "+reserved+" 预留标记");}
        result.AudiobookState="available";result.AudiobookObservedAt=observed;result.AudiobookExpired=expired;result.AudiobookNotice=expired?"有声书观察快照已过期":"只统计固定序列根中的标记存在性；未验证实际进程、音频或完成情况";
        result.Books=books;result.PlannedChapters=chapters;result.AttemptDirectories=attempts;result.StageSummary="标记数量（未验证）："+System.String.Join(" · ",parts);
      }catch{result.AudiobookState="unknown";result.AudiobookObservedAt=null;result.AudiobookNotice="有声书观察缺失、损坏、过期或格式未知";result.Books=result.PlannedChapters=result.AttemptDirectories=null;result.StageSummary="阶段标记未知";}
    }
    static int Number(Dictionary<string,object> root,string key){object value;if(!root.TryGetValue(key,out value))throw new InvalidDataException();int n=StrictInteger(value);if(n<0)throw new InvalidDataException();return n;}
    static int StrictInteger(object value){if(value is int)return (int)value;if(value is long){long n=(long)value;if(n<int.MinValue||n>int.MaxValue)throw new InvalidDataException();return (int)n;}throw new InvalidDataException();}
  }
}
