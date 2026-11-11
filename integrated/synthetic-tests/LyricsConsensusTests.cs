using System;
using IslandIntegrated;
using IslandMedia;
class LyricsConsensusTests {
 static void Check(bool c,string m){if(!c)throw new Exception(m);Console.WriteLine("PASS "+m);}
 static string Row(int id,string time,string plain="line",string artist="Artist",int duration=120){return "{\"id\":"+id+",\"trackName\":\"Song\",\"artistName\":\""+artist+"\",\"duration\":"+duration+",\"plainLyrics\":\""+plain+"\",\"syncedLyrics\":\"[00:"+time+"]line\"}";}
 static LyricResult Match(string records, double? duration=120){return LyricMatcher.Match("["+records+"]",new Snapshot{SourceId="cloudmusic.exe",Title="Song",Artist="Artist",DurationSeconds=duration});}
 static int Main(){
 var r=Match(Row(1,"01.00")+","+Row(2,"01.00")+","+Row(3,"02.00"));
 Check(r.State=="approximate" && r.Document.Lines[0].Seconds==1,"repeated actual timeline is approximate");
 Check(LrcText.Display(r,2)=="line","clean lyric display");
 Check(Match(Row(1,"01.00")+","+Row(2,"02.00")).State=="ambiguous","tie remains ambiguous");
 Check(Match(Row(1,"01.00")+","+Row(2,"01.00")+","+Row(3,"02.00","different")).State=="ambiguous","different words rejected");
 Check(Match(Row(1,"01.00")+","+Row(2,"01.00")+","+Row(3,"02.00"),null).State=="ambiguous","unknown duration rejected");
 Check(Match(Row(1,"01.00")+","+Row(2,"01.00")+","+Row(3,"02.00","line","Other")).State=="ready","other artist excluded");
 Check(Match(Row(1,"01.00")+","+Row(2,"01.00")+","+Row(3,"02.00","line","Artist",150)).State=="ready","other duration excluded");
 return 0; }
}
