using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using IslandIntegrated;

// Standalone synthetic test entry point. Example:
// csc /nologo /target:exe /out:CcSwitchTests.exe CcSwitchLauncher.cs synthetic-tests\CcSwitchTests.cs
// CcSwitchTests.exe
internal static class CcSwitchTests
{
    static int assertions;

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        assertions++;
        Console.WriteLine("PASS " + name);
    }

    static CcSwitchLauncher Make(Func<IEnumerable<CcSwitchProcess>> processes,
        Func<string, bool> exists, Func<IEnumerable<string>> shortcuts,
        Func<IntPtr, bool> focus, Func<string, bool> start)
    {
        return new CcSwitchLauncher(processes, exists, shortcuts, focus, start);
    }

    static CcSwitchWindow Window(int pid, int handle, string title, bool visible, bool topLevel, int width, int height)
    {
        return new CcSwitchWindow { ProcessId = pid, Handle = new IntPtr(handle), Title = title, Visible = visible, TopLevel = topLevel, Width = width, Height = height };
    }

    static void Main()
    {
        const string configured = @"C:\fixture\configured.exe";
        const string runningExe = @"C:\fixture\running.exe";
        const string shortcut = @"C:\fixture\CC Switch.lnk";
        Func<string, bool> exists = p => p == configured || p == runningExe || p == shortcut;
        Func<IEnumerable<string>> links = () => new[] { shortcut };
        Func<IEnumerable<CcSwitchProcess>> stopped = () => new CcSwitchProcess[0];
        string message;
        int starts = 0;

        var configuredLauncher = Make(stopped, exists, links, h => true, p => { starts++; return true; });
        var status = configuredLauncher.GetStatus(configured);
        Check(status.State == CcSwitchState.Available && status.LaunchPath == configured,
            "configured executable takes precedence over discovered shortcut");
        status = configuredLauncher.GetStatus(@"C:\fixture\missing.exe");
        Check(status.State == CcSwitchState.Error || status.State == CcSwitchState.NotFound,
            "invalid configured path fails closed");
        Check(configuredLauncher.Click(@"C:\fixture\missing.exe", out message) == CcSwitchClickResult.Failed && starts == 0,
            "invalid configured path does not fall back or launch");

        var empty = Make(stopped, exists, () => new string[0], h => true, p => { starts++; return true; });
        Check(empty.GetStatus(null).State == CcSwitchState.NotFound, "no configured path and no shortcut reports not found");
        Check(empty.Click(null, out message) == CcSwitchClickResult.Failed && starts == 0,
            "no path does not launch");

        var runningWindows = new[] {
            Window(19, 901, "com.ccswitch.desktop-siw", true, true, 16, 16),
            Window(19, 902, "", true, true, 800, 600),
            Window(19, 903, "Unrelated", true, true, 1200, 800),
            Window(19, 904, "CC Switch", true, false, 1200, 800),
            Window(19, 905, "CC Switch", false, true, 1000, 669),
            Window(19, 906, "CC Switch", false, true, 620, 760)
        };
        var selected = CcSwitchLauncher.SelectTargetWindow(new[] { new CcSwitchProcess { Name = "cc-switch", Id = 19, Windows = new List<CcSwitchWindow>(runningWindows) } });
        Check(selected != null && selected.Handle == new IntPtr(905),
            "select largest same-process titled top-level window when app windows are tray-hidden");
        var visiblePreferred = CcSwitchLauncher.SelectTargetWindow(new[] { new CcSwitchProcess { Name = "ccswitch", Id = 19, Windows = new List<CcSwitchWindow>(runningWindows) { Window(19, 907, "CC Switch", true, true, 240, 180) } } });
        Check(visiblePreferred != null && visiblePreferred.Handle == new IntPtr(907),
            "prefer a visible app window over a larger hidden app window");
        Check(CcSwitchLauncher.SelectTargetWindow(new[] { new CcSwitchProcess { Name = "cc-switch", Id = 19, Windows = new List<CcSwitchWindow> { Window(19, 908, "CC Switch", true, true, 16, 16), Window(19, 909, "Other", true, true, 1000, 800) } } }) == null,
            "reject internal small window and unrelated titled window");

        Check(CcSwitchLauncher.SelectTargetWindow(new[] { new CcSwitchProcess { Name = "cc-switch", Id = 19, Windows = new List<CcSwitchWindow> { Window(99, 910, "CC Switch", true, true, 800, 600) } } }) == null, "reject window owned by different process");

        int focusCalls = 0;
        var runningWindow = Make(
            () => new[] { new CcSwitchProcess { Name = "CCSWITCH", Id = 19, MainWindow = new IntPtr(901), Windows = new List<CcSwitchWindow>(runningWindows) } },
            exists, links,
            h => { focusCalls++; return h == new IntPtr(905); },
            p => { starts++; return true; });
        Check(runningWindow.Click(configured, out message) == CcSwitchClickResult.Focused && starts == 0 && focusCalls == 1,
            "running instance focuses selected real window without launching");

        var noWindow = Make(
            () => new[] { new CcSwitchProcess { Name = "cc-switch", Id = 20, MainWindow = IntPtr.Zero, ExecutablePath = runningExe } },
            exists, links, h => true,
            p => { starts++; return p == configured; });
        Check(noWindow.Click(configured, out message) == CcSwitchClickResult.Started && starts == 1,
            "windowless process prefers valid configured path over process and shortcut paths");

        var invalidWindowless = Make(() => new[] { new CcSwitchProcess { Name="cc-switch", Id=24, MainWindow=IntPtr.Zero, ExecutablePath=runningExe } }, exists, links, h=>true, p=>{ starts++; return true; });
        Check(invalidWindowless.Click(@"C:\fixture\missing.exe", out message)==CcSwitchClickResult.Failed, "windowless invalid explicit configuration fails closed");

        string selectedPath = null;
        var processPathFallback = Make(
            () => new[] { new CcSwitchProcess { Name = "cc-switch", Id = 21, MainWindow = IntPtr.Zero, ExecutablePath = runningExe } },
            exists, links, h => true,
            p => { selectedPath = p; return true; });
        Check(processPathFallback.Click(null, out message) == CcSwitchClickResult.Started && selectedPath == runningExe,
            "windowless process falls back to its readable existing executable path");

        selectedPath = null;
        var shortcutFallback = Make(
            () => new[] { new CcSwitchProcess { Name = "ccswitch", Id = 22, MainWindow = IntPtr.Zero, ExecutablePath = @"C:\denied\missing.exe" } },
            exists, links, h => true,
            p => { selectedPath = p; return true; });
        Check(shortcutFallback.Click(null, out message) == CcSwitchClickResult.Started && selectedPath == shortcut,
            "windowless process uses explicitly discovered shortcut when its executable path is unavailable");

        var untrustedFallback = Make(
            () => new[] { new CcSwitchProcess { Name = "ccswitch", Id = 23, MainWindow = IntPtr.Zero, ExecutablePath = @"C:\denied\missing.exe" } },
            exists, () => new string[0], h => true,
            p => { starts++; return true; });
        Check(untrustedFallback.Click(null, out message) == CcSwitchClickResult.Failed &&
            message.IndexOf("no reliable", StringComparison.OrdinalIgnoreCase) >= 0,
            "windowless process with inaccessible path and no shortcut fails without guessing");

        var failStarts = 0;
        var failedLaunch = Make(stopped, exists, links, h => true, p => { failStarts++; return false; });
        Check(failedLaunch.Click(configured, out message) == CcSwitchClickResult.Failed &&
            message.IndexOf("could not start", StringComparison.OrdinalIgnoreCase) >= 0,
            "launch failure is returned to caller");
        Check(failedLaunch.Click(configured, out message) == CcSwitchClickResult.Failed && failStarts == 2,
            "failed launch releases the start gate for retry");

        var doubleStarts = 0;
        var doubleClick = Make(stopped, exists, links, h => true, p => { doubleStarts++; return true; });
        Check(doubleClick.Click(configured, out message) == CcSwitchClickResult.Started,
            "first synthetic click starts through injected delegate");
        Check(doubleClick.Click(configured, out message) == CcSwitchClickResult.AlreadyStarting && doubleStarts == 1,
            "synthetic double click is suppressed inside 400ms gate");

        // Move only the synthetic launch timestamp back; this exercises expiry without sleeping.
        var field = typeof(CcSwitchLauncher).GetField("lastLaunchTicks", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, "400ms gate timestamp is available to synthetic test");
        long elapsedTicks = (long)Math.Ceiling(Stopwatch.Frequency * 401.0 / 1000.0);
        field.SetValue(doubleClick, Stopwatch.GetTimestamp() - elapsedTicks);
        Check(doubleClick.Click(configured, out message) == CcSwitchClickResult.Started && doubleStarts == 2,
            "start is allowed after the 400ms gate expires");

        int deniedReads = 0;
        Func<string> deniedRead = () => { deniedReads++; throw new UnauthorizedAccessException("synthetic access denied"); };
        Check(CcSwitchProcessPathProbe.Read(97001, 1234, deniedRead) == null && deniedReads == 1,
            "first denied process path read is recorded as unknown");
        Check(CcSwitchProcessPathProbe.Read(97001, 1234, deniedRead) == null && deniedReads == 1,
            "same process ID and start time do not repeat denied path read");
        Check(CcSwitchProcessPathProbe.Read(97001, 5678, () => { deniedReads++; return runningExe; }) == runningExe && deniedReads == 2,
            "new process start time permits one fresh path read");
        CcSwitchProcessPathProbe.Prune(new CcSwitchProcess[0]);
        Check(CcSwitchProcessPathProbe.Read(97001, 1234, () => { deniedReads++; return runningExe; }) == runningExe && deniedReads == 3,
            "process exit prunes denial so a later process instance may be read");

        Console.WriteLine("CcSwitch synthetic checks passed: " + assertions);
    }
}
