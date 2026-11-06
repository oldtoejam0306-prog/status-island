@echo off
setlocal
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy RemoteSigned -File "%~dp0Candidate.Diagnostic.ps1"
set "SIDEBAR_RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %SIDEBAR_RESULT%
