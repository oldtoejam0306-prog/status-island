using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using IslandIntegrated;
using IslandMedia;

internal static class PinkySessionFlowTests
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private const int TotalLimitMs = 19000;
    private const int WsChild = 0x40000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int GwlStyle = -16;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpShowWindow = 0x0040;
    private const int SwpHideWindow = 0x0080;
    private static readonly IntPtr DpiPerMonitorV2 = new IntPtr(-4);

    private static int checks;
    private static string fixtureDirectory;
    private static string fixtureControlFile;
    private static string childResultFile;
    private static Process childProcess;
    private static App app;
    private static System.Windows.Forms.Timer uiPumpTimer;
    private static int uiPumpTicks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "child")
            return RunSimulatedChild(args[1]);
        if (args.Length != 0)
            return 64;

        try
        {
            RunParentFixture();
            Console.WriteLine("{\"ok\":true,\"checks\":" + checks + ",\"elapsedMs\":" + Clock.ElapsedMilliseconds + ",\"visibleUi\":false,\"realPinky\":false,\"humanInputTested\":false}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("{\"ok\":false,\"checks\":" + checks + ",\"elapsedMs\":" + Clock.ElapsedMilliseconds + ",\"error\":\"" + Escape(error.ToString()) + "\",\"fixtureEvidence\":\"" + Escape(ReadFixtureEvidence()) + "\"}");
            return 1;
        }
        finally
        {
            CleanupParent();
        }
    }

    private static void RunParentFixture()
    {
        string localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost");
        string fixtureId = "fixture-" + Guid.NewGuid().ToString("N");
        fixtureDirectory = Path.Combine(localRoot, fixtureId);
        Directory.CreateDirectory(fixtureDirectory);

        string tempRoot = Path.Combine(Path.GetTempPath(), "pinky-session-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string layoutFile = Path.Combine(tempRoot, "layout.xml");
        string configFile = Path.Combine(tempRoot, "pinky-host-connection.json");
        fixtureControlFile = Path.Combine(fixtureDirectory, "test-control.stop");
        childResultFile = Path.Combine(fixtureDirectory, "child-result.json");

        var config = new Dictionary<string, object>();
        config["schema"] = "pinky.sidebar-connection.v1";
        config["enabled"] = true;
        config["directory"] = fixtureDirectory;
        config["pinkyExecutable"] = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        File.WriteAllText(configFile, new JavaScriptSerializer().Serialize(config), new UTF8Encoding(false));

        StartSimulatedChild();
        var fakeMedia = new NeteaseSource(
            () => DateTime.UtcNow,
            () => System.Threading.Tasks.Task.FromResult(new Snapshot { SourceId = "fixture", Title = "Simulated" }));
        app = new App(layoutFile, null, fakeMedia);
        GetField<System.Windows.Forms.Timer>(app, "timer").Stop();
        app.PrepareInitialLayout(Screen.PrimaryScreen);
        string[] originalCardIds = GetField<Layout>(app, "layout").Cards.Select(card => card.Id).ToArray();
        uiPumpTimer = new System.Windows.Forms.Timer { Interval = 25 };
        uiPumpTimer.Tick += (sender, eventArgs) => uiPumpTicks++;
        uiPumpTimer.Start();
        Check(GetField<PinkySidebarSession>(app, "pinkySession").Enabled, "Configured session accepted its fixture connection");
        Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Parent starts with no requested panel before child lease");

        var state = GetField<HoverState>(app, "state");
        state.Open = true;
        PumpUntil(() => GetProperty<bool>(app, "PinkyPanelRequested"), "requested phase", true);
        Check(GetProperty<bool>(app, "PinkyPanelRequested"), "Child requested phase reaches App");
        Check(GetProperty<Size>(app, "PinkyPanelSize").Width > 0, "Requested phase reserves an inline slot");
        string requestedPhase = ReadPanelPhase();
        Check(requestedPhase == "requested" || requestedPhase == "attached",
            "Panel lease is requested or has already advanced to attached");

        PumpUntil(() => String.Equals(GetField<PinkyPanelLease>(app, "pinkyPanel").Phase, "attached", StringComparison.Ordinal), "attached phase", true);
        Check(String.Equals(GetField<PinkyPanelLease>(app, "pinkyPanel").Phase, "attached", StringComparison.Ordinal),
            "Child voluntary attach is acknowledged by App");
        Check(File.Exists(childResultFile), "Child wrote DPI and attachment evidence");
        IDictionary<string, object> result = Parse(ReadSharedText(childResultFile));
        Check(Boolean(result, "dpiContextsEqual"), "Actual host and child window DPI contexts match before attach");
        Check(Boolean(result, "childAttached"), "Child performed SetParent on its own panel window");
        Check(Boolean(result, "hostVisibleObserved"), "Child observed a visible host lease");
        Check(Boolean(result, "observedChildVisibleWhileAttached"),
            "Child HWND was observed visible under the visible host after SetParent");

        PinkyNativeHost nativeHost = GetField<PinkyNativeHost>(GetField<PinkySidebarSession>(app, "pinkySession"), "host");
        PinkyNativeHost.PinkyNativeHostSnapshot hostSnapshot = nativeHost.Snapshot();
        Check(hostSnapshot.Visible, "Native host HWND is actually visible while the child is attached");
        Rectangle requestedPhysical = GetField<Rectangle>(app, "lastPinkyPhysicalBounds");
        Check(hostSnapshot.ClientRectPhysical == requestedPhysical,
            "PointToScreen/LogicalToPhysical slot rectangle matches the actual native host client rectangle");
        IDictionary<string, object> visibleHostLease = Parse(ReadSharedText(Path.Combine(fixtureDirectory, "host.json")));
        Check(Boolean(visibleHostLease, "visible"), "Published host lease is visible while child is attached");
        Check(ReadRectangle(visibleHostLease) == hostSnapshot.ClientRectPhysical,
            "Published physical rectangle matches GetClientRect plus ClientToScreen");
        Check(hostSnapshot.ClientRectPhysical.X < -1000 && hostSnapshot.ClientRectPhysical.Y < -1000,
            "Native host remains at the isolated offscreen coordinates");

        app.Collapse();
        PumpUntil(() => !GetProperty<bool>(app, "PinkyPanelRequested") && ReadPanelPhase() == "hidden", "hidden phase", false);
        Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Hidden phase removes the inline slot");
        Check(GetProperty<Size>(app, "PinkyPanelSize").IsEmpty, "Hidden phase leaves cards with no Pinky reservation");
        Check(ReadPanelPhase() == "hidden", "Child voluntarily detached and published hidden");
        Check(!GetField<HoverState>(app, "state").Open, "Collapse remains closed after panel detach");

        File.WriteAllText(fixtureControlFile, "stop", new UTF8Encoding(false));
        WaitChildExit(2500);
        Check(childProcess.ExitCode == 0, "Simulated child exits normally after hidden state");
        uiPumpTimer.Stop();
        uiPumpTimer.Dispose();
        uiPumpTimer = null;
        Check(uiPumpTicks >= 5, "App UI message pump continued processing during asynchronous host updates");

        var finalPanel = Parse(ReadSharedText(Path.Combine(fixtureDirectory, "panel.json")));
        var host = Parse(ReadSharedText(Path.Combine(fixtureDirectory, "host.json")));
        string hostNonce = Text(host, "nonce");
        Check(IsCsprngNonce(hostNonce), "Host nonce is 32 hex characters");
        Check(Text(finalPanel, "nonce") == hostNonce, "Child echoed the host CSPRNG nonce");
        Check(Integer(finalPanel, "pid") == childProcess.Id, "Panel lease PID is the owned child process");
        Check(LongString(finalPanel, "processBirthFileTime") > 0, "Panel lease includes child process birth FILETIME");
        Check(!Boolean(host, "visible"), "Host lease is hidden before shutdown");

        app.Shutdown();
        app = null;
        Layout savedLayout = LayoutStore.Load(layoutFile);
        Check(savedLayout.Cards.Select(card => card.Id).SequenceEqual(originalCardIds),
            "Collapse persistence retains only the original user cards, with no Pinky entry");
        Check(Clock.ElapsedMilliseconds < TotalLimitMs, "End-to-end fixture stayed within its total time limit");
    }

    private static void StartSimulatedChild()
    {
        string executable = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        string arguments = "child " + Quote(fixtureDirectory);
        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable)
        };
        childProcess = Process.Start(start);
        if (childProcess == null)
            throw new InvalidOperationException("Could not start the owned simulated panel process.");
        Check(childProcess.Id != Process.GetCurrentProcess().Id, "Panel runs in its own child process");
    }

    private static int RunSimulatedChild(string directory)
    {
        try
        {
            if (!SetProcessDpiAwarenessContext(DpiPerMonitorV2))
                throw new InvalidOperationException("Could not set simulated child process to PMv2.");

            string classText = "SIMULATED PINKY PANEL — TEST ONLY";
            var panel = new Form
            {
                Text = classText,
                Name = "SimulatedPinkyPanelOnly",
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ClientSize = new Size(320, 180)
            };
            var label = new Label
            {
                Text = "SIMULATED PANEL — NO APPROVAL ACTION",
                Dock = DockStyle.Top,
                Height = 38,
                TextAlign = ContentAlignment.MiddleCenter
            };
            panel.Controls.Add(label);
            panel.CreateControl();
            IntPtr panelHandle = panel.Handle;
            int originalStyle = GetWindowLong(panelHandle, GwlStyle);
            panel.Show();
            panel.Location = new Point(-32000, -32000);

            var coordinator = new Form
            {
                Text = "SIMULATED COORDINATOR",
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ClientSize = new Size(1, 1),
                Opacity = 0
            };
            string hostFile = Path.Combine(directory, "host.json");
            string panelFile = Path.Combine(directory, "panel.json");
            string resultFile = Path.Combine(directory, "child-result.json");
            string nonce = null;
            long sequence = 0;
            IntPtr currentParent = IntPtr.Zero;
            bool attached = false;
            bool hasAttachedOnce = false;
            bool hostVisibleObserved = false;
            bool observedChildVisibleWhileAttached = false;
            bool dpiContextsEqual = false;
            bool childAttached = false;
            bool stopped = false;

            var poll = new System.Windows.Forms.Timer { Interval = 80 };
            poll.Tick += (sender, eventArgs) =>
            {
                try
                {
                    CheckTotalTime();
                    if (File.Exists(Path.Combine(directory, "test-control.stop")) && !attached)
                    {
                        stopped = true;
                        poll.Stop();
                        panel.Close();
                        coordinator.Close();
                        return;
                    }
                    if (!File.Exists(hostFile))
                        return;

                    IDictionary<string, object> lease;
                    try { lease = Parse(ReadSharedText(hostFile)); }
                    catch (IOException) { return; }
                    if (Text(lease, "schema") != "pinky.sidebar-host.v1")
                        return;
                    string hostNonce = Text(lease, "nonce");
                    if (!IsCsprngNonce(hostNonce))
                        return;
                    if (nonce == null)
                        nonce = hostNonce;
                    if (!String.Equals(nonce, hostNonce, StringComparison.Ordinal))
                        return;

                    bool hostVisible = Boolean(lease, "visible");
                    IntPtr hostHandle = ParseHandle(Text(lease, "hwnd"));
                    if (hostVisible && hostHandle != IntPtr.Zero && IsWindow(hostHandle))
                    {
                        hostVisibleObserved = true;
                        IntPtr childContext = GetWindowDpiAwarenessContext(panelHandle);
                        IntPtr hostContext = GetWindowDpiAwarenessContext(hostHandle);
                        dpiContextsEqual = AreDpiAwarenessContextsEqual(childContext, hostContext);
                        if (!dpiContextsEqual)
                        {
                            WritePanel(panelFile, nonce, sequence++, "error", panelHandle, "dpi-context-mismatch", 320, 180);
                            throw new InvalidOperationException("Child and host HWND DPI awareness contexts differ.");
                        }

                        RECT client;
                        if (!GetClientRect(hostHandle, out client) || client.right <= client.left || client.bottom <= client.top)
                            return;
                        if (!attached || currentParent != hostHandle)
                        {
                            ShowWindow(panelHandle, SwHide);
                            SetParent(panelHandle, hostHandle);
                            SetWindowLong(panelHandle, GwlStyle, (originalStyle & ~WsPopup) | WsChild | WsVisible);
                            SetWindowPos(panelHandle, IntPtr.Zero, 0, 0, client.right - client.left, client.bottom - client.top,
                                SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
                            currentParent = hostHandle;
                            attached = true;
                            hasAttachedOnce = true;
                            childAttached = true;
                            ShowWindow(panelHandle, SwShowNoActivate);
                        }
                        else
                        {
                            SetWindowPos(panelHandle, IntPtr.Zero, 0, 0, client.right - client.left, client.bottom - client.top,
                                SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                        }
                        if (attached && currentParent == hostHandle &&
                            GetParent(panelHandle) == hostHandle &&
                            IsWindowVisible(panelHandle) && IsWindowVisible(hostHandle))
                            observedChildVisibleWhileAttached = true;
                        WritePanel(panelFile, nonce, sequence++, "attached", panelHandle, null,
                            Math.Max(1, (client.right - client.left) * 96d / Math.Max(1, (int)GetDpiForWindow(hostHandle))),
                            Math.Max(1, (client.bottom - client.top) * 96d / Math.Max(1, (int)GetDpiForWindow(hostHandle))));
                    }
                    else
                    {
                        if (attached)
                        {
                            ShowWindow(panelHandle, SwHide);
                            SetParent(panelHandle, IntPtr.Zero);
                            SetWindowLong(panelHandle, GwlStyle, originalStyle);
                            SetWindowPos(panelHandle, IntPtr.Zero, -32000, -32000, 320, 180,
                                SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpHideWindow);
                            currentParent = IntPtr.Zero;
                            attached = false;
                        }
                        string phase = hasAttachedOnce ? "hidden" : "requested";
                        WritePanel(panelFile, nonce, sequence++, phase, panelHandle, null, 320, 180);
                        if (File.Exists(Path.Combine(directory, "test-control.stop")))
                        {
                            stopped = true;
                            poll.Stop();
                            panel.Close();
                            coordinator.Close();
                        }
                    }

                    var evidence = new Dictionary<string, object>();
                    evidence["dpiContextsEqual"] = dpiContextsEqual;
                    evidence["childAttached"] = childAttached;
                    evidence["hostVisibleObserved"] = hostVisibleObserved;
                    evidence["observedChildVisibleWhileAttached"] = observedChildVisibleWhileAttached;
                    evidence["stopped"] = stopped;
                    WriteAtomicFixtureFile(resultFile, new JavaScriptSerializer().Serialize(evidence));
                }
                catch (Exception error)
                {
                    WriteAtomicFixtureFile(resultFile, new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                    {
                        { "error", error.ToString() },
                        { "dpiContextsEqual", dpiContextsEqual },
                        { "childAttached", childAttached },
                        { "hostVisibleObserved", hostVisibleObserved },
                        { "observedChildVisibleWhileAttached", observedChildVisibleWhileAttached }
                    }));
                    poll.Stop();
                    panel.Close();
                    coordinator.Close();
                }
            };

            coordinator.Show();
            coordinator.Hide();
            poll.Start();
            Application.Run(coordinator);
            poll.Dispose();
            panel.Dispose();
            return stopped ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("SIMULATED_CHILD_ERROR " + error);
            return 2;
        }
    }

    private static void PumpUntil(Func<bool> condition, string label, bool revealColumn)
    {
        long start = Clock.ElapsedMilliseconds;
        long lastRefresh = -1000;
        while (!condition())
        {
            CheckTotalTime();
            long now = Clock.ElapsedMilliseconds;
            long monotonic = PreviewClock.NowMs;
            if (now - lastRefresh >= 220)
            {
                Invoke(app, "RefreshPinkyHost", monotonic);
                lastRefresh = now;
            }
            if (revealColumn)
            {
                Invoke(app, "PresentReveal", true, Screen.PrimaryScreen, monotonic);
                GetField<Form>(app, "form").Location = new Point(-30000, -30000);
            }
            Application.DoEvents();
            Thread.Sleep(5);
            if (Clock.ElapsedMilliseconds - start > 9000)
                throw new TimeoutException("Timed out waiting for " + label + ".");
        }
    }

    private static string ReadPanelPhase()
    {
        string file = Path.Combine(fixtureDirectory, "panel.json");
        if (!File.Exists(file))
            return null;
        try { return Text(Parse(ReadSharedText(file)), "phase"); }
        catch { return null; }
    }

    private static string ReadSharedText(string file)
    {
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
            return reader.ReadToEnd();
    }

    private static void WriteAtomicFixtureFile(string file, string contents)
    {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents, new UTF8Encoding(false));
            if (File.Exists(file))
                File.Replace(temp, file, null);
            else
                File.Move(temp, file);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void WritePanel(string file, string nonce, long sequence, string phase,
        IntPtr panelHandle, string reason, double width, double height)
    {
        var data = new Dictionary<string, object>();
        data["schema"] = "pinky.sidebar-panel.v1";
        data["nonce"] = nonce;
        data["pid"] = Process.GetCurrentProcess().Id;
        data["processBirthFileTime"] = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
        data["updatedUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        data["phase"] = phase;
        data["desiredWidthDip"] = width;
        data["desiredHeightDip"] = height;
        data["panelHwnd"] = "0x" + panelHandle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        if (reason != null) data["reason"] = reason;
        data["sequence"] = sequence;

        string temp = file + ".tmp";
        File.WriteAllText(temp, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(temp, file, null);
        else File.Move(temp, file);
    }

    private static void WaitChildExit(int timeoutMs)
    {
        long until = Clock.ElapsedMilliseconds + timeoutMs;
        while (!childProcess.HasExited && Clock.ElapsedMilliseconds < until)
        {
            CheckTotalTime();
            Application.DoEvents();
            Thread.Sleep(10);
            childProcess.Refresh();
        }
        if (!childProcess.HasExited)
            throw new TimeoutException("Owned simulated panel child did not exit within " + timeoutMs + " ms.");
    }

    private static void CleanupParent()
    {
        if (uiPumpTimer != null)
        {
            uiPumpTimer.Stop();
            uiPumpTimer.Dispose();
            uiPumpTimer = null;
        }
        try
        {
            if (app != null)
            {
                app.Shutdown();
                app = null;
            }
        }
        catch { }

        try
        {
            if (childProcess != null)
            {
                if (!childProcess.HasExited)
                {
                    childProcess.Kill();
                    childProcess.WaitForExit(1000);
                }
                childProcess.Dispose();
                childProcess = null;
            }
        }
        catch { }

        try
        {
            if (!String.IsNullOrEmpty(fixtureDirectory) && Directory.Exists(fixtureDirectory))
            {
                string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost"));
                string fixture = Path.GetFullPath(fixtureDirectory);
                if (fixture.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(fixture).StartsWith("fixture-", StringComparison.Ordinal))
                    Directory.Delete(fixture, true);
            }
        }
        catch { }
    }

    private static string ReadFixtureEvidence()
    {
        try
        {
            if (String.IsNullOrEmpty(fixtureDirectory) || !Directory.Exists(fixtureDirectory))
                return "fixture directory absent";
            var entries = new List<string>();
            foreach (string file in Directory.GetFiles(fixtureDirectory))
                entries.Add(Path.GetFileName(file) + "=" + ReadSharedText(file));
            string child = childProcess == null ? "child absent" : childProcess.HasExited
                ? "child exit=" + childProcess.ExitCode : "child still running";
            return child + "; " + String.Join("; ", entries);
        }
        catch (Exception error) { return "diagnostic read failed: " + error.Message; }
    }

    private static void CheckTotalTime()
    {
        if (Clock.ElapsedMilliseconds >= TotalLimitMs)
            throw new TimeoutException("End-to-end fixture exceeded its 19-second total limit.");
    }

    private static void Check(bool condition, string description)
    {
        CheckTotalTime();
        if (!condition)
            throw new InvalidOperationException("Failed check: " + description);
        checks++;
    }

    private static bool IsCsprngNonce(string nonce)
    {
        if (nonce == null || nonce.Length != 32)
            return false;
        return nonce.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    private static IDictionary<string, object> Parse(string text)
    {
        return new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 8 }
            .DeserializeObject(text) as IDictionary<string, object>;
    }

    private static string Text(IDictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value as string : null;
    }

    private static bool Boolean(IDictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) && value is bool && (bool)value;
    }

    private static int Integer(IDictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
    }

    private static long LongString(IDictionary<string, object> data, string key)
    {
        return Convert.ToInt64(Text(data, key), CultureInfo.InvariantCulture);
    }

    private static IntPtr ParseHandle(string value)
    {
        if (String.IsNullOrEmpty(value) || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;
        long parsed;
        return Int64.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)
            ? new IntPtr(parsed) : IntPtr.Zero;
    }

    private static Rectangle ReadRectangle(IDictionary<string, object> data)
    {
        object value;
        var rect = data != null && data.TryGetValue("clientRectPhysical", out value)
            ? value as IDictionary<string, object> : null;
        if (rect == null)
            return Rectangle.Empty;
        return new Rectangle(Integer(rect, "x"), Integer(rect, "y"), Integer(rect, "width"), Integer(rect, "height"));
    }

    private static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance);
    }

    private static T GetProperty<T>(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            throw new MissingMemberException(instance.GetType().FullName, name);
        return (T)property.GetValue(instance, null);
    }

    private static object Invoke(object instance, string name, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(instance.GetType().FullName, name);
        return method.Invoke(instance, arguments);
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
}
