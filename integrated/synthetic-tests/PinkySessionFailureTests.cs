using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using IslandIntegrated;

internal static class PinkySessionFailureTests
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private const int TotalLimitMs = 18000;
    private const int ChildOperationTimeoutMs = 12000;
    private static int checks;
    private static string tempDirectory;
    private static string fixtureDirectory;
    private static readonly List<string> ownedTempDirectories = new List<string>();
    private static readonly List<string> ownedFixtureDirectories = new List<string>();
    private static PinkySidebarSession deniedSession;
    private static PinkySidebarSession concurrentSession;

    private static int Main()
    {
        try
        {
            TestAccessDeniedCleanup();
            TestConcurrentDispose();
            Console.WriteLine("{\"ok\":true,\"checks\":" + checks + ",\"elapsedMs\":" + Clock.ElapsedMilliseconds + ",\"visibleUi\":false,\"realPinky\":false,\"aclChanged\":false}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("{\"ok\":false,\"checks\":" + checks + ",\"elapsedMs\":" + Clock.ElapsedMilliseconds + ",\"error\":\"" + Escape(error.ToString()) + "\"}");
            return 1;
        }
        finally
        {
            Cleanup();
        }
    }

    private static void TestAccessDeniedCleanup()
    {
        string config = CreateConfiguration("access-denied");
        deniedSession = new PinkySidebarSession(config);
        Check(deniedSession.Enabled, "Fixture session is enabled before simulated denial");
        PinkyNativeHost nativeHost = GetField<PinkyNativeHost>(deniedSession, "host");
        IntPtr originalHwnd = nativeHost.Snapshot().Hwnd;
        Check(originalHwnd != IntPtr.Zero && IsWindow(originalHwnd), "Session owns its original hidden HWND");

        PinkyHostConnection connection = GetField<PinkyHostConnection>(deniedSession, "connection");
        FieldInfo denied = typeof(PinkyHostConnection).GetField("accessDenied", BindingFlags.Instance | BindingFlags.NonPublic);
        if (denied == null)
            throw new MissingFieldException(typeof(PinkyHostConnection).FullName, "accessDenied");
        denied.SetValue(connection, true);
        Check(!deniedSession.Enabled, "Simulated connection denial disables the session");

        PinkyPanelLease result = Wait(deniedSession.UpdateAsync(new Rectangle(-30000, -30000, 320, 180), false),
            "UpdateAsync access-denied cleanup");
        Check(result != null && !result.Verified, "Denied update returns an empty panel lease");
        Check(GetField<PinkyNativeHost>(deniedSession, "host") == null,
            "Disabled UpdateAsync entry releases the session host reference");
        Check(!IsWindow(originalHwnd), "Access-denied cleanup destroys the original HWND");
        deniedSession.Dispose();
        deniedSession.Dispose();
        deniedSession = null;
        Check(!IsWindow(originalHwnd), "Repeated access-denied session disposal remains safe");
    }

    private static void TestConcurrentDispose()
    {
        string config = CreateConfiguration("dispose-race");
        concurrentSession = new PinkySidebarSession(config);
        Check(concurrentSession.Enabled, "Concurrent fixture session is enabled");
        PinkyNativeHost nativeHost = GetField<PinkyNativeHost>(concurrentSession, "host");
        IntPtr originalHwnd = nativeHost.Snapshot().Hwnd;
        Check(originalHwnd != IntPtr.Zero && IsWindow(originalHwnd), "Concurrent fixture host HWND exists");

        var start = new ManualResetEvent(false);
        var workers = new Task<PinkyPanelLease>[6];
        Rectangle[] bounds =
        {
            new Rectangle(-30000, -30000, 320, 180),
            new Rectangle(-29980, -29990, 340, 190),
            new Rectangle(-29960, -29980, 360, 200),
            new Rectangle(-29940, -29970, 380, 210),
            new Rectangle(-29920, -29960, 400, 220),
            new Rectangle(-29900, -29950, 420, 230)
        };

        for (int i = 0; i < workers.Length; i++)
        {
            int index = i;
            workers[i] = Task.Run(delegate
            {
                if (!start.WaitOne(3000))
                    throw new TimeoutException("Concurrent worker start gate timed out.");
                return concurrentSession.UpdateAsync(bounds[index], false).GetAwaiter().GetResult();
            });
        }
        Task dispose = Task.Run(delegate
        {
            if (!start.WaitOne(3000))
                throw new TimeoutException("Dispose start gate timed out.");
            concurrentSession.Dispose();
        });
        start.Set();

        var all = new Task[workers.Length + 1];
        Array.Copy(workers, all, workers.Length);
        all[workers.Length] = dispose;
        if (!Task.WaitAll(all, ChildOperationTimeoutMs))
            throw new TimeoutException("Concurrent UpdateAsync/Dispose operations exceeded their bound.");
        foreach (Task<PinkyPanelLease> worker in workers)
            Check(worker.Status == TaskStatus.RanToCompletion, "Concurrent update completed without escaping an exception");
        Check(dispose.Status == TaskStatus.RanToCompletion, "Concurrent Dispose completed within its bound");
        Check(!concurrentSession.Enabled, "Disposed session cannot be re-enabled");
        Check(GetField<PinkyNativeHost>(concurrentSession, "host") == null,
            "Concurrent disposal leaves no session host reference");
        Check(!IsWindow(originalHwnd), "Concurrent disposal destroys the original host HWND");

        PinkyPanelLease afterDispose = Wait(concurrentSession.UpdateAsync(new Rectangle(-29800, -29900, 320, 180), true),
            "post-dispose UpdateAsync");
        Check(afterDispose != null && !afterDispose.Verified, "Post-dispose update is inert");
        Check(GetField<PinkyNativeHost>(concurrentSession, "host") == null && !IsWindow(originalHwnd),
            "Post-dispose update does not recreate or resurrect the host");
        concurrentSession.Dispose();
        concurrentSession = null;
        Check(!IsWindow(originalHwnd), "Repeated concurrent-session Dispose remains safe");
        start.Dispose();
    }

    private static string CreateConfiguration(string label)
    {
        string localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost");
        string id = "fixture-failure-" + label + "-" + Guid.NewGuid().ToString("N");
        fixtureDirectory = Path.Combine(localRoot, id);
        Directory.CreateDirectory(fixtureDirectory);
        ownedFixtureDirectories.Add(fixtureDirectory);

        tempDirectory = Path.Combine(Path.GetTempPath(), "pinky-session-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        ownedTempDirectories.Add(tempDirectory);
        string configuration = Path.Combine(tempDirectory, "pinky-host-connection.json");
        var data = new Dictionary<string, object>();
        data["schema"] = "pinky.sidebar-connection.v1";
        data["enabled"] = true;
        data["directory"] = fixtureDirectory;
        data["pinkyExecutable"] = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        File.WriteAllText(configuration, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
        return configuration;
    }

    private static PinkyPanelLease Wait(Task<PinkyPanelLease> task, string description)
    {
        CheckTotalTime();
        if (!task.Wait(7000))
            throw new TimeoutException(description + " timed out.");
        CheckTotalTime();
        return task.GetAwaiter().GetResult();
    }

    private static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance);
    }

    private static void Check(bool condition, string description)
    {
        CheckTotalTime();
        if (!condition)
            throw new InvalidOperationException("Failed check: " + description);
        checks++;
    }

    private static void CheckTotalTime()
    {
        if (Clock.ElapsedMilliseconds >= TotalLimitMs)
            throw new TimeoutException("Failure fixture exceeded its 18-second total limit.");
    }

    private static void Cleanup()
    {
        try { if (deniedSession != null) deniedSession.Dispose(); }
        catch { }
        try { if (concurrentSession != null) concurrentSession.Dispose(); }
        catch { }

        foreach (string candidate in ownedFixtureDirectories)
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost"));
                string fixture = Path.GetFullPath(candidate);
                if (fixture.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(fixture).StartsWith("fixture-failure-", StringComparison.Ordinal))
                {
                    if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
                }
            }
            catch { }
        }
        foreach (string candidate in ownedTempDirectories)
        {
            try
            {
                string temp = Path.GetFullPath(candidate);
                string root = Path.GetFullPath(Path.GetTempPath());
                if (temp.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(temp).StartsWith("pinky-session-failure-", StringComparison.Ordinal))
                {
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);
                }
            }
            catch { }
        }
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
}
