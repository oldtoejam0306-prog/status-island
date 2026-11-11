using System;
using System.IO;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Reflection;
using System.Web.Script.Serialization;
using IslandIntegrated;

internal static class PinkyHostConnectionTests
{
    private static void Main() { Run(); }

    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "pinky-host-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string exe = Path.Combine(root, "pinky.exe");
            var connection = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) => pid == 42 && birth == 77 && expected == exe);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string nonce = "0123456789abcdef0123456789abcdef";
            WritePanel(root, nonce, 42, 77, now, 1);
            Check(connection.ReadPanel(nonce, now).Verified, "fresh nonce accepted");
            Check(!connection.ReadPanel("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", now).Verified, "wrong nonce rejected");
            WritePanel(root, nonce, 42, 77, now.AddSeconds(-4), 2);
            Check(!connection.ReadPanel(nonce, now).Verified, "expired panel rejected");
            WritePanel(root, nonce, 99, 77, now, 3);
            Check(!connection.ReadPanel(nonce, now).Verified, "process mismatch rejected");

            VerifyLeaseSequenceCases(root, exe, now, nonce);
            VerifyAccessDeniedLatch(root, exe, now, nonce);
            VerifyReadTimingAndTransientLease(root, exe, now, nonce);

            var host = new PinkyHostLease { Nonce = nonce, ProcessId = 42, ProcessBirthFileTime = 77, Hwnd = new IntPtr(0x1234), UpdatedUtc = now, Visible = true, ClientRectPhysical = new System.Drawing.Rectangle(1, 2, 300, 400), Dpi = 144, Sequence = 1 };
            var writer = PinkyHostConnection.CreateForTesting(root, exe, null);
            writer.PublishHost(host);
            string hostJson = File.ReadAllText(Path.Combine(root, "host.json"), new UTF8Encoding(false, true));
            Check(hostJson.Contains("pinky.sidebar-host.v1") && hostJson.Contains("processBirthFileTime") && hostJson.Contains("\"sequence\":1"), "atomic host write readable");
            VerifyAppearance(writer, root, hostJson, host, now);
            VerifyAppearanceRetry(root, exe);
            VerifyAppearanceAccessDeniedLatch(root, writer);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void VerifyAppearance(PinkyHostConnection writer, string root, string hostJson, PinkyHostLease host, DateTimeOffset now)
    {
        string appearanceFile = Path.Combine(root, "appearance.json");
        string[] themes = { "tide", "pine", "iris", "amber" };
        foreach (string theme in themes)
        {
            writer.PublishAppearance(theme);
            var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(appearanceFile)) as System.Collections.Generic.IDictionary<string, object>;
            Check(data != null && (string)data["schema"] == "pinky.sidebar-appearance.v1" && (string)data["theme"] == theme,
                "appearance publishes " + theme);
        }
        File.WriteAllText(appearanceFile, "sentinel", new UTF8Encoding(false));
        writer.PublishAppearance("amber");
        Check(File.ReadAllText(appearanceFile) == "sentinel", "same appearance theme does not rewrite");
        writer.PublishAppearance("invalid");
        Check(File.ReadAllText(appearanceFile) == "sentinel", "invalid appearance theme ignored");
        Check(File.ReadAllText(Path.Combine(root, "host.json"), new UTF8Encoding(false, true)) == hostJson,
            "appearance leaves host lease content and sequence unchanged");
        host.Sequence = 2;
        host.UpdatedUtc = now.AddSeconds(1);
        writer.PublishHost(host);
        Check(File.ReadAllText(Path.Combine(root, "host.json"), new UTF8Encoding(false, true)).Contains("\"sequence\":2"),
            "host sequence advances independently after appearance publish");
    }

    private static void VerifyAppearanceRetry(string root, string exe)
    {
        string retryRoot = Path.Combine(root, "appearance-retry");
        Directory.CreateDirectory(retryRoot);
        long tick = 100;
        var connection = PinkyHostConnection.CreateForTesting(retryRoot, exe, null, () => tick);
        string target = Path.Combine(retryRoot, "appearance.json");
        Directory.CreateDirectory(target);
        connection.PublishAppearance("pine");
        Check(!connection.AccessDenied, "appearance IOException does not latch business access denied");
        Directory.Delete(target);
        connection.PublishAppearance("pine");
        Check(!File.Exists(target), "same theme IOException retry is throttled");
        tick += Stopwatch.Frequency * 5L;
        connection.PublishAppearance("pine");
        Check(File.Exists(target), "same theme retries after five monotonic seconds");
    }

    private static void VerifyAppearanceAccessDeniedLatch(string root, PinkyHostConnection writer)
    {
        var field = typeof(PinkyHostConnection).GetField("appearanceAccessDenied", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, "appearance access denied latch is isolated");
        field.SetValue(writer, true);
        string appearanceFile = Path.Combine(root, "appearance.json");
        File.WriteAllText(appearanceFile, "latch-sentinel", new UTF8Encoding(false));
        string hostBefore = File.ReadAllText(Path.Combine(root, "host.json"), new UTF8Encoding(false, true));
        writer.PublishAppearance("tide");
        Check(File.ReadAllText(appearanceFile) == "latch-sentinel" && !writer.AccessDenied,
            "appearance denial latch stops appearance writes without business denial");
        Check(File.ReadAllText(Path.Combine(root, "host.json"), new UTF8Encoding(false, true)) == hostBefore,
            "appearance denial latch leaves host lease unchanged");
    }

    private static void VerifyReadTimingAndTransientLease(string root, string exe, DateTimeOffset now, string nonce)
    {
        long tick = 0;
        var slowIdentity = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, path) =>
        {
            tick += Stopwatch.Frequency * 2L;
            return true;
        }, () => tick);
        WritePanel(root, nonce, 42, 77, now.AddSeconds(1.5), 100);
        Check(slowIdentity.ReadPanel(nonce, now).Verified, "timestamp is checked after identity-read elapsed time");

        tick = 0;
        bool denied = false;
        var reader = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, path) =>
        {
            if (denied) throw new UnauthorizedAccessException();
            return true;
        }, () => tick);
        WritePanel(root, nonce, 42, 77, now, 101);
        Check(reader.ReadPanel(nonce, now).Verified, "transient read fixture has a verified lease");
        using (var locked = new FileStream(Path.Combine(root, "panel.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tick = Stopwatch.Frequency * 2L;
            var retained = reader.ReadPanel(nonce, now.AddSeconds(2));
            Check(retained.Verified && retained.Sequence == 101, "sharing collision retains the same fresh verified layout");
            retained.Phase = "error";
            Check(reader.ReadPanel(nonce, now.AddSeconds(2)).Phase != "error", "returned cached layouts do not expose mutable cache state");
            tick = Stopwatch.Frequency * 4L;
            Check(!reader.ReadPanel(nonce, now.AddSeconds(4)).Verified, "cached layout expires at the original lease boundary");
        }
        tick = Stopwatch.Frequency * 5L;
        WritePanel(root, nonce, 42, 77, now.AddSeconds(5), 102);
        Check(reader.ReadPanel(nonce, now.AddSeconds(5)).Verified, "fresh heartbeat resumes normal layout reads");
        using (var locked = new FileStream(Path.Combine(root, "panel.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            denied = true;
            Check(!reader.ReadPanel(nonce, now.AddSeconds(5)).Verified && reader.AccessDenied,
                "identity access denial disables cache fallback");
        }
        denied = false;
        Check(!reader.ReadPanel(nonce, now.AddSeconds(5)).Verified, "access-denied connection stays disabled");
    }

    private static void VerifyAccessDeniedLatch(string root, string exe, DateTimeOffset now, string nonce)
    {
        int validatorCalls = 0;
        var denied = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) =>
        {
            validatorCalls++;
            throw new UnauthorizedAccessException();
        });
        WritePanel(root, nonce, 42, 77, now, 1);
        denied.ReadPanel(nonce, now);
        denied.ReadPanel(nonce, now);
        Check(denied.AccessDenied && validatorCalls == 1, "access denied latches without retry");
    }

    private static void VerifyLeaseSequenceCases(string root, string exe, DateTimeOffset now, string nonce)
    {
        long clock = 100;
        var connection = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) => pid == 42 && birth == 77 && expected == exe, () => clock);
        WritePanel(root, nonce, 42, 77, now, 1);
        Check(connection.ReadPanel(nonce, now).Verified, "sequence baseline accepted");
        clock += (long)(Stopwatch.Frequency * 3.1);
        Check(!connection.ReadPanel(nonce, now).Verified, "same sequence expires without refresh");

        var changed = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) => true, () => clock);
        WritePanel(root, nonce, 42, 77, now, 2);
        Check(changed.ReadPanel(nonce, now).Verified, "same sequence mutation baseline");
        WritePanel(root, nonce, 42, 77, now, 2, null, 421);
        Check(!changed.ReadPanel(nonce, now).Verified, "changed content with same sequence rejected");

        var restarted = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) => true, () => clock);
        WritePanel(root, nonce, 42, 77, now, 10);
        Check(restarted.ReadPanel(nonce, now).Verified, "old nonce sequence accepted");
        string nextNonce = "fedcba9876543210fedcba9876543210";
        WritePanel(root, nextNonce, 42, 77, now, 1);
        Check(restarted.ReadPanel(nextNonce, now).Verified, "new nonce resets sequence");

        var processRestart = PinkyHostConnection.CreateForTesting(root, exe,
            (pid, birth, expected) => (pid == 42 && birth == 77) || (pid == 43 && birth == 88), () => clock);
        WritePanel(root, nonce, 42, 77, now, 100);
        Check(processRestart.ReadPanel(nonce, now).Verified, "old process high sequence accepted");
        WritePanel(root, nonce, 43, 88, now, 1);
        Check(processRestart.ReadPanel(nonce, now).Verified, "restarted process low sequence accepted");

        var currentProcessOnly = PinkyHostConnection.CreateForTesting(root, exe,
            (pid, birth, expected) => pid == 43 && birth == 88, () => clock);
        WritePanel(root, nonce, 43, 88, now, 1);
        Check(currentProcessOnly.ReadPanel(nonce, now).Verified, "current process identity accepted");
        WritePanel(root, nonce, 42, 77, now, 101);
        Check(!currentProcessOnly.ReadPanel(nonce, now).Verified, "old process identity rejected");
        WritePanel(root, nonce, 43, 88, now, 2);
        Check(currentProcessOnly.ReadPanel(nonce, now).Verified, "rejected old identity leaves current sequence intact");

        var tail = PinkyHostConnection.CreateForTesting(root, exe, (pid, birth, expected) => true, () => clock);
        WritePanel(root, nonce, 42, 77, now, 20, new string('x', 161), 420);
        Check(!tail.ReadPanel(nonce, now).Verified, "bad tail field rejected");
        WritePanel(root, nonce, 42, 77, now, 20);
        Check(tail.ReadPanel(nonce, now).Verified, "bad tail does not poison sequence");
    }

    private static void WritePanel(string root, string nonce, int pid, long birth, DateTimeOffset updated, long sequence)
    {
        WritePanel(root, nonce, pid, birth, updated, sequence, null, 420);
    }

    private static void WritePanel(string root, string nonce, int pid, long birth, DateTimeOffset updated, long sequence, string reason, int width)
    {
        string stamp = updated.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        string reasonField = reason == null ? "" : ",\"reason\":\"" + reason + "\"";
        string json = "{\"schema\":\"pinky.sidebar-panel.v1\",\"nonce\":\"" + nonce + "\",\"pid\":" + pid + ",\"processBirthFileTime\":\"" + birth + "\",\"updatedUtc\":\"" + stamp + "\",\"phase\":\"requested\",\"desiredWidthDip\":" + width + ",\"desiredHeightDip\":560,\"sequence\":" + sequence + reasonField + "}";
        File.WriteAllText(Path.Combine(root, "panel.json"), json, new UTF8Encoding(false));
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
