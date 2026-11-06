@echo off
setlocal
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -ExecutionPolicy RemoteSigned -File "%~dp0Candidate.Start.ps1"
set "SIDEBAR_RESULT=%ERRORLEVEL%"
if not "%SIDEBAR_RESULT%"=="0" (
  echo.
  echo Candidate did not start. Keep the error above and see README.md.
  echo No software was downloaded and no system execution policy was changed.
  pause
)
exit /b %SIDEBAR_RESULT%
