using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using IslandIntegrated;

internal static class PinkyPopupAnchorTests
{
    private static int checks;

    private static int Main()
    {
        try
        {
            VerifyAnchor();
            VerifyConfiguration();
            Console.WriteLine("{\"ok\":true,\"checks\":" + checks + ",\"realPinky\":false,\"alphaSurfaceTested\":false}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            return 1;
        }
    }

    private static void VerifyAnchor()
    {
        using (var ordinary = new PinkyNativeHost(new Rectangle(-32000, -32000, 20, 20), false))
            Check((GetWindowLong(ordinary.Snapshot().Hwnd, -20) & 0x00080000) == 0, "Default constructor keeps ordinary host style");

        IntPtr hwnd;
        using (var anchor = new PinkyNativeHost(new Rectangle(-32000, -32000, 320, 156), false, true))
        {
            var initial = anchor.Snapshot();
            hwnd = initial.Hwnd;
            int style = GetWindowLong(hwnd, -20);
            Check((style & 0x00080000) != 0, "Popup anchor is layered");
            Check((style & 0x00000020) != 0, "Popup anchor passes input through");
            Check((style & 0x08000000) != 0, "Popup anchor cannot activate");
            uint colorKey, flags;
            byte alpha;
            Check(GetLayeredWindowAttributes(hwnd, out colorKey, out alpha, out flags), "Layered attributes can be queried");
            Check(alpha == 0 && (flags & 2) != 0, "Popup anchor has zero constant alpha");
            Check(initial.IsPerMonitorV2 && initial.ProcessId == Process.GetCurrentProcess().Id, "Anchor has local PMv2 identity");
            Check(!initial.Visible && initial.ClientRectPhysical.Size == new Size(320, 156), "Initial anchor is hidden at requested size");
            VerifyOwnedWindowIdentity(hwnd);
            Rectangle moved = new Rectangle(-31000, -31000, 320, 240);
            anchor.SetPlacement(moved, true);
            var shown = anchor.Snapshot();
            Check(shown.Visible && shown.ClientRectPhysical == moved, "Visible anchor reports actual requested bounds");
            Check(GetForegroundWindow() != hwnd, "Showing anchor does not activate it");
            anchor.SetPlacement(moved, false);
            Check(!anchor.Snapshot().Visible, "Anchor hides normally");
        }
        Check(!IsWindow(hwnd), "Disposing anchor destroys its HWND");
    }

    private static void VerifyOwnedWindowIdentity(IntPtr anchor)
    {
        IntPtr popup = CreateWindowEx(0x80, "STATIC", "Pinky popup ownership fixture", 0,
            -32000, -32000, 360, 80, anchor, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(popup != IntPtr.Zero, "Hidden top-level ownership fixture can be created");
        try
        {
            Check((GetWindowLong(popup, -16) & unchecked((int)0xC0000000)) == 0,
                "Owned top-level window can have neither WS_CHILD nor WS_POPUP");
            Check(GetWindow(popup, 4) == anchor, "GW_OWNER identifies the actual owner");
            Check(GetParent(popup) == IntPtr.Zero,
                "GetParent is not an owner check for an ordinary top-level window");
        }
        finally
        {
            DestroyWindow(popup);
        }
    }

    private static void VerifyConfiguration()
    {
        string privateRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost"));
        string scope = Path.Combine(privateRoot, "popup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scope);
        string file = Path.Combine(scope, "connection.json");
        var data = new Dictionary<string, object>();
        data["schema"] = "pinky.sidebar-connection.v1";
        data["enabled"] = true;
        data["directory"] = scope;
        data["pinkyExecutable"] = Assembly.GetExecutingAssembly().Location;
        try
        {
            Write(file, data);
            var ordinary = PinkyHostConnection.FromConfiguration(file);
            Check(ordinary.Enabled && !ordinary.OwnedPopupPresentation, "Existing configuration preserves ordinary presentation");
            data["presentation"] = "owned-popup";
            Write(file, data);
            var popup = PinkyHostConnection.FromConfiguration(file);
            Check(popup.Enabled && popup.OwnedPopupPresentation, "Explicit owned-popup enables transparent anchor");
            foreach (object invalid in new object[] { "", "other", true, null })
            {
                data["presentation"] = invalid;
                Write(file, data);
                Check(!PinkyHostConnection.FromConfiguration(file).Enabled, "Invalid presentation is rejected");
            }
            data["presentation"] = "owned-popup";
            data["unexpected"] = "value";
            Write(file, data);
            Check(!PinkyHostConnection.FromConfiguration(file).Enabled, "Unknown configuration fields remain rejected");
        }
        finally
        {
            string resolved = Path.GetFullPath(scope);
            if (!resolved.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture cleanup escaped its private root.");
            Directory.Delete(resolved, true);
        }
    }

    private static void Write(string file, Dictionary<string, object> data)
    {
        File.WriteAllText(file, new JavaScriptSerializer().Serialize(data));
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        checks++;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);
}
