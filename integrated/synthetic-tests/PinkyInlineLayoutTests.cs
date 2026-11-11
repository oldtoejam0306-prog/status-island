using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using IslandIntegrated;
using IslandMedia;

internal static class PinkyInlineLayoutTests
{
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        string fixtureRoot = Path.Combine(Path.GetTempPath(), "pinky-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        string layoutFile = Path.Combine(fixtureRoot, "layout.xml");
        bool ownedPopup = args.Length == 1 && args[0] == "owned-popup";
        string privateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusIsland", "PinkyHost");
        string hostDirectory = null;
        App app = null;

        try
        {
            string pinkyConfig = Path.Combine(fixtureRoot, "pinky-host-connection.json");
            if (ownedPopup)
            {
                hostDirectory = Path.Combine(privateRoot, "inline-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(hostDirectory);
                var config = new System.Collections.Generic.Dictionary<string, object> {
                    { "schema", "pinky.sidebar-connection.v1" }, { "enabled", true },
                    { "directory", hostDirectory }, { "pinkyExecutable", Assembly.GetExecutingAssembly().Location },
                    { "presentation", "owned-popup" }
                };
                File.WriteAllText(pinkyConfig, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(config));
            }
            var fakeMedia = new NeteaseSource(
                () => DateTime.UtcNow,
                () => Task.FromResult(new Snapshot { SourceId = "synthetic", Title = "Fixture" }));
            app = new App(layoutFile, null, fakeMedia);
            StopTimer(app);
            app.PrepareInitialLayout(Screen.PrimaryScreen);

            Check(File.Exists(pinkyConfig) == ownedPopup, "Fixture configuration matches selected presentation");
            Check(GetField<PinkySidebarSession>(app, "pinkySession").OwnedPopupPresentation == ownedPopup,
                "The requested presentation is loaded");
            Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Launch starts without a pending Pinky slot");

            app.Collapse();
            ApplyLease(app, "requested", 210, 126);
            Check(GetProperty<bool>(app, "PinkyPanelRequested"), "Normal collapse without a pending Pinky request does not mute the next request");
            ApplyLease(app, "hidden", 210, 126);

            Control cards = GetField<Control>(app, "cards");
            Layout layout = GetField<Layout>(app, "layout");
            bool layoutExistedAfterConstruction = File.Exists(layoutFile);
            byte[] initialLayoutBytes = layoutExistedAfterConstruction ? File.ReadAllBytes(layoutFile) : null;
            string[] initialCardIds = layout.Cards.Select(card => card.Id).ToArray();
            Rectangle[] initialCardBounds = cards.Controls.Cast<Control>().Select(control => control.Bounds).ToArray();
            int initialCardsTop = cards.Top;
            int initialFormHeight = GetField<Form>(app, "form").Height;
            Check(initialCardsTop == 0, "No-config cards retain original top position");

            ApplyLease(app, "requested", 210, 126);
            Check(GetProperty<bool>(app, "PinkyPanelRequested"), "Verified requested lease is accepted");
            Size requestedSize = GetProperty<Size>(app, "PinkyPanelSize");
            Check(!requestedSize.IsEmpty, "Requested panel receives a host slot");
            int reservedHeight = GetProperty<int>(app, "PinkyReservedHeight");
            Check(reservedHeight > 0 && cards.Top == reservedHeight, "Requested slot is reserved above the unchanged card stack");
            Check(GetField<Form>(app, "form").Height == cards.Height + reservedHeight + GetProperty<int>(app, "FooterHeight") + 8,
                "Column extent includes the panel slot and footer");
            Check(cards.Controls.Cast<Control>().Select(control => control.Bounds).SequenceEqual(initialCardBounds),
                "Requested lease does not rearrange individual user cards");
            Check(layout.Cards.Select(card => card.Id).SequenceEqual(initialCardIds), "Requested lease leaves Layout.Cards order unchanged");
            Check(File.Exists(layoutFile) == layoutExistedAfterConstruction &&
                (!layoutExistedAfterConstruction || File.ReadAllBytes(layoutFile).SequenceEqual(initialLayoutBytes)),
                "Applying a lease does not change the existing user layout bytes");

            ApplyLease(app, "attached", 210, 126);
            Check(GetProperty<bool>(app, "PinkyPanelRequested") && !GetProperty<Size>(app, "PinkyPanelSize").IsEmpty,
                "Verified attached lease keeps the inline slot available");
            Check(cards.Controls.Cast<Control>().Select(control => control.Bounds).SequenceEqual(initialCardBounds),
                "Attached lease preserves individual card bounds");

            ApplyLease(app, "hidden", 210, 126);
            Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Verified hidden lease removes the inline slot");
            Check(cards.Top == 0 && GetProperty<int>(app, "PinkyReservedHeight") == 0,
                "Hidden lease restores the prior card-stack origin");
            Check(GetField<Form>(app, "form").Height == initialFormHeight, "Hidden lease restores the no-Pinky column height");

            if (ownedPopup)
            {
                int previousCapacity = GetField<int>(app, "columnCapacity");
                SetField(app, "columnCapacity", 420);
                ApplyLease(app, "requested", 1000, 1000);
                // Large requests use the existing screen and column limits.
                int bodyWidth = GetField<int>(app, "baseColumnWidth");
                Size oversized = GetProperty<Size>(app, "PinkyPanelSize");
                Check(!oversized.IsEmpty && oversized.Width <= Math.Min(CornerLayout.Width, bodyWidth),
                    "Wide popup requests are clipped to the physical card body width");
                ApplyLease(app, "requested", 360, 80);
                Check(GetProperty<Size>(app, "PinkyPanelSize").Height == CornerLayout.CompactHeight,
                    "Compact popup viewport keeps the status body's physical compact height");
                ApplyLease(app, "requested", 360, 1000);
                Size clipped = GetProperty<Size>(app, "PinkyPanelSize");
                Check(cards.Width > bodyWidth, "Long popup fixture exercises a card-stack scrollbar");
                Rectangle slot = GetProperty<Rectangle>(app, "PinkyPanelSlot");
                Check(slot.Left >= 0 && slot.Right == cards.Left + bodyWidth,
                    "Pinky panel slot shares the visible card body right edge outside the scrollbar");
                Check(clipped.Height >= CornerLayout.CompactHeight &&
                    clipped.Height + WidgetGrid.Gap + GetProperty<int>(app, "FooterHeight") + 8 < 420,
                    "Long details are clipped inside the column before its footer");
                Check(cards.Top == clipped.Height + WidgetGrid.Gap,
                    "Expanded popup reserves its full viewport inside the column");
                Check(cards.Height >= WidgetGrid.SmallWidth(WidgetGrid.DefaultColumnWidth),
                    "Expanded popup preserves one small-card height for the original card stack");
                Check(cards.Controls.Cast<Control>().Select(control => control.Bounds).SequenceEqual(initialCardBounds),
                    "Clipping popup details preserves individual card bounds");
                SetField(app, "columnCapacity", previousCapacity);
                ApplyLease(app, "hidden", 360, 80);
            }

            ApplyLease(app, "requested", 210, 126);
            Check(GetProperty<bool>(app, "PinkyPanelRequested"), "A new request after hidden is accepted");
            ApplyLease(app, null, 0, 0);
            Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Invalid or empty lease removes the slot");
            Check(cards.Top == 0, "Invalid lease restores the card stack");

            ApplyLease(app, "requested", 210, 126);
            SetField(app, "columnCapacity", 96);
            Invoke(app, "UpdateColumnExtent");
            Check(GetProperty<bool>(app, "PinkyPanelRequested"), "Short workspace request remains visible to the session");
            Check(GetProperty<Size>(app, "PinkyPanelSize").IsEmpty, "Short workspace refuses an undersized inline slot");
            Check(GetProperty<int>(app, "PinkyReservedHeight") == 0 && cards.Top == 0,
                "Short workspace keeps the full cards area because the Pinky host slot is unavailable and hidden");

            SetField(app, "columnCapacity", Screen.PrimaryScreen.WorkingArea.Height);
            Invoke(app, "UpdateColumnExtent");
            ApplyLease(app, "requested", 210, 126);
            app.Collapse();
            Check(!GetProperty<bool>(app, "PinkyPanelRequested"), "Explicit collapse dismisses the current request");
            Invoke(app, "Tick", false);
            Check(!GetProperty<bool>(app, "PinkyPanelRequested") && !GetProperty<bool>(app, "Expanded"),
                "The next controller tick does not immediately reopen after collapse");

            ApplyLease(app, "hidden", 210, 126);
            ApplyLease(app, "requested", 210, 126);
            Check(GetProperty<bool>(app, "PinkyPanelRequested"), "Hidden acknowledgement re-arms a later request");
            Check(layout.Cards.Select(card => card.Id).SequenceEqual(initialCardIds), "All lease transitions preserve Layout.Cards");
            Layout savedLayout = LayoutStore.Load(layoutFile);
            Check(savedLayout.Cards.Select(card => card.Id).SequenceEqual(initialCardIds),
                "Collapse persistence contains only the original user cards, with no Pinky entry");

            Console.WriteLine("{\"ok\":true,\"checks\":" + checks + ",\"ownedPopup\":" + ownedPopup.ToString().ToLowerInvariant() +
                ",\"visibleUi\":false,\"realPinky\":false,\"fixture\":\"isolated\"}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("{\"ok\":false,\"checks\":" + checks + ",\"error\":\"" + Escape(error.ToString()) + "\"}");
            return 1;
        }
        finally
        {
            if (app != null)
                app.Shutdown();
            if (hostDirectory != null)
            {
                string resolved = Path.GetFullPath(hostDirectory);
                if (!resolved.StartsWith(Path.GetFullPath(privateRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Fixture cleanup escaped the private host root.");
                Directory.Delete(resolved, true);
            }
        }
    }

    private static void ApplyLease(App app, string phase, double width, double height)
    {
        var lease = new PinkyPanelLease
        {
            Verified = phase != null,
            Phase = phase,
            DesiredWidthDip = width,
            DesiredHeightDip = height,
            PanelHwnd = phase == null ? IntPtr.Zero : new IntPtr(123),
            Sequence = checks + 1
        };
        Invoke(app, "ApplyPinkyPanel", lease);
    }

    private static void StopTimer(App app)
    {
        GetField<System.Windows.Forms.Timer>(app, "timer").Stop();
        object monitor = GetField<object>(app, "aihot");
        SetField(monitor, "nextRequestUtc", DateTime.MaxValue);
    }

    private static T GetField<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().FullName, name);
        return (T)field.GetValue(instance);
    }

    private static void SetField(object instance, string name, object value)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(instance.GetType().FullName, name);
        field.SetValue(instance, value);
    }

    private static T GetProperty<T>(object instance, string name)
    {
        PropertyInfo property = instance.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            throw new MissingMemberException(instance.GetType().FullName, name);
        return (T)property.GetValue(instance, null);
    }

    private static object Invoke(object instance, string name, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(instance.GetType().FullName, name);
        return method.Invoke(instance, arguments);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("Failed check: " + description);
        checks++;
    }

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
