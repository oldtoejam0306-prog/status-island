' 静默启动器：避免 Windows Terminal / conhost 弹出黑窗（纯 ASCII，路径用环境变量展开）
Dim sh, ps1, cmd, extra, a
extra = ""
For Each a In WScript.Arguments
  extra = extra & " " & a
Next
Set sh = CreateObject("WScript.Shell")
ps1 = sh.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\StatusIsland\StatusIsland.ps1"
cmd = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """ " & extra
sh.Run cmd, 0, False
