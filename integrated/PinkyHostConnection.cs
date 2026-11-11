using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;

namespace IslandIntegrated
{
    public sealed class PinkyPanelLease
    {
        public bool Verified;
        public string Phase;
        public double DesiredWidthDip;
        public double DesiredHeightDip;
        public IntPtr PanelHwnd;
        public string Reason;
        public long Sequence;
    }

    public sealed class PinkyHostLease
    {
        public string Nonce;
        public int ProcessId;
        public long ProcessBirthFileTime;
        public IntPtr Hwnd;
        public DateTimeOffset UpdatedUtc;
        public bool Visible;
        public Rectangle ClientRectPhysical;
        public int Dpi;
        public long Sequence;
    }

    public sealed class PinkyHostConnection
    {
        internal delegate bool ProcessIdentityValidator(int pid, long birthFileTime, string expectedExecutable);
        internal delegate long MonotonicClock();

        private const int MaxFileBytes = 16 * 1024;
        private readonly string directory;
        private readonly string executable;
        private readonly ProcessIdentityValidator validator;
        private readonly MonotonicClock monotonicClock;
        private long lastPanelSequence = -1;
        private string lastPanelFingerprint;
        private string lastPanelNonce;
        private int lastPanelPid;
        private long lastPanelBirthFileTime;
        private long lastPanelHeartbeat;
        private bool hasPanelHeartbeat;
        private PinkyPanelLease lastVerifiedPanel;
        private bool accessDenied;
        private bool appearanceAccessDenied;
        private string lastAppearanceTheme;
        private string lastAppearanceAttemptTheme;
        private long lastAppearanceAttempt;
        private bool hasAppearanceAttempt;
        private long lastHostSequence = -1;
        private string lastHostNonce;

        private PinkyHostConnection(string directory, string executable, ProcessIdentityValidator validator, MonotonicClock clock)
        {
            this.directory = Path.GetFullPath(directory);
            this.executable = Path.GetFullPath(executable);
            this.validator = validator ?? ValidateProcessIdentity;
            this.monotonicClock = clock ?? Stopwatch.GetTimestamp;
        }

        public bool Enabled { get; private set; }
        public bool AccessDenied { get { return accessDenied; } }
        public bool OwnedPopupPresentation { get; private set; }

