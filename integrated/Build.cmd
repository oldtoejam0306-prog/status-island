@echo off
setlocal
cd /d "%~dp0"
set "SIDEBAR_NET=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%SIDEBAR_NET%\csc.exe" set "SIDEBAR_NET=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319"
set "SIDEBAR_CSC=%SIDEBAR_NET%\csc.exe"
set "SIDEBAR_META=%SystemRoot%\System32\WinMetadata"
set "SIDEBAR_RUNTIME=%SIDEBAR_NET%\Facades\System.Runtime.dll"
if not exist "%SIDEBAR_RUNTIME%" set "SIDEBAR_RUNTIME=%SIDEBAR_NET%\System.Runtime.dll"
if not exist "%SIDEBAR_CSC%" goto missing
if not exist "%SIDEBAR_RUNTIME%" goto missing
for %%F in (Windows.Media.winmd Windows.Foundation.winmd Windows.Storage.winmd) do if not exist "%SIDEBAR_META%\%%F" goto missing
if not exist "state\build" mkdir "state\build"
if not exist "state\build" goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:library /out:"state\build\NeteaseMedia.dll" /reference:System.Core.dll /reference:"%SIDEBAR_META%\Windows.Media.winmd" /reference:"%SIDEBAR_META%\Windows.Foundation.winmd" /reference:"%SIDEBAR_META%\Windows.Storage.winmd" /reference:"%SIDEBAR_RUNTIME%" /reference:"%SIDEBAR_NET%\System.Runtime.InteropServices.WindowsRuntime.dll" NeteaseMedia.cs MediaPipeline.cs
if errorlevel 1 goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:exe /out:"state\build\MediaContextDiagnostic.exe" /reference:System.Core.dll /reference:"%SIDEBAR_META%\Windows.Media.winmd" /reference:"%SIDEBAR_META%\Windows.Foundation.winmd" /reference:"%SIDEBAR_META%\Windows.Storage.winmd" /reference:"%SIDEBAR_RUNTIME%" MediaContextDiagnostic.cs
if errorlevel 1 goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:library /out:"state\build\StatusNative.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll StatusNative.1.cs StatusNative.2.cs
if errorlevel 1 goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:library /out:"state\build\Controller.Check.dll" /reference:"state\build\StatusNative.dll" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll /reference:"state\build\NeteaseMedia.dll" /reference:System.Web.Extensions.dll /reference:"%SIDEBAR_NET%\WPF\PresentationCore.dll" /reference:"%SIDEBAR_NET%\WPF\WindowsBase.dll" CcSwitchLauncher.cs ThemePicker.cs AihotSidebar.cs AihotSurface.cs IntegratedController.cs PinkyNativeHost.cs PinkyHostConnection.cs PinkySidebarSession.cs PerfLegacySupport.cs GridSupport.cs StatusRestore.cs StatusMaterials.cs AgentActivity.cs AgentLifecycleCore.cs AgentLifecyclePublisher.cs AgentLifecycleReader.cs BalanceReadonly.cs LegacyBalanceOwner.cs StatusServiceAdapters.cs StatusServiceTransports.cs MusicCardControl.cs LrclibLyrics.cs WeatherCardControl.cs AutoloopSidebarControl.cs AutoloopBridge.cs AutoloopDetailView.cs AutoloopProjectSnapshots.cs OpenMeteoWeatherProvider.cs WeatherDetailModel.cs WeatherCityHistory.cs WeatherDetailView.cs CpuUtilitySource.cs LocalSystemSource.cs StatusLocalExtras.cs NetworkReadonlyView.cs StatusPresence.cs RouteDropdown.cs NormalUpdateHandoff.cs StatusUiRenderer.cs DragPreview.cs CardDragInput.cs EditCardChrome.cs LayoutModel.cs CalendarModel.cs ClockCalendarControl.cs GlassSurface.cs GlassColumnForm.cs CornerMotion.cs RevealMotion.cs ActionButton.cs ManagementModel.cs FramePresentation.cs
if errorlevel 1 goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:exe /out:"state\build\ValidationRunner.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll /reference:"state\build\Controller.Check.dll" /reference:"state\build\NeteaseMedia.dll" ValidationRunner.cs
if errorlevel 1 goto failed
"%SIDEBAR_CSC%" /nologo /codepage:65001 /target:exe /out:"state\build\LayoutTests.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll /reference:System.Core.dll /reference:"state\build\Controller.Check.dll" synthetic-tests\LayoutTests.cs
if errorlevel 1 goto failed
echo Build completed using the installed Windows .NET Framework compiler.
exit /b 0
:missing
echo Missing installed Windows .NET compiler, runtime facade or WinRT metadata.
echo Windows 10 1809 or later with .NET Framework 4.8 is expected. Nothing was installed.
exit /b 2
:failed
echo Build failed. No existing application was stopped or replaced.
exit /b 1
