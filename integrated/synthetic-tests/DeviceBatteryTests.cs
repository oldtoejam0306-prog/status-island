using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;

namespace IslandIntegrated
{
    public static class DeviceBatteryTests
    {
        static int checks;
        static void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Device battery fixture failed: " + name);
        }

        static IDictionary<string, object> Device(string id, string name, string platform, string model,
            bool online, bool paused, object level, object charging, object plugged, object full, object batteryAt)
        {
            return new Dictionary<string, object>
            {
                {"stableId",id},{"name",name},{"platform",platform},{"model",model},
                {"online",online},{"paused",paused},{"level",level},{"charging",charging},
                {"plugged",plugged},{"full",full},{"batteryAtUtc",batteryAt}
            };
        }

        static string Document(DateTimeOffset updated, long sequence, object[] devices, int pid = 42,
            string birth = "638987654321000000", string nonce = "0123456789abcdef0123456789abcdef")
        {
            var root = new Dictionary<string, object>
            {
                {"schema","pinky.sidebar-batteries.v1"},{"pid",pid},{"processBirthFileTime",birth},
                {"nonce",nonce},{"sequence",sequence},{"updatedUtc",updated.ToUniversalTime().ToString("o")},
                {"devices",devices}
            };
            return new JavaScriptSerializer().Serialize(root);
        }

        static PinkyBatterySnapshot Parse(string json, DateTimeOffset now, int expectedPid = 42,
            long expectedBirth = 638987654321000000, string expectedNonce = "0123456789abcdef0123456789abcdef",
            bool identityValid = true)
        {
            return PinkyBatteryDocumentParser.Parse(json, expectedNonce, expectedPid, expectedBirth, now,
                delegate(int pid, long birth) { return identityValid && pid == expectedPid && birth == expectedBirth; });
        }

