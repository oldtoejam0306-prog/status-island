Option Explicit
Dim shell, files, folder, executable, command
If WScript.Arguments.Count <> 0 Then WScript.Quit 64
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
executable = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
If Not files.FileExists(executable) Or Not files.FileExists(folder & "\Candidate.Start.ps1") Then
  MsgBox "Sidebar files are incomplete. Use Launch.cmd for diagnostics.", 16, "Sidebar"
  WScript.Quit 2
End If
shell.CurrentDirectory = folder
command = Chr(34) & executable & Chr(34) & " -NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy RemoteSigned -File " & Chr(34) & folder & "\Candidate.Start.ps1" & Chr(34)
shell.Run command, 0, False
