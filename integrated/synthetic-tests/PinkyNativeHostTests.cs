using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using IslandIntegrated;

internal static class PinkyNativeHostTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwHide = 0;
    private const int GwOwner = 4;
    private static readonly IntPtr HwndNotTopMost = new IntPtr(-2);
    private static readonly IntPtr HwndTopMost = new IntPtr(-1);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static int Main()
    {
        PinkyNativeHost host = null;
        WindowMessageCounter messages = null;
        OwnerProbeWindow ownerForm = null;
        IntPtr createdHwnd = IntPtr.Zero;
        IntPtr foregroundBefore = GetForegroundWindow();
        try
        {
            ownerForm = new OwnerProbeWindow();
            Require(ownerForm.Visible && ownerForm.TopMost, "offscreen topmost backing form is visible");
            Require(GetForegroundWindow() != ownerForm.Handle,
                "owner probe did not become foreground (baseline was " + foregroundBefore + ")");

            host = new PinkyNativeHost(new Rectangle(-32000, -32000, 320, 240), false);
            Console.Error.WriteLine("checkpoint: host constructed");
            PinkyNativeHost.PinkyNativeHostSnapshot initial = host.Snapshot();
            createdHwnd = initial.Hwnd;
            DumpWindowStyles("initial-hidden", createdHwnd, ownerForm.Handle);
            Require(createdHwnd != IntPtr.Zero, "created HWND");
            Require(initial.ProcessId == Process.GetCurrentProcess().Id, "host PID");
            Require(initial.ProcessBirthFileTime > 0, "process birth FILETIME");
            Require(!initial.Visible, "initial hidden state");
            Require(initial.ClientRectPhysical.Width == 320 && initial.ClientRectPhysical.Height == 240,
                "initial physical client size");
            Require(initial.Dpi > 0, "window DPI");
            Require(initial.IsPerMonitorV2, "actual HWND PMv2 context");
            Require(!initial.TopMost, "initial hidden host is not topmost");
            Require(GetForegroundWindow() == foregroundBefore && GetForegroundWindow() != initial.Hwnd,
                "hidden host did not take foreground");

            host.SetOwner(ownerForm.Handle);
            Console.Error.WriteLine("checkpoint: owner queued");
            host.Snapshot();
            Console.Error.WriteLine("checkpoint: owner snapshot returned");
            DumpWindowStyles("after-owner", createdHwnd, ownerForm.Handle);
            Require(GetWindow(createdHwnd, GwOwner) == ownerForm.Handle, "same-process popup owner assigned");
            host.SetOwner(ownerForm.Handle);
            host.Snapshot();
            Require(GetWindow(createdHwnd, GwOwner) == ownerForm.Handle, "unchanged owner remains assigned");

            messages = new WindowMessageCounter(createdHwnd);
            Rectangle target = new Rectangle(-31000, -30900, 480, 300);
            host.SetPlacement(target, true);
            PinkyNativeHost.PinkyNativeHostSnapshot moved = host.Snapshot();
            Require(moved.Hwnd == createdHwnd, "stable host HWND");
            Require(moved.Visible, "visible state");
            Require(moved.ClientRectPhysical.X == -31000 && moved.ClientRectPhysical.Y == -30900,
                "negative physical screen position");
            Require(moved.ClientRectPhysical.Width == 480 && moved.ClientRectPhysical.Height == 300,
                "resized physical client size");
            Require(moved.IsPerMonitorV2, "PMv2 context after placement");
            DiagnoseTopMost("owned", createdHwnd, ownerForm.Handle);
            host.SetOwner(IntPtr.Zero);
            host.Snapshot();
            DiagnoseTopMost("owner-cleared", createdHwnd, ownerForm.Handle);
            host.SetOwner(ownerForm.Handle);
            host.Snapshot();
            Require(moved.TopMost, "visible host is topmost");
            Require(WindowIsAbove(createdHwnd, ownerForm.Handle), "owned host is above its backing form");
            Require(GetForegroundWindow() == foregroundBefore && GetForegroundWindow() != moved.Hwnd,
                "programmatic show did not take foreground");

            MessageCounts beforeOwnerRaise = messages.Read();
            for (int i = 0; i < 5; i++)
            {
                Require(SetWindowPos(ownerForm.Handle, HwndTopMost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate), "owner topmost reassertion");
                Application.DoEvents();
                Require(WindowIsAbove(createdHwnd, ownerForm.Handle), "owned host stays above a topmost owner reassertion");
            }
            Require(messages.Read().Equals(beforeOwnerRaise), "owner z-order reassertions do not move or resize the host");

            MessageCounts afterShow = messages.Read();
            Require(afterShow.WindowPosChanging > 0 && afterShow.WindowPosChanged > 0,
                "initial placement reports window-position messages");
            Require(afterShow.Size > 0 && afterShow.ShowWindow > 0,
                "initial resize/show reports transition messages");

            for (int i = 0; i < 8; i++)
            {
                host.SetPlacement(target, true);
                Require(host.Snapshot().Visible, "identical visible placement remains visible");
            }
            Require(messages.Read().Equals(afterShow), "identical visible placement emits no position, size or show messages");

            MessageCounts beforeMove = messages.Read();
            Rectangle movedOnly = new Rectangle(-30900, -30800, 480, 300);
            host.SetPlacement(movedOnly, true);
            PinkyNativeHost.PinkyNativeHostSnapshot afterMove = host.Snapshot();
            MessageCounts movedMessages = messages.Read();
            Require(afterMove.ClientRectPhysical.X == movedOnly.X && afterMove.ClientRectPhysical.Y == movedOnly.Y,
                "external target move is applied");
            Require(movedMessages.WindowPosChanged > beforeMove.WindowPosChanged && movedMessages.Size == beforeMove.Size,
                "position-only transition reports position without resize");

            MessageCounts beforeResize = messages.Read();
            Rectangle resized = new Rectangle(movedOnly.X, movedOnly.Y, 500, 320);
            host.SetPlacement(resized, true);
            PinkyNativeHost.PinkyNativeHostSnapshot afterResize = host.Snapshot();
            MessageCounts resizeMessages = messages.Read();
            Require(afterResize.ClientRectPhysical.Width == resized.Width && afterResize.ClientRectPhysical.Height == resized.Height,
                "size-only target resize is applied");
            Require(resizeMessages.WindowPosChanged > beforeResize.WindowPosChanged && resizeMessages.Size > beforeResize.Size,
                "size transition reports position and size messages");

            MessageCounts beforeStableResize = messages.Read();
            for (int i = 0; i < 5; i++)
            {
                host.SetPlacement(resized, true);
                host.Snapshot();
            }
            Require(messages.Read().Equals(beforeStableResize), "identical resized placement emits no window messages");

            // Simulate an external move, then verify the next placement corrects actual HWND bounds.
            Require(SetWindowPos(createdHwnd, IntPtr.Zero, resized.X + 100, resized.Y + 80,
                resized.Width, resized.Height, SwpNoSize | SwpNoZOrder | SwpNoActivate), "external move setup");
            host.Snapshot();
            messages.Reset();
            host.SetPlacement(resized, true);
            PinkyNativeHost.PinkyNativeHostSnapshot afterExternalMove = host.Snapshot();
            Require(afterExternalMove.ClientRectPhysical.X == resized.X && afterExternalMove.ClientRectPhysical.Y == resized.Y,
                "external move is repaired");
            Require(messages.Read().WindowPosChanged > 0, "external move repair emits a position change");

            // Simulate external hiding and verify the next visible lease restores the HWND.
            ShowWindow(createdHwnd, SwHide);
            host.Snapshot();
            messages.Reset();
            host.SetPlacement(resized, true);
            PinkyNativeHost.PinkyNativeHostSnapshot afterExternalHide = host.Snapshot();
            Require(afterExternalHide.Visible, "external hide is repaired");
            Require(messages.Read().ShowWindow > 0, "external hide repair emits a visibility transition");

            // Topmost style loss is repaired without reapplying unchanged geometry.
            Require(SetWindowPos(createdHwnd, HwndNotTopMost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate), "external topmost perturbation setup");
            host.Snapshot();
            messages.Reset();
            host.SetPlacement(resized, true);
            PinkyNativeHost.PinkyNativeHostSnapshot afterTopmostRepair = host.Snapshot();
            Require(afterTopmostRepair.TopMost, "external topmost loss is repaired");
            Require(messages.Read().WindowPosChanged > 0, "topmost repair emits a z-order position change");

            messages.Reset();
            host.SetPlacement(resized, false);
            PinkyNativeHost.PinkyNativeHostSnapshot hidden = host.Snapshot();
            Require(!hidden.Visible, "hidden state");
            MessageCounts afterHide = messages.Read();
            Require(afterHide.ShowWindow > 0, "visible-to-hidden transition emits a show-window message");
            for (int i = 0; i < 5; i++)
            {
                host.SetPlacement(resized, false);
                Require(!host.Snapshot().Visible, "identical hidden placement remains hidden");
            }
            Require(messages.Read().Equals(afterHide), "identical hidden placement emits no position, size or show messages");
            Require(GetForegroundWindow() == foregroundBefore && GetForegroundWindow() != hidden.Hwnd,
                "programmatic hide changed the foreground");

            host.SetOwner(IntPtr.Zero);
            host.Snapshot();
            Require(GetWindow(createdHwnd, GwOwner) == IntPtr.Zero, "zero owner clears popup ownership");

            messages.Dispose();
            messages = null;
            host.Dispose();
            host.Dispose();
            host = null;
            Require(!IsWindow(createdHwnd), "host HWND destroyed on close");
            TestRejectedOwners(ownerForm);
            CheckTime();
            Console.WriteLine("{\"ok\":true,\"checks\":\"create,identity,physical-bounds,PMv2,owner,owner-clear,owner-topmost-order,move,resize,visible,topmost,hide,non-activating-show,foreground-retained,idempotent-visible,idempotent-hidden,external-move-repair,external-hide-repair,external-topmost-repair,invalid-self-owner,invalid-child-owner,invalid-foreign-owner,close\",\"elapsedMs\":" + Clock.ElapsedMilliseconds + "}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("{\"ok\":false,\"error\":" + Quote(ex.ToString()) + ",\"elapsedMs\":" + Clock.ElapsedMilliseconds + "}");
            return 1;
        }
        finally
        {
            if (messages != null)
            {
                try { messages.Dispose(); }
                catch (Exception ex) { Console.Error.WriteLine("message-counter cleanup: " + ex.Message); }
            }
            if (host != null)
            {
                try { host.Dispose(); }
                catch (Exception ex) { Console.Error.WriteLine("cleanup: " + ex.Message); }
            }
            if (ownerForm != null)
            {
                try { ownerForm.Dispose(); }
                catch (Exception ex) { Console.Error.WriteLine("owner form cleanup: " + ex.Message); }
            }
        }
    }

    private static void Require(bool condition, string check)
    {
        CheckTime();
        if (!condition)
            throw new InvalidOperationException("Failed check: " + check);
    }

    private static bool WindowIsAbove(IntPtr candidate, IntPtr reference)
    {
        IntPtr current = GetTopWindow(IntPtr.Zero);
        while (current != IntPtr.Zero)
        {
            if (current == candidate) return true;
            if (current == reference) return false;
            current = GetWindow(current, 2); // GW_HWNDNEXT
        }
        return false;
    }

    private static void TestRejectedOwners(OwnerProbeWindow owner)
    {
        using (PinkyNativeHost isolated = new PinkyNativeHost(new Rectangle(-32000, -32000, 160, 120), false))
        using (Control child = new ChildProbeControl())
        {
            IntPtr hwnd = isolated.Snapshot().Hwnd;
            child.CreateControl();
            SetParent(child.Handle, owner.Handle);
            ExpectOwnerRejected(isolated, hwnd, hwnd, "self owner rejected");
            ExpectOwnerRejected(isolated, hwnd, child.Handle, "child owner rejected");

            IntPtr shell = GetShellWindow();
            uint shellPid = 0;
            if (shell != IntPtr.Zero) GetWindowThreadProcessId(shell, out shellPid);
            if (shell != IntPtr.Zero && shellPid != Process.GetCurrentProcess().Id)
                ExpectOwnerRejected(isolated, hwnd, shell, "foreign owner rejected using read-only shell HWND");
            else
                Console.WriteLine("foreign-owner check skipped: no foreign shell HWND available");
        }
    }

    private static void DiagnoseTopMost(string phase, IntPtr hwnd, IntPtr owner)
    {
        const int GwlExStyle = -20;
        uint beforeLegacy = unchecked((uint)GetWindowLongW(hwnd, GwlExStyle));
        ulong beforePtr = unchecked((ulong)GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64());
        uint ownerLegacy = unchecked((uint)GetWindowLongW(owner, GwlExStyle));
        ulong ownerPtr = unchecked((ulong)GetWindowLongPtrW(owner, GwlExStyle).ToInt64());
        SetLastErrorNative(0);
        bool ok = SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, 0x13);
        int error = Marshal.GetLastWin32Error();
        uint afterLegacy = unchecked((uint)GetWindowLongW(hwnd, GwlExStyle));
        ulong afterPtr = unchecked((ulong)GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64());
        Console.WriteLine("diag " + phase + " hwnd=" + hwnd + " owner=" + owner +
            " actualOwner=" + GetWindow(hwnd, GwOwner) +
            " exstyleLegacy=0x" + beforeLegacy.ToString("X8") + " exstylePtr=0x" + beforePtr.ToString("X16") +
            " ownerExstyleLegacy=0x" + ownerLegacy.ToString("X8") + " ownerExstylePtr=0x" + ownerPtr.ToString("X16") +
            " setPos=0x13 result=" + ok + " error=" + error +
            " afterLegacy=0x" + afterLegacy.ToString("X8") + " afterPtr=0x" + afterPtr.ToString("X16"));
        bool ownerSet = SetWindowPos(owner, HwndTopMost, 0, 0, 0, 0, 0x13);
        uint ownerAfter = unchecked((uint)GetWindowLongW(owner, GwlExStyle));
        Console.WriteLine("diag " + phase + " ownerSetPos=0x13 result=" + ownerSet +
            " error=" + Marshal.GetLastWin32Error() + " ownerAfterLegacy=0x" + ownerAfter.ToString("X8"));
    }

    private static void DumpWindowStyles(string phase, IntPtr hwnd, IntPtr owner)
    {
        Console.WriteLine("diag " + phase + " hwnd=0x" + hwnd.ToInt64().ToString("X") +
            " owner=0x" + owner.ToInt64().ToString("X") + " actualOwner=0x" + GetWindow(hwnd, GwOwner).ToInt64().ToString("X") +
            " hostLegacy=0x" + unchecked((uint)GetWindowLongW(hwnd, -20)).ToString("X8") +
            " hostPtr=0x" + unchecked((ulong)GetWindowLongPtrW(hwnd, -20).ToInt64()).ToString("X16") +
            " ownerLegacy=0x" + unchecked((uint)GetWindowLongW(owner, -20)).ToString("X8") +
            " ownerPtr=0x" + unchecked((ulong)GetWindowLongPtrW(owner, -20).ToInt64()).ToString("X16"));
    }

    private static void ExpectOwnerRejected(PinkyNativeHost host, IntPtr hwnd, IntPtr candidate, string label)
    {
        bool rejected = false;
        try { host.SetOwner(candidate); host.Snapshot(); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, label);
        Require(GetWindow(hwnd, GwOwner) == IntPtr.Zero, label + " leaves owner unchanged");
    }

    private sealed class OwnerProbeWindow : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private OwnerProbeForm form;
        public IntPtr Handle { get; private set; }
        public bool Visible { get; private set; }
        public bool TopMost { get; private set; }

        public OwnerProbeWindow()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.WaitOne(3000)) throw new TimeoutException("Owner probe form did not start.");
        }

        private void Run()
        {
            form = new OwnerProbeForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(-32200, -32200, 1800, 1200);
            form.TopMost = true;
            form.ShowInTaskbar = false;
            form.Show();
            Handle = form.Handle;
            Visible = form.Visible;
            TopMost = form.TopMost;
            ready.Set();
            Application.Run();
            form.Dispose();
        }

        public void Dispose()
        {
            if (form != null && form.IsHandleCreated)
                form.BeginInvoke((MethodInvoker)delegate { form.Close(); Application.ExitThread(); });
            thread.Join(3000);
            ready.Dispose();
        }
    }

    private sealed class OwnerProbeForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } // WS_EX_NOACTIVATE
        }
    }

    private sealed class ChildProbeControl : Control
    {
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.Style |= unchecked((int)0x40000000); return cp; } // WS_CHILD
        }
    }

    private static void CheckTime()
    {
        if (Clock.ElapsedMilliseconds > 15000)
            throw new TimeoutException("The isolated host test exceeded 15 seconds.");
    }

    private sealed class WindowMessageCounter : IDisposable
    {
        private const int GwlpWndProc = -4;
        private const uint WmSize = 0x0005;
        private const uint WmShowWindow = 0x0018;
        private const uint WmWindowPosChanging = 0x0046;
        private const uint WmWindowPosChanged = 0x0047;
        private readonly IntPtr hwnd;
        private readonly WindowProc callback;
        private readonly IntPtr original;
        private bool disposed;
        private int positionChanging, positionChanged, size, showWindow;

        public WindowMessageCounter(IntPtr hwnd)
        {
            this.hwnd = hwnd;
            callback = Observe;
            original = SetLong(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(callback));
            if (original == IntPtr.Zero)
                throw new InvalidOperationException("Could not subclass the isolated host HWND for message counting.");
        }

        private IntPtr Observe(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmWindowPosChanging) Interlocked.Increment(ref positionChanging);
            else if (message == WmWindowPosChanged) Interlocked.Increment(ref positionChanged);
            else if (message == WmSize) Interlocked.Increment(ref size);
            else if (message == WmShowWindow) Interlocked.Increment(ref showWindow);
            return CallWindowProc(original, window, message, wParam, lParam);
        }

        public MessageCounts Read()
        {
            return new MessageCounts(
                Interlocked.CompareExchange(ref positionChanging, 0, 0),
                Interlocked.CompareExchange(ref positionChanged, 0, 0),
                Interlocked.CompareExchange(ref size, 0, 0),
                Interlocked.CompareExchange(ref showWindow, 0, 0));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref positionChanging, 0);
            Interlocked.Exchange(ref positionChanged, 0);
            Interlocked.Exchange(ref size, 0);
            Interlocked.Exchange(ref showWindow, 0);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (IsWindow(hwnd)) SetLong(hwnd, GwlpWndProc, original);
            GC.KeepAlive(callback);
        }

        private static IntPtr SetLong(IntPtr hwnd, int index, IntPtr value)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
        }

        private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
    }

    private struct MessageCounts : IEquatable<MessageCounts>
    {
        public readonly int WindowPosChanging, WindowPosChanged, Size, ShowWindow;

        public MessageCounts(int changing, int changed, int size, int showWindow)
        {
            WindowPosChanging = changing;
            WindowPosChanged = changed;
            Size = size;
            ShowWindow = showWindow;
        }

        public bool Equals(MessageCounts other)
        {
            return WindowPosChanging == other.WindowPosChanging && WindowPosChanged == other.WindowPosChanged &&
                Size == other.Size && ShowWindow == other.ShowWindow;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
    private static extern void SetLastErrorNative(uint error);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
