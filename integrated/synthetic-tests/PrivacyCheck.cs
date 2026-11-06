using System;
using System.Reflection;
class PrivacyCheck {
 static int Main() {
  var safe=typeof(MediaContextDiagnostic).GetMethod("Safe",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(var item in new[]{new[]{"song\r\nartist\tname","song  artist name"},new[]{@"C:\Users\Private\song.mp3","[path]"},new[]{@"\\host\private\music","[path]"},new[]{"artist@example.com","[address]"},new[]{"https://example.com/private?id=1","[address]"}}) {
   var value=(string)safe.Invoke(null,new object[]{item[0]});if(value!=item[1])throw new Exception("metadata redaction mismatch");
  }
  if(((string)safe.Invoke(null,new object[]{new string('x',200)})).Length!=160)throw new Exception("limit");
  Console.WriteLine("PASS diagnostic metadata redaction: controls, drive path, UNC path, email, URL, length (6)");return 0;
 }
}