        public static PinkyHostConnection FromConfiguration(string file)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost");
                EnsurePlainAncestors(root);
                EnsurePlainAncestors(Path.GetDirectoryName(Path.GetFullPath(file)));
                EnsurePlainFile(file);
                if (!File.Exists(file) || new FileInfo(file).Length > MaxFileBytes) return Disabled();
                IDictionary<string, object> data = Parse(File.ReadAllText(file, new UTF8Encoding(false, true)));
                if (data == null || !HasOnly(data, "schema", "enabled", "directory", "pinkyExecutable", "presentation")) return Disabled();
                if (Text(data, "schema") != "pinky.sidebar-connection.v1" || !Bool(data, "enabled")) return Disabled();
                object presentation;
                bool ownedPopup = data.TryGetValue("presentation", out presentation);
                if (ownedPopup && !String.Equals(presentation as string, "owned-popup", StringComparison.Ordinal)) return Disabled();
                string dir = Text(data, "directory");
                string exe = Text(data, "pinkyExecutable");
                if (!Path.IsPathRooted(dir) || !Path.IsPathRooted(exe)) return Disabled();
                dir = Path.GetFullPath(dir); exe = Path.GetFullPath(exe);
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                if (!String.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase) &&
                    !dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Disabled();
                EnsurePlainAncestors(dir);
                EnsurePlainAncestors(Path.GetDirectoryName(exe));
                var result = new PinkyHostConnection(dir, exe, null, null);
                result.Enabled = true;
                result.OwnedPopupPresentation = ownedPopup;
                return result;
            }
            catch (UnauthorizedAccessException) { return Disabled(true); }
            catch (SecurityException) { return Disabled(true); }
            catch (Win32Exception ex) { return ex.NativeErrorCode == 5 ? Disabled(true) : Disabled(); }
            catch { return Disabled(); }
        }

        internal static PinkyHostConnection CreateForTesting(string directory, string executable, ProcessIdentityValidator validator)
        {
            return CreateForTesting(directory, executable, validator, null);
        }

        internal static PinkyHostConnection CreateForTesting(string directory, string executable, ProcessIdentityValidator validator, MonotonicClock clock)
        {
            var result = new PinkyHostConnection(directory, executable, validator, clock);
            result.Enabled = true;
            return result;
        }

        public PinkyPanelLease ReadPanel(string hostNonce, DateTimeOffset now)
        {
            var failed = new PinkyPanelLease { Reason = "panel-disabled" };
            if (!Enabled || accessDenied || !ValidNonce(hostNonce)) return failed;
            long readStarted = monotonicClock();
            try
            {
                failed.Reason = "panel-read";
                EnsurePlainAncestors(directory);
                string file = Path.Combine(directory, "panel.json");
                EnsurePlainFile(file);
                string raw = ReadBounded(file);
                if (raw == null) return failed;
                failed.Reason = "panel-schema";
                IDictionary<string, object> d = Parse(raw);
                if (d == null || !HasOnly(d, "schema", "nonce", "pid", "processBirthFileTime", "updatedUtc", "phase", "desiredWidthDip", "desiredHeightDip", "panelHwnd", "reason", "sequence")) return failed;
                if (Text(d, "schema") != "pinky.sidebar-panel.v1" || Text(d, "nonce") != hostNonce) return failed;
                int pid = Integer(d, "pid");
                long birth = LongString(d, "processBirthFileTime");
                long sequence = Long(d, "sequence");
                failed.Reason = "panel-identity";
                if (pid <= 0 || birth <= 0 || sequence < 0 || sequence > 9007199254740991L || !validator(pid, birth, executable)) return failed;
                now = now.AddSeconds(Math.Max(0, monotonicClock() - readStarted) / (double)Stopwatch.Frequency);
                failed.Reason = "panel-time";
                DateTimeOffset updated;
                if (!DateTimeOffset.TryParse(Text(d, "updatedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out updated)) return failed;
                double skew = (updated - now.ToUniversalTime()).TotalSeconds;
                if (skew > 1 || (now.ToUniversalTime() - updated).TotalSeconds > 3) return failed;
                failed.Reason = "panel-content";
                string phase = Text(d, "phase");
                if (Array.IndexOf(new[] { "hidden", "requested", "attached", "standalone", "error" }, phase) < 0) return failed;
                double width = Number(d, "desiredWidthDip");
                double height = Number(d, "desiredHeightDip");
                if (!Finite(width) || !Finite(height) || width < 1 || height < 1 || width > 4096 || height > 4096) return failed;
                IntPtr hwnd = IntPtr.Zero;
                object hv;
                if (d.TryGetValue("panelHwnd", out hv) && hv != null)
                {
                    if (!(hv is string) || !TryHandle((string)hv, out hwnd)) return failed;
                }
                object reasonValue;
                string reason = null;
                if (d.TryGetValue("reason", out reasonValue))
                {
                    reason = reasonValue as string;
                    if (reason == null) return failed;
                }
                if (reason != null && reason.Length > 160) return failed;

                bool newIdentity = !String.Equals(lastPanelNonce, hostNonce, StringComparison.Ordinal) ||
                    lastPanelPid != pid || lastPanelBirthFileTime != birth;
                if (newIdentity)
                {
                    lastPanelNonce = hostNonce;
                    lastPanelPid = pid;
                    lastPanelBirthFileTime = birth;
                    lastPanelSequence = -1;
                    lastPanelFingerprint = null;
                    lastPanelHeartbeat = 0;
                    hasPanelHeartbeat = false;
                    lastVerifiedPanel = null;
                }

                string fingerprint = raw;
                failed.Reason = "panel-sequence";
                if (sequence < lastPanelSequence) return failed;
                long currentTick = monotonicClock();
                if (fingerprint != lastPanelFingerprint)
                {
                    if (sequence <= lastPanelSequence) return failed;
                    lastPanelSequence = sequence;
                    lastPanelFingerprint = fingerprint;
                    lastPanelHeartbeat = currentTick;
                    hasPanelHeartbeat = true;
                }
                else if (!hasPanelHeartbeat || (currentTick - lastPanelHeartbeat) / (double)Stopwatch.Frequency > 3) return failed;
                var verified = new PinkyPanelLease { Verified = true, Phase = phase, DesiredWidthDip = width, DesiredHeightDip = height, PanelHwnd = hwnd, Reason = reason, Sequence = sequence };
                lastVerifiedPanel = CopyPanel(verified);
                return verified;
            }
            catch (UnauthorizedAccessException) { accessDenied = true; return failed; }
            catch (SecurityException) { accessDenied = true; return failed; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 5) accessDenied = true; return failed; }
            catch (IOException ex)
            {
                int code = ex.HResult & 0xffff;
                if (code != 2 && code != 3 && code != 32 && code != 33) return failed;
                return ReadCachedLayout(hostNonce, failed);
            }
            catch { return failed; }
        }

        private PinkyPanelLease ReadCachedLayout(string hostNonce, PinkyPanelLease failed)
        {
            if (accessDenied || lastVerifiedPanel == null || !hasPanelHeartbeat || lastPanelNonce != hostNonce)
                return failed;
            try
            {
                if (!validator(lastPanelPid, lastPanelBirthFileTime, executable)) return failed;
                double age = (monotonicClock() - lastPanelHeartbeat) / (double)Stopwatch.Frequency;
                if (age < 0 || age > 3) return failed;
                return CopyPanel(lastVerifiedPanel);
            }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            catch (SecurityException) { accessDenied = true; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 5) accessDenied = true; }
            catch { }
            return failed;
        }

        private static PinkyPanelLease CopyPanel(PinkyPanelLease panel)
        {
            return new PinkyPanelLease { Verified = panel.Verified, Phase = panel.Phase,
                DesiredWidthDip = panel.DesiredWidthDip, DesiredHeightDip = panel.DesiredHeightDip,
                PanelHwnd = panel.PanelHwnd, Reason = panel.Reason, Sequence = panel.Sequence };
        }

        public void PublishHost(PinkyHostLease lease)
        {
            if (lease == null) throw new ArgumentNullException("lease");
            if (!Enabled || accessDenied) return;
            if (!ValidNonce(lease.Nonce) || lease.ProcessId <= 0 || lease.ProcessBirthFileTime <= 0 || lease.Hwnd == IntPtr.Zero || lease.Sequence < 0 || lease.Sequence > 9007199254740991L || lease.Dpi < 48 || lease.Dpi > 960 || lease.ClientRectPhysical.Width <= 0 || lease.ClientRectPhysical.Height <= 0 || lease.ClientRectPhysical.Width > 16384 || lease.ClientRectPhysical.Height > 16384) throw new ArgumentException("Invalid host lease.");
            if (!String.Equals(lastHostNonce, lease.Nonce, StringComparison.Ordinal))
            {
                lastHostNonce = lease.Nonce;
                lastHostSequence = -1;
            }
            if (lease.Sequence <= lastHostSequence) throw new ArgumentException("Host sequence must increase.");
            var data = new Dictionary<string, object>();
            data["schema"] = "pinky.sidebar-host.v1";
            data["nonce"] = lease.Nonce;
            data["pid"] = lease.ProcessId;
            data["sequence"] = lease.Sequence;
            data["processBirthFileTime"] = lease.ProcessBirthFileTime.ToString(CultureInfo.InvariantCulture);
            data["hwnd"] = "0x" + lease.Hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            data["updatedUtc"] = lease.UpdatedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            data["visible"] = lease.Visible;
            var rect = new Dictionary<string, object>();
            rect["x"] = lease.ClientRectPhysical.X;
            rect["y"] = lease.ClientRectPhysical.Y;
            rect["width"] = lease.ClientRectPhysical.Width;
            rect["height"] = lease.ClientRectPhysical.Height;
            data["clientRectPhysical"] = rect;
            data["dpi"] = lease.Dpi;
            try
            {
                AtomicWrite(Path.Combine(directory, "host.json"), new JavaScriptSerializer().Serialize(data));
                lastHostSequence = lease.Sequence;
            }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            catch (SecurityException) { accessDenied = true; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 5) accessDenied = true; }
        }

        public void PublishAppearance(string theme)
        {
            if (!Enabled || accessDenied || appearanceAccessDenied) return;
            if (theme != "tide" && theme != "pine" && theme != "iris" && theme != "amber") return;
            if (String.Equals(lastAppearanceTheme, theme, StringComparison.Ordinal)) return;

            long now = monotonicClock();
            if (hasAppearanceAttempt && String.Equals(lastAppearanceAttemptTheme, theme, StringComparison.Ordinal) &&
                now - lastAppearanceAttempt < Stopwatch.Frequency * 5L) return;

            hasAppearanceAttempt = true;
            lastAppearanceAttempt = now;
            lastAppearanceAttemptTheme = theme;
            var data = new Dictionary<string, object>();
            data["schema"] = "pinky.sidebar-appearance.v1";
            data["theme"] = theme;
            try
            {
                AtomicWrite(Path.Combine(directory, "appearance.json"), new JavaScriptSerializer().Serialize(data));
                lastAppearanceTheme = theme;
            }
            catch (UnauthorizedAccessException) { appearanceAccessDenied = true; }
            catch (SecurityException) { appearanceAccessDenied = true; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 5) appearanceAccessDenied = true; }
            catch (IOException) { }
        }

        public void ClearHost(string hostNonce)
        {
            if (!Enabled || accessDenied || !ValidNonce(hostNonce)) return;
            try
            {
                string file = Path.Combine(directory, "host.json");
                EnsurePlainAncestors(directory);
                EnsurePlainFile(file);
                if (!File.Exists(file)) return;
                IDictionary<string, object> d = Parse(ReadBounded(file));
                if (d == null || Text(d, "schema") != "pinky.sidebar-host.v1" || Text(d, "nonce") != hostNonce) return;
                d["visible"] = false;
                d["updatedUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                long seq = Long(d, "sequence");
                const long maxSafeSequence = 9007199254740991L;
                if (seq < 0 || seq >= maxSafeSequence) return;
                long nextSequence = seq + 1;
                if (String.Equals(lastHostNonce, hostNonce, StringComparison.Ordinal))
                {
                    if (lastHostSequence >= maxSafeSequence) return;
                    nextSequence = Math.Max(nextSequence, lastHostSequence + 1);
                }
                if (nextSequence > maxSafeSequence) return;
                d["sequence"] = nextSequence;
                AtomicWrite(file, new JavaScriptSerializer().Serialize(d));
                lastHostNonce = hostNonce;
                lastHostSequence = nextSequence;
            }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            catch (SecurityException) { accessDenied = true; }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 5) accessDenied = true; }
            catch { }
        }

        private static PinkyHostConnection Disabled(bool denied = false)
        {
            string temp = Path.GetTempPath();
            var connection = new PinkyHostConnection(temp, Path.Combine(temp, "pinky.exe"), null, null);
            connection.Enabled = false;
            connection.accessDenied = denied;
            return connection;
        }

        private static IDictionary<string, object> Parse(string raw)
        {
            if (raw == null || raw.Length > MaxFileBytes) return null;
            var serializer = new JavaScriptSerializer { MaxJsonLength = MaxFileBytes, RecursionLimit = 8 };
            return serializer.DeserializeObject(raw) as IDictionary<string, object>;
        }

        private static bool HasOnly(IDictionary<string, object> data, params string[] names)
        {
            foreach (string key in data.Keys)
            {
                if (Array.IndexOf(names, key) < 0) return false;
            }
            return true;
        }

        private static string Text(IDictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string : null;
        }

        private static bool Bool(IDictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static int Integer(IDictionary<string, object> data, string key)
        {
            object value;
            int result;
            return data.TryGetValue(key, out value) &&
                Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result : -1;
        }

        private static long Long(IDictionary<string, object> data, string key)
        {
            object value;
            long result;
            return data.TryGetValue(key, out value) &&
                Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result : -1;
        }

        private static long LongString(IDictionary<string, object> data, string key)
        {
            string value = Text(data, key);
            long result;
            return value != null && Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        private static double Number(IDictionary<string, object> data, string key)
        {
            object value;
            double result;
            return data.TryGetValue(key, out value) &&
                Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result : Double.NaN;
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static bool ValidNonce(string value)
        {
            return value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-fA-F]{32}$");
        }

        private static bool TryHandle(string text, out IntPtr value)
        {
            long parsed;
            value = IntPtr.Zero;
            if (text == null || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !Int64.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)) return false;
            value = new IntPtr(parsed);
            return value != IntPtr.Zero;
        }
        private static string ReadBounded(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var memory = new MemoryStream())
            {
                byte[] buffer = new byte[2048]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { if (memory.Length + read > MaxFileBytes) return null; memory.Write(buffer, 0, read); }
                return new UTF8Encoding(false, true).GetString(memory.ToArray());
            }
        }
        private void AtomicWrite(string file, string content)
        {
            EnsurePlainAncestors(directory); EnsurePlainFile(file); Directory.CreateDirectory(directory);
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(content);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                EnsurePlainFile(temp);
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static void EnsurePlainFile(string file) { if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse point rejected."); }
        private static void EnsurePlainAncestors(string path)
        {
            string full = Path.GetFullPath(path);
            string current = Path.GetPathRoot(full);
            string rest = full.Substring(current.Length);
            foreach (string part in rest.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse point rejected.");
            }
        }
        private static bool ValidateProcessIdentity(int pid, long birth, string expectedExecutable)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    long actual = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                    string path = Path.GetFullPath(process.MainModule.FileName);
                    return actual == birth && String.Equals(path, expectedExecutable, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (UnauthorizedAccessException) { throw; }
            catch (SecurityException) { throw; }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 5) throw;
                return false;
            }
            catch { return false; }
        }
    }
}
