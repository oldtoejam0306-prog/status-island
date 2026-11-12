using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IslandIntegrated
{
    public sealed class DeviceBatterySample
    {
        public string StableId { get; private set; }
        public string Name { get; private set; }
        public string Platform { get; private set; }
        public string Model { get; private set; }
        public bool Online { get; private set; }
        public bool Paused { get; private set; }
        public double? Level { get; private set; }
        public bool? Charging { get; private set; }
        public bool? Plugged { get; private set; }
        public bool? Full { get; private set; }
        public DateTimeOffset? BatteryAtUtc { get; private set; }

        public DeviceBatterySample(string stableId, string name, string platform, string model,
            bool online, bool paused, double? level, bool? charging, bool? plugged,
            bool? full, DateTimeOffset? batteryAtUtc)
        {
            StableId = stableId ?? String.Empty;
            Name = name ?? String.Empty;
            Platform = platform ?? String.Empty;
            Model = model ?? String.Empty;
            Online = online;
            Paused = paused;
            Level = level;
            Charging = charging;
            Plugged = plugged;
            Full = full;
            BatteryAtUtc = batteryAtUtc.HasValue ? batteryAtUtc.Value.ToUniversalTime() : (DateTimeOffset?)null;
        }

        public bool HasFreshLevel(DateTimeOffset now)
        {
            if (!Online || Paused || !Level.HasValue || !BatteryAtUtc.HasValue ||
                Double.IsNaN(Level.Value) || Double.IsInfinity(Level.Value) || Level.Value < 0 || Level.Value > 100)
                return false;
            double age = (now.ToUniversalTime() - BatteryAtUtc.Value).TotalSeconds;
            return age >= -PinkyBatteryDocumentParser.MaximumFutureClockSkewSeconds && age <= 180;
        }
    }

    public sealed class PinkyBatterySnapshot
    {
        private readonly ReadOnlyCollection<DeviceBatterySample> devices;
        public bool Verified { get; private set; }
        public bool AccessDenied { get; private set; }
        public string Reason { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessBirthFileTime { get; private set; }
        public string Nonce { get; private set; }
        public long Sequence { get; private set; }
        public DateTimeOffset UpdatedUtc { get; private set; }
        public IList<DeviceBatterySample> Devices { get { return devices; } }

        internal PinkyBatterySnapshot(bool verified, bool accessDenied, string reason, int pid,
            long birth, string nonce, long sequence, DateTimeOffset updatedUtc,
            IList<DeviceBatterySample> samples)
        {
            Verified = verified;
            AccessDenied = accessDenied;
            Reason = reason ?? String.Empty;
            ProcessId = pid;
            ProcessBirthFileTime = birth;
            Nonce = nonce ?? String.Empty;
            Sequence = sequence;
            UpdatedUtc = updatedUtc.ToUniversalTime();
            devices = new ReadOnlyCollection<DeviceBatterySample>(
                (samples ?? new List<DeviceBatterySample>()).ToArray());
        }

        public static PinkyBatterySnapshot Unavailable(string reason, bool accessDenied)
        {
            return new PinkyBatterySnapshot(false, accessDenied, reason, 0, 0, String.Empty, -1,
                DateTimeOffset.MinValue, new List<DeviceBatterySample>());
        }

        public bool IsHeartbeatFresh(DateTimeOffset now)
        {
            if (!Verified) return false;
            double age = (now.ToUniversalTime() - UpdatedUtc).TotalSeconds;
            return age >= -PinkyBatteryDocumentParser.MaximumFutureClockSkewSeconds && age <= PinkyBatteryDocumentParser.MaximumHeartbeatAgeSeconds;
        }
    }

    public sealed class PinkyBatteryDocumentParser
    {
        public const int MaximumDocumentBytes = 64 * 1024;
        public const int MaximumDeviceCount = 32;
        public const int MaximumHeartbeatAgeSeconds = 45;
        public const int MaximumFutureClockSkewSeconds = 5;

        public static PinkyBatterySnapshot Parse(string json, string expectedNonce, int expectedPid,
            long expectedBirthFileTime, DateTimeOffset now, Func<int, long, bool> validateIdentity)
        {
            try
            {
                if (String.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
                    return PinkyBatterySnapshot.Unavailable("oversized-or-empty", false);
                if (!ValidNonce(expectedNonce) || expectedPid <= 0 || expectedBirthFileTime <= 0 || validateIdentity == null)
                    return PinkyBatterySnapshot.Unavailable("invalid-expected-identity", false);

                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumDocumentBytes, RecursionLimit = 8 };
                var root = serializer.DeserializeObject(json) as IDictionary<string, object>;
                if (root == null || !HasOnly(root, "schema", "pid", "processBirthFileTime", "nonce", "sequence", "updatedUtc", "devices"))
                    return PinkyBatterySnapshot.Unavailable("schema", false);
                if (Text(root, "schema") != "pinky.sidebar-batteries.v1" ||
                    Text(root, "nonce") != expectedNonce)
                    return PinkyBatterySnapshot.Unavailable("identity", false);

                int pid = Integer(root, "pid");
                long birth = LongString(root, "processBirthFileTime");
                long sequence = Long(root, "sequence");
                DateTimeOffset updated;
                if (pid != expectedPid || birth != expectedBirthFileTime || sequence < 0 || sequence > 9007199254740991L ||
                    !TryUtc(root, "updatedUtc", out updated))
                    return PinkyBatterySnapshot.Unavailable("identity-or-time", false);
                if (!validateIdentity(pid, birth))
                    return PinkyBatterySnapshot.Unavailable("process-identity", false);
                double heartbeatAge = (now.ToUniversalTime() - updated).TotalSeconds;
                if (heartbeatAge < -MaximumFutureClockSkewSeconds || heartbeatAge > MaximumHeartbeatAgeSeconds)
                    return PinkyBatterySnapshot.Unavailable("heartbeat-stale-or-future", false);

                object deviceValue;
                var list = root.TryGetValue("devices", out deviceValue) ? deviceValue as object[] : null;
                if (list == null || list.Length > MaximumDeviceCount)
                    return PinkyBatterySnapshot.Unavailable("devices-shape", false);

                var samples = new List<DeviceBatterySample>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (object item in list)
                {
                    var d = item as IDictionary<string, object>;
                    if (d == null || !HasOnly(d, "stableId", "name", "platform", "model", "online", "paused",
                        "level", "charging", "plugged", "full", "batteryAtUtc"))
                        return PinkyBatterySnapshot.Unavailable("device-schema", false);

                    string stableId = Text(d, "stableId");
                    string name = Text(d, "name");
                    string platform = Text(d, "platform");
                    string model = Text(d, "model");
                    bool online, paused;
                    if (!BoundedText(stableId, 128, false) || !BoundedText(name, 160, false) ||
                        !BoundedText(platform, 80, true) || !BoundedText(model, 160, true) ||
                        !TryBool(d, "online", out online) || !TryBool(d, "paused", out paused) ||
                        !seen.Add(stableId))
                        return PinkyBatterySnapshot.Unavailable("device-identity-or-boolean", false);

                    double? level;
                    bool? charging, plugged, full;
                    DateTimeOffset? batteryAt;
                    if (!TryNullableNumber(d, "level", out level) ||
                        !TryNullableBoolean(d, "charging", out charging) ||
                        !TryNullableBoolean(d, "plugged", out plugged) ||
                        !TryNullableBoolean(d, "full", out full) ||
                        !TryNullableUtc(d, "batteryAtUtc", out batteryAt))
                        return PinkyBatterySnapshot.Unavailable("device-value-shape", false);

                    // Reject invalid values at sample scope. Other devices remain usable.
                    if (level.HasValue && (Double.IsNaN(level.Value) || Double.IsInfinity(level.Value) || level.Value < 0 || level.Value > 100))
                        level = null;
                    if (batteryAt.HasValue && (batteryAt.Value - now.ToUniversalTime()).TotalSeconds > MaximumFutureClockSkewSeconds)
                    {
                        level = null;
                        batteryAt = null;
                    }
                    samples.Add(new DeviceBatterySample(stableId, name, platform, model, online, paused,
                        level, charging, plugged, full, batteryAt));
                }
                return new PinkyBatterySnapshot(true, false, String.Empty, pid, birth, expectedNonce,
                    sequence, updated, samples);
            }
            catch (UnauthorizedAccessException) { return PinkyBatterySnapshot.Unavailable("access-denied", true); }
            catch (SecurityException) { return PinkyBatterySnapshot.Unavailable("access-denied", true); }
            catch (Exception) { return PinkyBatterySnapshot.Unavailable("invalid-document", false); }
        }

        private static bool TryUtc(IDictionary<string, object> data, string key, out DateTimeOffset result)
        {
            result = default(DateTimeOffset);
            string value = Text(data, key);
            if (String.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out result)) return false;
            return result.Offset == TimeSpan.Zero;
        }

        private static bool TryNullableUtc(IDictionary<string, object> data, string key, out DateTimeOffset? result)
        {
            object value;
            result = null;
            if (!data.TryGetValue(key, out value)) return false;
            if (value == null) return true;
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(value as string, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed) ||
                parsed.Offset != TimeSpan.Zero) return false;
            result = parsed;
            return true;
        }

        private static bool TryNullableNumber(IDictionary<string, object> data, string key, out double? result)
        {
            object value;
            result = null;
            if (!data.TryGetValue(key, out value)) return false;
            if (value == null) return true;
            double parsed;
            if (!Double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed)) return false;
            result = parsed;
            return true;
        }

        private static bool TryNullableBoolean(IDictionary<string, object> data, string key, out bool? result)
        {
            object value;
            result = null;
            if (!data.TryGetValue(key, out value)) return false;
            if (value == null) return true;
            if (!(value is bool)) return false;
            result = (bool)value;
            return true;
        }

        private static bool TryBool(IDictionary<string, object> data, string key, out bool result)
        {
            object value;
            result = false;
            if (!data.TryGetValue(key, out value) || !(value is bool)) return false;
            result = (bool)value;
            return true;
        }

        private static int Integer(IDictionary<string, object> data, string key)
        {
            object value; int result;
            return data.TryGetValue(key, out value) && Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        private static long Long(IDictionary<string, object> data, string key)
        {
            object value; long result;
            return data.TryGetValue(key, out value) && Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.None, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        private static long LongString(IDictionary<string, object> data, string key)
        {
            string value = Text(data, key); long result;
            return value != null && Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) ? result : -1;
        }

        private static string Text(IDictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string : null;
        }

        private static bool HasOnly(IDictionary<string, object> data, params string[] names)
        {
            foreach (string key in data.Keys) if (Array.IndexOf(names, key) < 0) return false;
            foreach (string name in names) if (!data.ContainsKey(name)) return false;
            return true;
        }

        private static bool BoundedText(string value, int maxLength, bool mayBeEmpty)
        {
            return value != null && value.Length <= maxLength && (mayBeEmpty || value.Length > 0);
        }

        private static bool ValidNonce(string value)
        {
            return value != null && Regex.IsMatch(value, "^[0-9a-fA-F]{32}$");
        }
    }

    public sealed class DeviceBatterySlot
    {
        public string Label { get; private set; }
        public DeviceBatterySample Sample { get; private set; }
        public bool NeedsSelection { get; private set; }
        public string UnavailableReason { get; private set; }

        internal DeviceBatterySlot(string label, DeviceBatterySample sample, bool needsSelection, string reason)
        {
            Label = label;
            Sample = sample;
            NeedsSelection = needsSelection;
            UnavailableReason = reason ?? String.Empty;
        }

        public bool Available(DateTimeOffset now) { return Sample != null && Sample.HasFreshLevel(now); }
        public string Value(DateTimeOffset now)
        {
            return Available(now) ? Math.Round(Sample.Level.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%" : "不可用";
        }
        public string Tooltip(DateTimeOffset now)
        {
            if (!Available(now))
                return Label + "：不可用" + (NeedsSelection ? "（检测到多台同类设备，需要选择显示的设备）" : String.Empty);
            string state = Sample.Full == true ? "已充满" : Sample.Charging == true ? "充电中" :
                Sample.Plugged == true ? "已接入电源" : Sample.Charging == false ? "未充电" : "充电状态未知";
            string observed = Sample.BatteryAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) + " 本地时间";
            string name = String.IsNullOrWhiteSpace(Sample.Model) ? Sample.Name : Sample.Name + "（" + Sample.Model + "）";
            return Label + "：" + name + "，" + Value(now) + "，" + state + "，观测时间 " + observed;
        }
    }

    public sealed class DeviceBatterySnapshot
    {
        public DeviceBatterySlot Mac { get; private set; }
        public DeviceBatterySlot IPad { get; private set; }
        public DeviceBatterySlot Phone { get; private set; }
        public DateTimeOffset UpdatedUtc { get; private set; }

        internal DeviceBatterySnapshot(DeviceBatterySlot mac, DeviceBatterySlot ipad, DeviceBatterySlot phone, DateTimeOffset updatedUtc)
        {
            Mac = mac; IPad = ipad; Phone = phone; UpdatedUtc = updatedUtc.ToUniversalTime();
        }

        public DeviceBatterySlot Slot(int index) { return index == 0 ? Mac : index == 1 ? IPad : index == 2 ? Phone : null; }
        public string Signature(DateTimeOffset now)
        {
            return SlotSignature(Mac, now) + "|" + SlotSignature(IPad, now) + "|" + SlotSignature(Phone, now);
        }
        public string Tooltip(int index, DateTimeOffset now)
        {
            var slot = Slot(index);
            return slot == null ? String.Empty : slot.Tooltip(now);
        }
        private static string SlotSignature(DeviceBatterySlot slot, DateTimeOffset now)
        {
            if (slot == null || !slot.Available(now)) return (slot == null ? "missing" : slot.Label) + "|unavailable|" + (slot != null && slot.NeedsSelection ? "select" : "");
            var s = slot.Sample;
            return slot.Label + "|" + s.StableId + "|" + s.Level.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                "|" + NullableBool(s.Charging) + "|" + NullableBool(s.Plugged) + "|" + NullableBool(s.Full) +
                "|" + s.BatteryAtUtc.Value.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        private static string NullableBool(bool? value) { return value.HasValue ? (value.Value ? "1" : "0") : "?"; }
    }

    public sealed class DeviceBatteryConfiguration
    {
        public bool Enabled { get; private set; }
        public string Host { get; private set; }
        public string Account { get; private set; }
        public string KeyFile { get; private set; }
        public string KnownHostsFile { get; private set; }
        public string MacStableId { get; private set; }
        public string IPadStableId { get; private set; }
        public string PhoneStableId { get; private set; }
        public bool AccessDenied { get; private set; }

        private DeviceBatteryConfiguration(bool enabled, string host, string account, string keyFile,
            string knownHostsFile, string macId, string ipadId, string phoneId, bool accessDenied)
        {
            Enabled = enabled; Host = host ?? String.Empty; Account = account ?? String.Empty;
            KeyFile = keyFile ?? String.Empty; KnownHostsFile = knownHostsFile ?? String.Empty;
            MacStableId = macId ?? String.Empty; IPadStableId = ipadId ?? String.Empty; PhoneStableId = phoneId ?? String.Empty;
            AccessDenied = accessDenied;
        }

        public static DeviceBatteryConfiguration Load(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsPlainPath(path))
                    return Disabled(false);
                if (new FileInfo(path).Length > 8192) return Disabled(false);
                var serializer = new JavaScriptSerializer { MaxJsonLength = 8192, RecursionLimit = 4 };
                var data = serializer.DeserializeObject(File.ReadAllText(path, new UTF8Encoding(false, true))) as IDictionary<string, object>;
                if (data == null || !HasOnly(data, "schema", "enabled", "host", "account", "keyFile", "knownHostsFile", "macStableId", "ipadStableId", "phoneStableId"))
                    return Disabled(false);
                if (Text(data, "schema") != "pinky.device-battery-connection.v1") return Disabled(false);
                bool enabled; if (!TryBool(data, "enabled", out enabled)) return Disabled(false);
                string host = Text(data, "host"), account = Text(data, "account"), key = Text(data, "keyFile"), known = Text(data, "knownHostsFile");
                string mac = Text(data, "macStableId"), ipad = Text(data, "ipadStableId"), phone = Text(data, "phoneStableId");
                if (!Bounded(host, 253) || !Bounded(account, 64) || !Bounded(key, 1024) || !Bounded(known, 1024) ||
                    !Bounded(mac, 128) || !Bounded(ipad, 128) || !Bounded(phone, 128)) return Disabled(false);
                if (enabled)
                {
                    if (!Regex.IsMatch(host, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$") ||
                        !Regex.IsMatch(account, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$") ||
                        !Path.IsPathRooted(key) || !Path.IsPathRooted(known) ||
                        !File.Exists(key) || !File.Exists(known) || !IsPlainPath(key) || !IsPlainPath(known))
                        return Disabled(false);
                }
                return new DeviceBatteryConfiguration(enabled, host, account, key, known, mac, ipad, phone, false);
            }
            catch (UnauthorizedAccessException) { return Disabled(true); }
            catch (SecurityException) { return Disabled(true); }
            catch { return Disabled(false); }
        }

        private static DeviceBatteryConfiguration Disabled(bool denied)
        {
            return new DeviceBatteryConfiguration(false, String.Empty, String.Empty, String.Empty, String.Empty, String.Empty, String.Empty, String.Empty, denied);
        }
        private static bool Bounded(string value, int length) { return value != null && value.Length <= length; }
        private static bool IsPlainPath(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                string parent = Path.GetDirectoryName(current);
                if (String.IsNullOrEmpty(parent) || String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
            return true;
        }
        private static bool TryBool(IDictionary<string, object> d, string key, out bool value)
        {
            object raw; value = false;
            if (!d.TryGetValue(key, out raw) || !(raw is bool)) return false;
            value = (bool)raw; return true;
        }
        private static string Text(IDictionary<string, object> d, string key) { object value; return d.TryGetValue(key, out value) ? value as string : null; }
        private static bool HasOnly(IDictionary<string, object> data, params string[] names)
        {
            foreach (string key in data.Keys) if (Array.IndexOf(names, key) < 0) return false;
            foreach (string name in names) if (!data.ContainsKey(name)) return false;
            return true;
        }
    }

    public sealed class DeviceBatterySource : IDisposable
    {
        public const int MacPollIntervalSeconds = 60;
        public const int MacTimeoutMilliseconds = 12000;
        public const int MaximumMacTextCharacters = 16 * 1024;
        private readonly object gate = new object();
        private readonly DeviceBatteryConfiguration configuration;
        private readonly Func<DeviceBatterySample> macReader;
        private readonly Func<long> monotonicClock;
        private PinkyBatterySnapshot pinky;
        private DeviceBatterySample mac;
        private string macReason = "not-sampled";
        private bool macAccessDenied;
        private long nextMacPoll;
        private int macInFlight;
        private bool stopped;
        private Process activeSsh;
        private CancellationTokenSource cancellation = new CancellationTokenSource();

        public event EventHandler Changed;

        public DeviceBatterySource(string configurationFile)
            : this(DeviceBatteryConfiguration.Load(configurationFile), null, Stopwatch.GetTimestamp) { }

        public DeviceBatterySource(DeviceBatteryConfiguration configuration, Func<DeviceBatterySample> testReader, Func<long> testClock)
        {
            this.configuration = configuration ?? DeviceBatteryConfiguration.Load(null);
            macReader = testReader;
            monotonicClock = testClock ?? Stopwatch.GetTimestamp;
            macAccessDenied = this.configuration.AccessDenied;
        }

        public bool MacAccessDenied { get { lock (gate) return macAccessDenied; } }
        public bool MacEnabled { get { return configuration.Enabled; } }
        public string Signature(DateTimeOffset now) { return Snapshot(now).Signature(now); }

        public DeviceBatterySnapshot Snapshot(DateTimeOffset now)
        {
            DeviceBatterySample macCopy;
            PinkyBatterySnapshot pinkyCopy;
            bool denied;
            string reason;
            lock (gate) { macCopy = mac; pinkyCopy = pinky; denied = macAccessDenied; reason = macReason; }
            var macSlot = macCopy != null && macCopy.HasFreshLevel(now)
                ? new DeviceBatterySlot("Mac", macCopy, false, String.Empty)
                : new DeviceBatterySlot("Mac", null, false, denied ? "access-denied" : reason);
            var samples = pinkyCopy != null && pinkyCopy.IsHeartbeatFresh(now)
                ? pinkyCopy.Devices.Where(x => x != null && x.Online && !x.Paused).ToArray()
                : new DeviceBatterySample[0];
            var ipad = Select(samples, configuration.IPadStableId, true, "iPad");
            var phone = Select(samples, configuration.PhoneStableId, false, "手机");
            return new DeviceBatterySnapshot(macSlot, ipad, phone, DateTimeOffset.UtcNow);
        }

        public void ObservePinky(PinkyBatterySnapshot snapshot, DateTimeOffset now)
        {
            bool changed;
            lock (gate)
            {
                string before = SignatureUnsafe(now);
                if (snapshot == null || !snapshot.Verified || !snapshot.IsHeartbeatFresh(now)) pinky = null;
                else pinky = snapshot;
                changed = before != SignatureUnsafe(now);
            }
            if (changed) RaiseChanged();
        }

        public void StartIfDue(DateTimeOffset now)
        {
            lock (gate) if (stopped || !configuration.Enabled || macAccessDenied) return;
            long ticks = monotonicClock();
            if (ticks < Interlocked.Read(ref nextMacPoll)) return;
            if (Interlocked.CompareExchange(ref macInFlight, 1, 0) != 0) return;
            Interlocked.Exchange(ref nextMacPoll, ticks + Stopwatch.Frequency * MacPollIntervalSeconds);
            Task.Run(delegate
            {
                DeviceBatterySample sample = null;
                string reason = "unavailable";
                bool denied = false;
                try
                {
                    sample = macReader != null ? macReader() : ReadMacBattery(cancellation.Token, out reason, out denied);
                    if (sample != null && sample.HasFreshLevel(DateTimeOffset.UtcNow)) reason = String.Empty;
                    else { sample = null; if (String.IsNullOrEmpty(reason)) reason = "unavailable"; }
                }
                catch (OperationCanceledException) { reason = "stopped"; }
                catch (UnauthorizedAccessException) { reason = "access-denied"; denied = true; }
                catch (SecurityException) { reason = "access-denied"; denied = true; }
                catch (Win32Exception ex)
                {
                    denied = ex.NativeErrorCode == 5;
                    reason = denied ? "access-denied" : "ssh-start-failed";
                }
                catch { reason = "unavailable"; }
                bool changed;
                lock (gate)
                {
                    string before = SignatureUnsafe(DateTimeOffset.UtcNow);
                    if (!stopped)
                    {
                        mac = sample;
                        macReason = reason;
                        macAccessDenied = denied;
                    }
                    changed = before != SignatureUnsafe(DateTimeOffset.UtcNow);
                }
                Interlocked.Exchange(ref macInFlight, 0);
                if (changed) RaiseChanged();
            });
        }

        public static DeviceBatterySample ParseMacPmset(string output, string stableId, DateTimeOffset observedUtc)
        {
            if (String.IsNullOrWhiteSpace(output)) return null;
            string batteryLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("-InternalBattery-", StringComparison.OrdinalIgnoreCase));
            if (batteryLine == null) return null;
            string[] fields = batteryLine.Split(';');
            string firstField = fields.Length > 0 ? fields[0] : String.Empty;
            string stateField = fields.Length > 1 ? fields[1].Trim() : String.Empty;
            string detailField = fields.Length > 2 ? String.Join(";", fields.Skip(2)).Trim() : String.Empty;
            var battery = Regex.Match(firstField, @"^\s*-(?<name>[^\s(]+)(?:\([^)]*\))?\s+.*?(?<![A-Za-z0-9_.+-])(?<level>\d{1,3}(?:\.\d+)?)\s*%\s*$");
            if (!battery.Success) return null;
            string name = battery.Groups["name"].Value.Trim();
            bool present = !Regex.IsMatch(detailField, @"(?i)\bpresent\s*:\s*false\b");
            bool? plugged = null;
            if (Regex.IsMatch(output, @"(?i)Now drawing from\s+'AC Power'")) plugged = true;
            else if (Regex.IsMatch(output, @"(?i)Now drawing from\s+'Battery Power'")) plugged = false;
            double? level = null;
            double parsed;
            if (Double.TryParse(battery.Groups["level"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed >= 0 && parsed <= 100)
                level = parsed;
            bool? charging = null;
            if (String.Equals(stateField, "not charging", StringComparison.OrdinalIgnoreCase)) charging = false;
            else if (String.Equals(stateField, "charging", StringComparison.OrdinalIgnoreCase)) charging = true;
            else if (String.Equals(stateField, "discharging", StringComparison.OrdinalIgnoreCase) || String.Equals(stateField, "charged", StringComparison.OrdinalIgnoreCase)) charging = false;
            bool? full = String.Equals(stateField, "charged", StringComparison.OrdinalIgnoreCase) ? (bool?)true : null;
            if (!present || !level.HasValue) level = null;
            return new DeviceBatterySample(String.IsNullOrWhiteSpace(stableId) ? "mac" : stableId,
                "Mac", "macOS", name, present, false, level, charging, plugged, full, observedUtc);
        }

        public void Stop()
        {
            Process process = null;
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                try { cancellation.Cancel(); } catch { }
                process = activeSsh;
                activeSsh = null;
                pinky = null;
                mac = null;
            }
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
            }
            try { cancellation.Dispose(); } catch { }
            RaiseChanged();
        }

        public void Dispose() { Stop(); }

        private string SignatureUnsafe(DateTimeOffset now)
        {
            var macSlot = mac != null && mac.HasFreshLevel(now) ? new DeviceBatterySlot("Mac", mac, false, String.Empty) : new DeviceBatterySlot("Mac", null, false, macReason);
            var samples = pinky != null && pinky.IsHeartbeatFresh(now) ? pinky.Devices.Where(x => x != null && x.Online && !x.Paused).ToArray() : new DeviceBatterySample[0];
            return new DeviceBatterySnapshot(macSlot, Select(samples, configuration.IPadStableId, true, "iPad"),
                Select(samples, configuration.PhoneStableId, false, "手机"), now).Signature(now);
        }

        private static DeviceBatterySlot Select(DeviceBatterySample[] devices, string configuredId, bool ipad, string label)
        {
            if (!String.IsNullOrWhiteSpace(configuredId))
            {
                var configured = devices.Where(x => String.Equals(x.StableId, configuredId, StringComparison.Ordinal)).ToArray();
                return configured.Length == 1 ? new DeviceBatterySlot(label, configured[0], false, String.Empty) : new DeviceBatterySlot(label, null, false, "configured-device-unavailable");
            }
            var matches = devices.Where(x => Matches(x, ipad)).ToArray();
            if (matches.Length == 1) return new DeviceBatterySlot(label, matches[0], false, String.Empty);
            if (matches.Length > 1) return new DeviceBatterySlot(label, null, true, "multiple-devices");
            return new DeviceBatterySlot(label, null, false, "no-matching-device");
        }

        private static bool Matches(DeviceBatterySample sample, bool ipad)
        {
            string text = (sample.Platform + " " + sample.Model + " " + sample.Name).ToLowerInvariant();
            if (ipad) return text.Contains("ipad");
            return text.Contains("iphone") || text.Contains("phone") && !text.Contains("headphone");
        }

        private DeviceBatterySample ReadMacBattery(CancellationToken token, out string reason, out bool accessDenied)
        {
            reason = "unavailable"; accessDenied = false;
            if (token.IsCancellationRequested) throw new OperationCanceledException();
            string ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"OpenSSH\ssh.exe");
            if (!File.Exists(ssh)) { reason = "ssh-missing"; return null; }
            if (!File.Exists(configuration.KeyFile) || !File.Exists(configuration.KnownHostsFile)) { reason = "ssh-key-or-known-hosts-missing"; return null; }
            var info = new ProcessStartInfo(ssh, BuildSshArguments())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(ssh)
            };
            using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                if (!process.Start()) { reason = "ssh-start-failed"; return null; }
                lock (gate) { if (stopped) { try { process.Kill(); } catch { } throw new OperationCanceledException(); } activeSsh = process; }
                var stdout = Task.Factory.StartNew(() => ReadBounded(process.StandardOutput, MaximumMacTextCharacters, token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                var stderr = Task.Factory.StartNew(() => ReadBounded(process.StandardError, MaximumMacTextCharacters, token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    if (!process.WaitForExit(MacTimeoutMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        reason = "ssh-timeout";
                        return null;
                    }
                    Task.WaitAll(new Task[] { stdout, stderr }, 1000);
                    var outResult = stdout.IsCompleted ? stdout.Result : null;
                    var errResult = stderr.IsCompleted ? stderr.Result : null;
                    if (outResult == null || outResult.Oversized) { reason = "ssh-output-invalid"; return null; }
                    if (process.ExitCode != 0)
                    {
                        string error = errResult == null ? String.Empty : errResult.Text;
                        accessDenied = process.ExitCode == 255 &&
                            (error.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             error.IndexOf("host key verification failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             error.IndexOf("identity file", StringComparison.OrdinalIgnoreCase) >= 0);
                        reason = accessDenied ? "ssh-access-denied" : "ssh-nonzero-exit";
                        return null;
                    }
                    var sample = ParseMacPmset(outResult.Text, configuration.MacStableId, DateTimeOffset.UtcNow);
                    if (sample == null) { reason = "pmset-parse-failed"; return null; }
                    reason = String.Empty;
                    return sample;
                }
                finally { lock (gate) { if (ReferenceEquals(activeSsh, process)) activeSsh = null; } }
            }
        }

        private string BuildSshArguments()
        {
            string hostTarget = configuration.Account + "@" + configuration.Host;
            return "-o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile=" + Quote(configuration.KnownHostsFile) +
                " -i " + Quote(configuration.KeyFile) + " " + hostTarget + " /usr/bin/pmset -g batt";
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            var result = new StringBuilder();
            result.Append('"');
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"')
                {
                    result.Append('\\', slashes * 2 + 1);
                    result.Append('"');
                    slashes = 0;
                    continue;
                }
                if (slashes > 0) { result.Append('\\', slashes); slashes = 0; }
                result.Append(ch);
            }
            if (slashes > 0) result.Append('\\', slashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private sealed class BoundedRead
        {
            public string Text;
            public bool Oversized;
        }

        private static BoundedRead ReadBounded(System.IO.StreamReader reader, int max, CancellationToken token)
        {
            var result = new BoundedRead();
            var text = new StringBuilder();
            char[] buffer = new char[1024];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int count = reader.Read(buffer, 0, buffer.Length);
                if (count <= 0) break;
                if (text.Length + count <= max) text.Append(buffer, 0, count);
                else result.Oversized = true;
            }
            result.Text = result.Oversized ? String.Empty : text.ToString();
            return result;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) try { handler(this, EventArgs.Empty); } catch { }
        }
    }
}
