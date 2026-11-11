using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace IslandIntegrated
{
    // Independent, injectable CC Switch launch/activation helper. No path is inferred.
    public enum CcSwitchState { Running, Available, NotFound, Error }
    public enum CcSwitchClickResult { Focused, Started, AlreadyStarting, Failed }
    public sealed class CcSwitchWindow
    {
        public int ProcessId { get; set; }
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public bool Visible { get; set; }
        public bool Minimized { get; set; }
        public bool TopLevel { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
    public sealed class CcSwitchProcess
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public IntPtr MainWindow { get; set; }
        // Null when Windows denied access or the executable path could not be read.
        public string ExecutablePath { get; set; }
        public long StartTimeUtcTicks { get; set; }
        public List<CcSwitchWindow> Windows { get; set; }
    }

    // Per-process denial memory: do not repeat a denied MainModule probe on every UI refresh.
    // Entries are scoped to the observed process ID and start time, and pruned when that
    // process instance disappears. A zero start time means even StartTime was inaccessible.
    internal static class CcSwitchProcessPathProbe
    {
        static readonly object sync = new object();
        static readonly Dictionary<int, long> denied = new Dictionary<int, long>();

        internal static bool StartTimeWasDenied(int processId)
        {
            lock (sync) { long value; return denied.TryGetValue(processId, out value) && value == 0; }
        }

        internal static bool IsDenied(int processId, long startTimeUtcTicks)
        {
            lock (sync)
            {
                long value;
                return denied.TryGetValue(processId, out value) && (value == 0 || value == startTimeUtcTicks);
            }
        }

        internal static string Read(int processId, long startTimeUtcTicks, Func<string> readPath)
        {
            lock (sync)
            {
                long value;
                if (denied.TryGetValue(processId, out value) && (value == 0 || value == startTimeUtcTicks)) return null;
                try { return readPath(); }
                catch (UnauthorizedAccessException) { denied[processId] = startTimeUtcTicks; return null; }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    if (ex.NativeErrorCode == 5) { denied[processId] = startTimeUtcTicks; return null; }
                    throw;
                }
            }
        }

        internal static void MarkDenied(int processId, long startTimeUtcTicks)
        {
            lock (sync) denied[processId] = startTimeUtcTicks;
        }

        internal static void Prune(IEnumerable<CcSwitchProcess> current)
        {
            var observed = new Dictionary<int, long>();
            foreach (var process in current ?? Enumerable.Empty<CcSwitchProcess>())
                if (process != null) observed[process.Id] = process.StartTimeUtcTicks;
            lock (sync)
            {
                foreach (var id in denied.Keys.ToArray())
                {
                    long ticks;
                    if (!observed.TryGetValue(id, out ticks) || (denied[id] != 0 && denied[id] != ticks)) denied.Remove(id);
                }
            }
        }
    }
    public sealed class CcSwitchStatus
    {
        public CcSwitchState State { get; internal set; }
        public string Message { get; internal set; }
        public string LaunchPath { get; internal set; }
        public int RunningCount { get; internal set; }
    }

    public sealed class CcSwitchLauncher
    {
        readonly Func<IEnumerable<CcSwitchProcess>> processes;
        readonly Func<string, bool> fileExists;
        readonly Func<IEnumerable<string>> startMenuLinks;
        readonly Func<IntPtr, bool> focusWindow;
        readonly Func<string, bool> launch;
        readonly object gate = new object();
        long lastLaunchTicks;

        public CcSwitchLauncher(Func<IEnumerable<CcSwitchProcess>> processes,
            Func<string, bool> fileExists, Func<IEnumerable<string>> startMenuLinks,
            Func<IntPtr, bool> focusWindow, Func<string, bool> launch)
        {
            if (processes == null) throw new ArgumentNullException("processes");
            if (fileExists == null) throw new ArgumentNullException("fileExists");
            if (startMenuLinks == null) throw new ArgumentNullException("startMenuLinks");
            if (focusWindow == null) throw new ArgumentNullException("focusWindow");
            if (launch == null) throw new ArgumentNullException("launch");
            this.processes = processes;
            this.fileExists = fileExists;
            this.startMenuLinks = startMenuLinks;
            this.focusWindow = focusWindow;
            this.launch = launch;
        }

        public static CcSwitchLauncher CreateDefault(string configuredPath)
        {
            return new CcSwitchLauncher(ReadProcesses,
                p => File.Exists(p), ReadStartMenuLinks, Focus, LaunchConfiguredPath);
        }

        public System.Threading.Tasks.Task<CcSwitchStatus> GetStatusAsync(string configuredPath)
        {
            return System.Threading.Tasks.Task.Run(() => GetStatus(configuredPath));
        }

        public CcSwitchStatus GetStatus(string configuredPath)
        {
            try
            {
                var running = ReadMatchingProcesses();
                if (running.Count > 0)
                    return new CcSwitchStatus { State = CcSwitchState.Running, Message = "CC Switch is running", RunningCount = running.Count };
                string path = ResolvePath(configuredPath);
                return path == null
                    ? new CcSwitchStatus { State = CcSwitchState.NotFound, Message = "CC Switch is not running; no configured or discoverable shortcut was found" }
                    : new CcSwitchStatus { State = CcSwitchState.Available, Message = "CC Switch is not running; click to start", LaunchPath = path };
            }
            catch (Exception ex)
            {
                return new CcSwitchStatus { State = CcSwitchState.Error, Message = "CC Switch status unavailable: " + SafeMessage(ex) };
            }
        }

        // One explicit click: focus any running instance; otherwise launch the selected path once.
        public CcSwitchClickResult Click(string configuredPath, out string message)
        {
            message = null;
            lock (gate)
            {
                if (lastLaunchTicks != 0 && (Stopwatch.GetTimestamp()-lastLaunchTicks)*1000.0/Stopwatch.Frequency < 400) { message = "CC Switch 正在启动"; return CcSwitchClickResult.AlreadyStarting; }
                List<CcSwitchProcess> running;
                try { running = ReadMatchingProcesses(); }
                catch (Exception ex) { message = "Could not check CC Switch: " + SafeMessage(ex); return CcSwitchClickResult.Failed; }
                string path;
                if (running.Count > 0)
                {
                    var target = SelectTargetWindow(running);
                    if (target != null)
                    {
                        try { if (focusWindow(target.Handle)) { message = "Focused CC Switch"; return CcSwitchClickResult.Focused; } }
                        catch (Exception ex) { message = "Could not focus CC Switch: " + SafeMessage(ex); return CcSwitchClickResult.Failed; }
                        message = "Windows did not allow CC Switch to be focused"; return CcSwitchClickResult.Failed;
                    }
                    try { path = ResolveNoWindowPath(configuredPath, running); }
                    catch (Exception ex) { message = "Could not resolve the running CC Switch path: " + SafeMessage(ex); return CcSwitchClickResult.Failed; }
                    if (path == null) { message = "CC Switch is running without a window and has no reliable executable or shortcut path"; return CcSwitchClickResult.Failed; }
                    return Start(path, out message);
                }
                try { path = ResolvePath(configuredPath); }
                catch (Exception ex) { message = "Could not find CC Switch: " + SafeMessage(ex); return CcSwitchClickResult.Failed; }
                if (path == null) { message = "No CC Switch executable or shortcut is configured or discoverable"; return CcSwitchClickResult.Failed; }
                return Start(path, out message);
            }
        }

        CcSwitchClickResult Start(string path, out string message)
        {
            lastLaunchTicks = Stopwatch.GetTimestamp();
            try
            {
                if (!launch(path)) { lastLaunchTicks = 0; message = "Windows could not start CC Switch"; return CcSwitchClickResult.Failed; }
                message = "Started CC Switch"; return CcSwitchClickResult.Started;
            }
            catch (Exception ex) { lastLaunchTicks = 0; message = "Could not start CC Switch: " + SafeMessage(ex); return CcSwitchClickResult.Failed; }
        }

        string ResolveNoWindowPath(string configured, IEnumerable<CcSwitchProcess> running)
        {
            if (!String.IsNullOrWhiteSpace(configured)) return IsUsable(configured) ? Path.GetFullPath(configured) : null;
            foreach (var process in running)
                if (process != null && IsExecutable(process.ExecutablePath) && fileExists(process.ExecutablePath))
                    return Path.GetFullPath(process.ExecutablePath);
            foreach (var path in startMenuLinks() ?? Enumerable.Empty<string>())
                if (IsShortcut(path) && fileExists(path)) return Path.GetFullPath(path);
            return null;
        }

        string ResolvePath(string configured)
        {
            if (!String.IsNullOrWhiteSpace(configured)) return IsUsable(configured) ? Path.GetFullPath(configured) : null;
            foreach (var path in startMenuLinks() ?? Enumerable.Empty<string>())
                if (IsUsable(path)) return Path.GetFullPath(path);
            return null;
        }
        bool IsUsable(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            if (!IsExecutable(path) && !IsShortcut(path)) return false;
            return fileExists(path);
        }
        static bool IsExecutable(string path) { return !String.IsNullOrWhiteSpace(path) && String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase); }
        static bool IsShortcut(string path) { return !String.IsNullOrWhiteSpace(path) && String.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase); }
        List<CcSwitchProcess> ReadMatchingProcesses()
        {
            return (processes() ?? Enumerable.Empty<CcSwitchProcess>())
                .Where(p => p != null && (String.Equals(p.Name, "cc-switch", StringComparison.OrdinalIgnoreCase) || String.Equals(p.Name, "ccswitch", StringComparison.OrdinalIgnoreCase))).ToList();
        }
        internal static CcSwitchWindow SelectTargetWindow(IEnumerable<CcSwitchProcess> processes)
        {
            return (processes ?? Enumerable.Empty<CcSwitchProcess>())
                .Where(p => p != null && (String.Equals(p.Name, "cc-switch", StringComparison.OrdinalIgnoreCase) || String.Equals(p.Name, "ccswitch", StringComparison.OrdinalIgnoreCase)))
                .SelectMany(p => (p.Windows ?? new List<CcSwitchWindow>()).Where(w => w != null && w.ProcessId == p.Id))
                .Where(w => w != null && w.Handle != IntPtr.Zero && w.TopLevel &&
                    String.Equals((w.Title ?? "").Trim(), "CC Switch", StringComparison.OrdinalIgnoreCase) &&
                    w.Width >= 160 && w.Height >= 120)
                .OrderByDescending(w => w.Visible)
                .ThenByDescending(w => (long)w.Width * w.Height)
                .ThenBy(w => w.ProcessId)
                .FirstOrDefault();
        }
        static string SafeMessage(Exception ex) { return String.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message; }

        static IEnumerable<CcSwitchProcess> ReadProcesses()
        {
            var result = new List<CcSwitchProcess>();
            foreach (string name in new[] { "cc-switch", "ccswitch" })
            {
                Process[] list = Process.GetProcessesByName(name);
                try
                {
                    foreach (var p in list)
                    {
                        IntPtr hwnd = IntPtr.Zero;
                        long startTicks = 0;
                        string exe = null;
                        try { hwnd = p.MainWindowHandle; } catch { }
                        if (!CcSwitchProcessPathProbe.StartTimeWasDenied(p.Id))
                        {
                            try { startTicks = p.StartTime.ToUniversalTime().Ticks; }
                            catch (UnauthorizedAccessException) { CcSwitchProcessPathProbe.MarkDenied(p.Id, 0); }
                            catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode == 5) CcSwitchProcessPathProbe.MarkDenied(p.Id, 0); }
                        }
                        if (!CcSwitchProcessPathProbe.StartTimeWasDenied(p.Id) &&
                            !PathProbeDenied(p.Id, startTicks))
                        {
                            try { exe = CcSwitchProcessPathProbe.Read(p.Id, startTicks, () => p.MainModule.FileName); }
                            catch { /* other lookup failures remain unknown; never elevate or infer */ }
                        }
                        result.Add(new CcSwitchProcess { Name = name, Id = p.Id, MainWindow = hwnd, ExecutablePath = exe, StartTimeUtcTicks = startTicks, Windows = ReadTopLevelWindows(p.Id) });
                    }
                }
                finally { foreach (var p in list) p.Dispose(); }
            }
            CcSwitchProcessPathProbe.Prune(result);
            return result;
        }
        static bool PathProbeDenied(int processId, long startTicks)
        {
            // Read is the authoritative cache check; this helper only lets us skip the lambda
            // invocation after a denial while keeping Process.MainModule entirely untouched.
            return CcSwitchProcessPathProbe.IsDenied(processId, startTicks);
        }
        [StructLayout(LayoutKind.Sequential)] struct WindowRect { public int Left, Top, Right, Bottom; }
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);
        static List<CcSwitchWindow> ReadTopLevelWindows(int processId)
        {
            var windows = new List<CcSwitchWindow>();
            EnumWindows((hwnd, state) =>
            {
                uint ownerPid;
                GetWindowThreadProcessId(hwnd, out ownerPid);
                if (ownerPid != (uint)processId) return true;
                WindowRect rect;
                if (!GetWindowRect(hwnd, out rect)) return true;
                var title = new StringBuilder(512);
                GetWindowText(hwnd, title, title.Capacity);
                windows.Add(new CcSwitchWindow
                {
                    ProcessId = processId,
                    Handle = hwnd,
                    Title = title.ToString(),
                    Visible = IsWindowVisible(hwnd),
                    Minimized = IsIconic(hwnd),
                    TopLevel = true, // EnumWindows enumerates top-level windows, not child controls.
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top
                });
                return true;
            }, IntPtr.Zero);
            return windows;
        }
        static IEnumerable<string> ReadStartMenuLinks()
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (var path in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                    if (Path.GetFileNameWithoutExtension(path).IndexOf("cc", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        Path.GetFileNameWithoutExtension(path).IndexOf("switch", StringComparison.OrdinalIgnoreCase) >= 0) yield return path;
            }
        }
        static bool LaunchConfiguredPath(string path)
        {
            string ext = Path.GetExtension(path);
            if (!String.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) && !String.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)) return false;
            var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            if (p != null) p.Dispose();
            return p != null;
        }
        static bool Focus(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            ShowWindow(hwnd, IsIconic(hwnd) ? 9 : 5);
            return SetForegroundWindow(hwnd);
        }
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    }
}