        static DeviceBatteryConfiguration Config(string directory, bool enabled, string ipadId, string phoneId)
        {
            Directory.CreateDirectory(directory);
            string key = Path.Combine(directory, "fixture-key");
            string knownHosts = Path.Combine(directory, "known-hosts");
            File.WriteAllText(key, "fixture only");
            File.WriteAllText(knownHosts, "fixture only");
            var config = new Dictionary<string, object>
            {
                {"schema","pinky.device-battery-connection.v1"},{"enabled",enabled},
                {"host",enabled?"fixture.invalid":""},{"account",enabled?"fixture":""},
                {"keyFile",enabled?key:""},{"knownHostsFile",enabled?knownHosts:""},
                {"macStableId","fixture-mac"},{"ipadStableId",ipadId??""},{"phoneStableId",phoneId??""}
            };
            string path = Path.Combine(directory, "device-battery-connection.json");
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(config));
            return DeviceBatteryConfiguration.Load(path);
        }

        static PinkyBatterySnapshot FixtureDevices(DateTimeOffset now, bool duplicatePhones, object phoneLevel, object ipadLevel)
        {
            var devices = new List<object>();
            devices.Add(Device("ipad-1", "iPad", "iPadOS", "iPad Pro", true, false, ipadLevel, true, true, false, now.AddSeconds(-20).ToString("o")));
            devices.Add(Device("phone-1", "iPhone", "iOS", "iPhone", true, false, phoneLevel, false, false, false, now.AddSeconds(-25).ToString("o")));
            if (duplicatePhones) devices.Add(Device("phone-2", "iPhone备用机", "iOS", "iPhone", true, false, 72, false, false, false, now.AddSeconds(-10).ToString("o")));
            return Parse(Document(now, 7, devices.ToArray()), now);
        }

        static void TestParserAndFreshness(DateTimeOffset now)
        {
            var devices = new object[]
            {
                Device("ipad-1", "iPad", "iPadOS", "iPad Pro", true, false, 0, true, true, false, now.AddSeconds(-20).ToString("o")),
                Device("phone-1", "iPhone", "iOS", "iPhone", true, false, 100, false, false, true, now.AddSeconds(-30).ToString("o")),
                Device("unknown-1", "未知设备", "iOS", "", true, false, null, null, null, null, null)
            };
            var parsed = Parse(Document(now, 8, devices), now);
            Check(parsed.Verified && parsed.Sequence == 8 && parsed.Devices.Count == 3, "valid schema and sequence");
            Check(parsed.Devices[0].Level.HasValue && parsed.Devices[0].Level.Value == 0 && parsed.Devices[0].HasFreshLevel(now), "zero percent remains a real sample");
            Check(parsed.Devices[1].Level.HasValue && parsed.Devices[1].Level.Value == 100 && parsed.Devices[1].Full == true, "100 percent and full state remain real");
            Check(!parsed.Devices[2].Level.HasValue && !parsed.Devices[2].HasFreshLevel(now), "null level is unavailable");
            var boundary = Parse(Document(now, 9, new object[] { Device("mac-1", "Mac", "macOS", "", true, false, 50, false, true, false, now.AddSeconds(-180).ToString("o")) }), now);
            Check(boundary.Verified && boundary.Devices[0].HasFreshLevel(now), "180 second freshness boundary is inclusive");
            var stale = Parse(Document(now, 10, new object[] { Device("mac-1", "Mac", "macOS", "", true, false, 50, false, true, false, now.AddSeconds(-181).ToString("o")) }), now);
            Check(stale.Verified && !stale.Devices[0].HasFreshLevel(now), "battery sample older than 180 seconds is not fresh");
            var futureSample = Parse(Document(now, 11, new object[] { Device("mac-1", "Mac", "macOS", "", true, false, 50, false, true, false, now.AddSeconds(6).ToString("o")) }), now);
            Check(futureSample.Verified && !futureSample.Devices[0].Level.HasValue, "battery sample over five seconds in future is discarded");
            Check(!Parse(Document(now.AddSeconds(-46), 12, new object[0]), now).Verified, "45 second heartbeat tolerance");
            Check(!Parse(Document(now.AddSeconds(6), 13, new object[0]), now).Verified, "heartbeat over five seconds in future is rejected");
            Check(!Parse(Document(now, 14, new object[0], 43), now).Verified, "PID identity mismatch");
            Check(!Parse(Document(now, 15, new object[0]), now, identityValid: false).Verified, "expected executable identity validator");
            Check(!Parse("{}", now).Verified, "malformed schema");
        }

        static void TestPmsetParser()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            const string normal = "Now drawing from 'AC Power'\n -InternalBattery-0 (id=fixture)\t78%; charging; 1:18 remaining present: true";
            var sample = DeviceBatterySource.ParseMacPmset(normal, "mac-fixture", now);
            Check(sample != null && sample.Level.HasValue && sample.Level.Value == 78 && sample.Charging == true && sample.Plugged == true, "pmset charging/AC sample");
            var zero = DeviceBatterySource.ParseMacPmset("Now drawing from 'Battery Power'\n -InternalBattery-0 (id=fixture)\t0%; discharging; present: true", "mac-fixture", now);
            Check(zero != null && zero.Level.HasValue && zero.Level.Value == 0 && zero.Plugged == false, "pmset zero percent");
            var full = DeviceBatterySource.ParseMacPmset("Now drawing from 'AC Power'\n -InternalBattery-0 (id=fixture)\t100%; charged; present: true", "mac-fixture", now);
            Check(full != null && full.Level.HasValue && full.Level.Value == 100 && full.Full == true, "pmset 100 percent");
            var overflow = DeviceBatterySource.ParseMacPmset("Now drawing from 'AC Power'\n -InternalBattery-0 (id=fixture)\t1000%; charging; present: true", "mac-fixture", now);
            Check(overflow == null || !overflow.Level.HasValue, "pmset rejects 1000 percent");
            var negative = DeviceBatterySource.ParseMacPmset("Now drawing from 'Battery Power'\n -InternalBattery-0 (id=fixture)\t-1%; discharging; present: true", "mac-fixture", now);
            Check(negative == null || !negative.Level.HasValue, "pmset rejects negative percentage");
        }

        static bool AcceptFixtureProcessIdentity(int pid, long birth, string executable)
        {
            return pid == 42 && birth == 638987654321000000 && executable.EndsWith("pinky-fixture.exe", StringComparison.OrdinalIgnoreCase);
        }

        static string PanelDocument(DateTimeOffset updated, long sequence)
        {
            var panel = new Dictionary<string, object>
            {
                {"schema","pinky.sidebar-panel.v1"},{"nonce","0123456789abcdef0123456789abcdef"},
                {"pid",42},{"processBirthFileTime","638987654321000000"},{"updatedUtc",updated.ToUniversalTime().ToString("o")},
                {"phase","hidden"},{"desiredWidthDip",360.0},{"desiredHeightDip",80.0},
                {"panelHwnd",null},{"reason","fixture"},{"sequence",sequence}
            };
            return new JavaScriptSerializer().Serialize(panel);
        }

        static void TestPanelLaneIsolation(DateTimeOffset now)
        {
            string directory = Path.Combine(Path.GetTempPath(), "pinky-battery-lane-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Type delegateType = typeof(PinkyHostConnection).GetNestedType("ProcessIdentityValidator", BindingFlags.NonPublic);
                var method = typeof(DeviceBatteryTests).GetMethod("AcceptFixtureProcessIdentity", BindingFlags.NonPublic | BindingFlags.Static);
                Delegate validator = Delegate.CreateDelegate(delegateType, method);
                var factory = typeof(PinkyHostConnection).GetMethod("CreateForTesting", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(string), delegateType }, null);
                Check(factory != null, "isolated panel-reader test factory exists");
                var connection = (PinkyHostConnection)factory.Invoke(null, new object[] { directory, Path.Combine(directory, "pinky-fixture.exe"), validator });
                string panelPath = Path.Combine(directory, "panel.json");
                File.WriteAllText(panelPath, PanelDocument(now, 1));
                var missingBattery = connection.ReadPanel("0123456789abcdef0123456789abcdef", now);
                Check(missingBattery.Verified && (missingBattery.Batteries == null || !missingBattery.Batteries.Verified), "missing battery file does not invalidate panel lane");
                Check(!connection.BatteryAccessDenied && !connection.AccessDenied, "missing battery file does not set access-denied flags");

                string batteryPath = Path.Combine(directory, "batteries.json");
                var goodBattery = Document(now, 1, new object[] { Device("phone-1", "iPhone", "iOS", "iPhone", true, false, 0, false, false, false, now.AddSeconds(-3).ToString("o")) });
                File.WriteAllText(batteryPath, goodBattery);
                File.WriteAllText(panelPath, PanelDocument(now.AddMilliseconds(100), 2));
                var validLane = connection.ReadPanel("0123456789abcdef0123456789abcdef", now.AddMilliseconds(100));
                Check(validLane.Verified && validLane.Batteries != null && validLane.Batteries.Verified && validLane.Batteries.Devices[0].Level == 0, "valid battery lane attaches to verified panel lease");

                var replay = Document(now.AddMilliseconds(200), 1, new object[] { Device("phone-1", "iPhone", "iOS", "iPhone", true, false, 99, false, false, false, now.AddSeconds(-1).ToString("o")) });
                File.WriteAllText(batteryPath, replay);
                File.WriteAllText(panelPath, PanelDocument(now.AddMilliseconds(300), 3));
                var replayLane = connection.ReadPanel("0123456789abcdef0123456789abcdef", now.AddMilliseconds(300));
                Check(replayLane.Verified && replayLane.Batteries != null && !replayLane.Batteries.Verified, "battery sequence replay is rejected without invalidating panel lease");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory, false);
            }
        }

        static void TestSelectionRenderingAndGeometry(DateTimeOffset now, string evidenceDirectory)
        {
            string temp = Path.Combine(Path.GetTempPath(), "device-battery-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var configured = new DeviceBatterySource(Config(Path.Combine(temp, "configured"), false, "ipad-1", "phone-1"), null, null);
                configured.ObservePinky(FixtureDevices(now, false, 100, 0), now);
                var view = configured.Snapshot(now);
                Check(view.IPad.Available(now) && view.IPad.Value(now) == "0%", "configured iPad stable ID and zero display");
                Check(view.Phone.Available(now) && view.Phone.Value(now) == "100%", "configured phone stable ID and 100 display");
                string ipadTip = view.IPad.Tooltip(now);
                Check(ipadTip.Contains("iPad Pro") && ipadTip.Contains("0%") && ipadTip.Contains("充电中") && ipadTip.Contains("观测时间"), "tooltip includes device name, charging and observation time");
                Check(view.Phone.Tooltip(now).Contains("iPhone") && view.Phone.Tooltip(now).Contains("100%") && view.Phone.Tooltip(now).Contains("观测时间"), "phone tooltip includes full sample identity and time");
                Check(view.Signature(now) != configured.Snapshot(now.AddSeconds(181)).Signature(now.AddSeconds(181)), "freshness expiry changes the render signature");

                var automatic = new DeviceBatterySource(Config(Path.Combine(temp, "automatic"), false, "", ""), null, null);
                automatic.ObservePinky(FixtureDevices(now, true, 72, 0), now);
                var ambiguous = automatic.Snapshot(now);
                Check(ambiguous.Phone.NeedsSelection && !ambiguous.Phone.Available(now), "multiple same-class phones require selection");
                Check(ambiguous.Phone.Value(now) == "不可用" && ambiguous.Phone.Tooltip(now).Contains("需要选择显示的设备"), "ambiguous source is explicit");

                var unknown = new DeviceBatterySource(Config(Path.Combine(temp, "unknown"), false, "", ""), null, null).Snapshot(now);
                Check(unknown.Mac.Value(now) == "不可用" && unknown.IPad.Value(now) == "不可用" && unknown.Phone.Value(now) == "不可用", "all missing lanes show unavailable");

                Directory.CreateDirectory(evidenceDirectory);
                using (var compact = StatusUiRenderer.RenderWithBattery(360, 80, 0, new LegacySnapshot(), "fixture", null, 0, view))
                    compact.Save(Path.Combine(evidenceDirectory, "battery-compact-0-100.png"));
                using (var compactUnknown = StatusUiRenderer.RenderWithBattery(360, 80, 0, new LegacySnapshot(), "fixture", null, 0, unknown))
                    compactUnknown.Save(Path.Combine(evidenceDirectory, "battery-compact-unknown.png"));
                using (var expanded = StatusUiRenderer.RenderWithBattery(360, 480, 0, new LegacySnapshot(), "fixture", null, 0, view))
                    expanded.Save(Path.Combine(evidenceDirectory, "battery-expanded-0-100.png"));
                using (var expandedUnknown = StatusUiRenderer.RenderWithBattery(360, 480, 0, new LegacySnapshot(), "fixture", null, 0, unknown))
                    expandedUnknown.Save(Path.Combine(evidenceDirectory, "battery-expanded-unknown.png"));

                var compactRow = StatusUiLayout.BatteryRow(360, true);
                var expandedRow = StatusUiLayout.BatteryRow(360, false);
                Check(compactRow.Y == 59 && compactRow.Height == 16 && compactRow.Bottom <= 80, "compact row at y59-75 fits 360x80");
                Check(expandedRow.Y == 362 && expandedRow.Height == 16 && expandedRow.Bottom <= 480, "expanded row at y362-378 fits");
                for (int i = 0; i < 3; i++)
                {
                    var a = StatusUiLayout.BatterySlot(i, 360, true);
                    var b = StatusUiLayout.BatterySlot(i, 360, false);
                    Check(a.Width == 96 && a.X >= 24 && a.Right <= 336, "compact equal slot geometry " + i);
                    Check(b.Width == 96 && b.X >= 24 && b.Right <= 336, "expanded equal slot geometry " + i);
                    if (i > 0) Check(StatusUiLayout.BatterySlot(i - 1, 360, true).Right + 12 == a.Left, "12px compact gap " + i);
                }
                Check(!StatusUiLayout.BatteryRow(360, false).IntersectsWith(StatusUiLayout.Connection(1, 360)), "expanded row clears DeepSeek line");
                Check(!StatusUiLayout.BatteryRow(360, false).IntersectsWith(StatusUiLayout.Voice(0, 360)), "expanded row clears voice row");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories)) File.Delete(file);
                foreach (string directory in Directory.GetDirectories(temp, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory, false);
                Directory.Delete(temp, false);
            }
        }

        static void TestSingleFlightAndStop(DateTimeOffset now)
        {
            string temp = Path.Combine(Path.GetTempPath(), "device-battery-flight-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            using (var started = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                int calls = 0;
                DeviceBatterySource source = null;
                DeviceBatterySource stopping = null;
                ManualResetEventSlim lateStarted = new ManualResetEventSlim(false);
                ManualResetEventSlim lateRelease = new ManualResetEventSlim(false);
                try
                {
                    var config = Config(Path.Combine(temp, "first"), true, "", "");
                    source = new DeviceBatterySource(config, delegate
                    {
                        Interlocked.Increment(ref calls); started.Set(); release.Wait(2000);
                        return new DeviceBatterySample("mac-fixture", "Mac", "macOS", "fixture", true, false, 55, false, true, false, DateTimeOffset.UtcNow);
                    }, System.Diagnostics.Stopwatch.GetTimestamp);
                    source.StartIfDue(now); source.StartIfDue(now);
                    Check(started.Wait(1500), "Mac reader starts asynchronously");
                    Thread.Sleep(40);
                    Check(calls == 1, "duplicate due ticks are single-flight");
                    release.Set();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 1500 && !source.Snapshot(DateTimeOffset.UtcNow).Mac.Available(DateTimeOffset.UtcNow)) Thread.Sleep(10);
                    Check(source.Snapshot(DateTimeOffset.UtcNow).Mac.Available(DateTimeOffset.UtcNow), "fake Mac result arrives asynchronously");
                    source.Stop(); source = null;

                    stopping = new DeviceBatterySource(config, delegate
                    {
                        lateStarted.Set(); lateRelease.Wait(2000);
                        return new DeviceBatterySample("late", "Mac", "macOS", "fixture", true, false, 99, false, true, false, DateTimeOffset.UtcNow);
                    }, System.Diagnostics.Stopwatch.GetTimestamp);
                    stopping.StartIfDue(now);
                    Check(lateStarted.Wait(1500), "late Mac poll starts");
                    stopping.Stop();
                    lateRelease.Set();
                    Thread.Sleep(60);
                    Check(!stopping.Snapshot(DateTimeOffset.UtcNow).Mac.Available(DateTimeOffset.UtcNow), "Stop prevents late result publication");
                    stopping = null;
                }
                finally
                {
                    release.Set(); lateRelease.Set();
                    if (source != null) source.Stop();
                    if (stopping != null) stopping.Stop();
                    lateStarted.Dispose(); lateRelease.Dispose();
                    foreach (string file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories)) File.Delete(file);
                    foreach (string directory in Directory.GetDirectories(temp, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) Directory.Delete(directory, false);
                    Directory.Delete(temp, false);
                }
                TestMacAccessDeniedLatch(Path.Combine(temp, "denied"), new UnauthorizedAccessException("fake denied"), now);
                TestMacAccessDeniedLatch(Path.Combine(temp, "win32-denied"), new Win32Exception(5), now);
            }
        }

        public static string RunAll(string evidenceDirectory)
        {
            checks = 0;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TestParserAndFreshness(now);
            TestPmsetParser();
            TestPanelLaneIsolation(now);
            TestSelectionRenderingAndGeometry(now, evidenceDirectory);
            TestSingleFlightAndStop(now);
            return new JavaScriptSerializer().Serialize(new
            {
                schema = "sidebar.device-battery-tests.v1",
                passed = true,
                checks = checks,
                scope = "fake Pinky JSON identity/freshness/0/100, pmset parsing including invalid 1000 and -1, device ambiguity, 60-second single-flight scheduling/Stop, and 360px compact/expanded GDI+ geometry evidence",
                realSsh = false,
                realPhone = false,
                realMac = false
            });
        }

        public static int Main(string[] args)
        {
            try
            {
                string output = args != null && args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "device-battery-render-evidence");
                Console.WriteLine(RunAll(output));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        static void TestMacAccessDeniedLatch(string directory, Exception deniedException, DateTimeOffset now)
        {
            var config = Config(directory, true, "", "");
            int calls = 0;
            long ticks = System.Diagnostics.Stopwatch.GetTimestamp();
            var source = new DeviceBatterySource(config, delegate
            {
                Interlocked.Increment(ref calls);
                throw deniedException;
            }, delegate { return Interlocked.Read(ref ticks); });
            try
            {
                source.StartIfDue(now);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!source.MacAccessDenied && watch.ElapsedMilliseconds < 1500) Thread.Sleep(5);
                Check(source.MacAccessDenied && calls == 1, "Mac permission failure is latched");
                Interlocked.Exchange(ref ticks, ticks + System.Diagnostics.Stopwatch.Frequency * 120L);
                source.StartIfDue(now.AddMinutes(2));
                Thread.Sleep(40);
                Check(calls == 1, "latched Mac permission failure is not retried");
            }
            finally { source.Stop(); }
        }
    }
}
