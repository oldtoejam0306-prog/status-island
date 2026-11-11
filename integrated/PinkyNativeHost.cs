using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace IslandIntegrated
{
    /// <summary>
    /// A private top-level HWND owned by this module. Programmatic display does not
    /// activate it; user activation remains available to child UI. This class never
    /// discovers, reparents, resizes, or sends input to another process's windows.
    /// </summary>
    public sealed class PinkyNativeHost : IDisposable
    {
        private const int CreateTimeoutMs = 5000;
        private const int OperationTimeoutMs = 5000;
        private const int CloseTimeoutMs = 5000;
        private const int MaxDimension = 16384;
        private const uint WmAppDispatch = 0x8000 + 0x531;
        private const uint WmAppClose = 0x8000 + 0x532;
        private const uint WmDestroy = 0x0002;
        private const uint WmNcDestroy = 0x0082;
        private const uint WsPopup = 0x80000000;
        private const uint WsClipChildren = 0x02000000;
        private const uint WsExToolWindow = 0x00000080;
        private const uint WsExTopMost = 0x00000008;
        private const uint WsExTransparent = 0x00000020;
        private const uint WsExLayered = 0x00080000;
        private const uint WsExNoActivate = 0x08000000;
        private const uint LwaAlpha = 0x00000002;
        private const int GwlExStyle = -20;
        private const int GwlStyle = -16;
        private const int GwlpHwndParent = -8;
        private const int GwOwner = 4;
        private const uint WsChild = 0x40000000;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const int SwHide = 0;
        private const int SwShowNoActivate = 4;
        private const uint PmNoRemove = 0;
        private const uint PmV2Value = 0xFFFFFFFC;
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const uint BlackBrush = 4;

        private static readonly IntPtr DpiContextPerMonitorV2 = new IntPtr(unchecked((int)PmV2Value));
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private static readonly WindowProc WindowProcedureDelegate = StaticWindowProcedure;
        private static readonly ConcurrentDictionary<IntPtr, PinkyNativeHost> Hosts =
            new ConcurrentDictionary<IntPtr, PinkyNativeHost>();

        private readonly object _gate = new object();
        private readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();
        private readonly ManualResetEvent _started = new ManualResetEvent(false);
        private readonly ManualResetEvent _threadStopped = new ManualResetEvent(false);
        private readonly Thread _thread;
        private readonly string _className;
        private readonly Rectangle _initialBounds;
        private readonly bool _initialVisible;
        private readonly bool _transparentAnchor;
        private volatile bool _cancelStart;
        private bool _disposed;
        private bool _resourcesDisposed;
        private IntPtr _hwnd;
        private uint _threadId;
        private IntPtr _previousDpiContext;
        private bool _hasPreviousDpiContext;
        private bool _classRegistered;
        private Exception _startupException;
        private volatile Exception _threadException;
        private long _processBirthFileTime;

        public PinkyNativeHost(Rectangle initialPhysicalBounds, bool visible)
            : this(initialPhysicalBounds, visible, false)
        {
        }

        public PinkyNativeHost(Rectangle initialPhysicalBounds, bool visible, bool transparentAnchor)
        {
            ValidateBounds(initialPhysicalBounds);
            _initialBounds = initialPhysicalBounds;
            _initialVisible = visible;
            _transparentAnchor = transparentAnchor;
            _className = "IslandIntegrated.PinkyNativeHost." + Guid.NewGuid().ToString("N");
            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;
            _thread.Name = "Pinky native host UI";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_started.WaitOne(CreateTimeoutMs))
            {
                _cancelStart = true;
                throw new TimeoutException("Timed out creating the native host within five seconds.");
            }
            if (_startupException != null)
                throw new InvalidOperationException("Could not create the native host.", _startupException);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("The native host thread stopped before creating its window.");
        }

        /// <summary>Queues a physical-pixel placement update on the host's own STA thread.</summary>
        public void SetPlacement(Rectangle physicalBounds, bool visible)
        {
            ValidateBounds(physicalBounds);
            lock (_gate)
            {
                ThrowIfUnavailable();
                _pending.Enqueue(delegate
                {
                    if (_hwnd == IntPtr.Zero)
                        return;
                    ApplyPlacement(physicalBounds);
                    ShowOrHide(visible);
                });
                PostDispatchLocked();
            }
        }

        /// <summary>Assigns or clears this top-level WS_POPUP window's owner on its STA thread.</summary>
        public void SetOwner(IntPtr owner)
        {
            lock (_gate)
            {
                ThrowIfUnavailable();
                _pending.Enqueue(delegate
                {
                    if (_hwnd == IntPtr.Zero)
                        return;
                    SetOwnerOnHostThread(owner);
                });
                PostDispatchLocked();
            }
        }

        /// <summary>Returns an identity and physical client-area snapshot from the host thread.</summary>
        public PinkyNativeHostSnapshot Snapshot()
        {
            SnapshotRequest request = new SnapshotRequest();
            lock (_gate)
            {
                ThrowIfUnavailable();
                _pending.Enqueue(delegate
                {
                    lock (request.Gate)
                    {
                        if (request.Abandoned)
                            return;
                        try
                        {
                            // ReadSnapshot checks prior UI-thread failures before reporting state.
                            request.Value = ReadSnapshot();
                        }
                        catch (Exception ex)
                        {
                            request.Error = ex;
                        }
                        request.Done.Set();
                    }
                });
                PostDispatchLocked();
            }

            bool signaled = request.Done.WaitOne(OperationTimeoutMs);
            lock (request.Gate)
            {
                if (!signaled)
                {
                    request.Abandoned = true;
                    request.Done.Dispose();
                    throw new TimeoutException("Timed out taking a native host snapshot within five seconds.");
                }
                request.Done.Dispose();
                if (request.Error != null)
                    throw new InvalidOperationException("Could not read the native host snapshot.", request.Error);
                return request.Value;
            }
        }

        /// <summary>
        /// Hides and closes only this module's HWND. The caller should wait for Pinky to
        /// detach its own child before normal shutdown; Pinky is responsible for recovery
        /// if this host disappears unexpectedly.
        /// </summary>
        public void Dispose()
        {
            bool shouldPostClose = false;
            lock (_gate)
            {
                if (_resourcesDisposed)
                    return;
                if (!_disposed)
                {
                    _disposed = true;
                    shouldPostClose = _hwnd != IntPtr.Zero && !_threadStopped.WaitOne(0);
                    if (shouldPostClose && !PostMessage(_hwnd, WmAppClose, IntPtr.Zero, IntPtr.Zero))
                        _threadException = new Win32Exception(Marshal.GetLastWin32Error(), "Could not post native host close.");
                }
            }

            if (!_threadStopped.WaitOne(CloseTimeoutMs))
                throw new TimeoutException("Timed out closing the native host within five seconds.");

            lock (_gate)
            {
                if (!_resourcesDisposed)
                {
                    _resourcesDisposed = true;
                    _started.Dispose();
                    _threadStopped.Dispose();
                }
            }
        }

        private void ThrowIfUnavailable()
        {
            if (_disposed)
                throw new ObjectDisposedException("PinkyNativeHost");
            if (_threadStopped.WaitOne(0))
                throw new InvalidOperationException("The native host UI thread has stopped.", _threadException);
            if (_threadException != null)
                throw new InvalidOperationException("A native host operation failed; the host is no longer usable.", _threadException);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("The native host window is unavailable.");
        }

        private void PostDispatchLocked()
        {
            if (!PostMessage(_hwnd, WmAppDispatch, IntPtr.Zero, IntPtr.Zero))
            {
                Action ignored;
                _pending.TryDequeue(out ignored);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not queue work to the native host thread.");
            }
        }

        private void ThreadMain()
        {
            _threadId = GetCurrentThreadId();
            Message ignoredMessage;
            PeekMessage(out ignoredMessage, IntPtr.Zero, 0, 0, PmNoRemove);
            try
            {
                _previousDpiContext = SetThreadDpiAwarenessContext(DpiContextPerMonitorV2);
                if (_previousDpiContext == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadDpiAwarenessContext(PMv2) failed.");
                _hasPreviousDpiContext = true;
                IntPtr effective = GetThreadDpiAwarenessContext();
                if (!AreDpiAwarenessContextsEqual(effective, DpiContextPerMonitorV2))
                    throw new InvalidOperationException("The host thread did not acquire a per-monitor-v2 DPI context.");

                if (_cancelStart)
                    throw new OperationCanceledException("Host creation was canceled after the startup timeout.");

                RegisterOwnClass();
                uint exStyle = WsExToolWindow;
                if (_transparentAnchor)
                    exStyle |= WsExLayered | WsExTransparent | WsExNoActivate;
                _hwnd = CreateWindowEx(exStyle, _className, "Pinky 确认区域",
                    WsPopup | WsClipChildren, _initialBounds.X, _initialBounds.Y,
                    _initialBounds.Width, _initialBounds.Height,
                    IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for the native host.");
                Hosts[_hwnd] = this;
                if (_transparentAnchor && !SetLayeredWindowAttributes(_hwnd, 0, 0, LwaAlpha))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make the popup anchor transparent.");

                _processBirthFileTime = GetCurrentProcessBirthFileTime();
                if (_cancelStart)
                    throw new OperationCanceledException("Host creation was canceled after the startup timeout.");

                if (_initialVisible)
                    ShowOrHide(true);
                _started.Set();

                Message message;
                while (true)
                {
                    int result = GetMessage(out message, IntPtr.Zero, 0, 0);
                    if (result == 0)
                        break;
                    if (result == -1)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMessage failed for the native host thread.");
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch (Exception ex)
            {
                if (!_started.WaitOne(0))
                    _startupException = ex;
                else
                    _threadException = ex;
            }
            finally
            {
                IntPtr hwnd = _hwnd;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SwHide);
                    DestroyWindow(hwnd);
                    _hwnd = IntPtr.Zero;
                }
                if (_classRegistered)
                {
                    UnregisterClass(_className, GetModuleHandle(null));
                    _classRegistered = false;
                }
                if (_hasPreviousDpiContext)
                {
                    SetThreadDpiAwarenessContext(_previousDpiContext);
                    _hasPreviousDpiContext = false;
                }
                if (!_started.WaitOne(0))
                    _started.Set();
                _threadStopped.Set();
            }
        }

        private void RegisterOwnClass()
        {
            WindowClassEx wc = new WindowClassEx();
            wc.Size = (uint)Marshal.SizeOf(typeof(WindowClassEx));
            wc.WindowProcedure = WindowProcedureDelegate;
            wc.Instance = GetModuleHandle(null);
            wc.BackgroundBrush = GetStockObject(BlackBrush);
            wc.ClassName = _className;
            if (RegisterClassEx(ref wc) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassEx failed for the native host.");
            _classRegistered = true;
        }

        private void ShowOrHide(bool visible)
        {
            if (visible)
            {
                if (!IsTopMost(_hwnd) && !SetWindowPos(_hwnd, HwndTopMost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make the native host topmost.");
                if (!IsWindowVisible(_hwnd))
                    ShowWindow(_hwnd, SwShowNoActivate);
            }
            else if (IsWindowVisible(_hwnd))
                ShowWindow(_hwnd, SwHide);
        }

        private void ApplyPlacement(Rectangle target)
        {
            if (!IsWindow(_hwnd))
                throw new InvalidOperationException("The native host window no longer exists.");

            Rect actual;
            if (!GetWindowRect(_hwnd, out actual))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the native host bounds.");

            bool move = actual.Left != target.Left || actual.Top != target.Top;
            bool resize = actual.Right - actual.Left != target.Width || actual.Bottom - actual.Top != target.Height;
            if (!move && !resize)
                return;

            uint flags = SwpNoZOrder | SwpNoActivate;
            if (!move)
                flags |= SwpNoMove;
            if (!resize)
                flags |= SwpNoSize;

            if (!SetWindowPos(_hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, flags))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed for the native host.");
        }

        private void SetOwnerOnHostThread(IntPtr owner)
        {
            if (owner == _hwnd)
                throw new ArgumentException("The native host cannot own itself.", "owner");

            if (owner != IntPtr.Zero)
            {
                if (!IsWindow(owner))
                    throw new ArgumentException("The owner HWND does not exist.", "owner");

                uint ownerProcessId;
                GetWindowThreadProcessId(owner, out ownerProcessId);
                if (ownerProcessId != GetCurrentProcessId())
                    throw new ArgumentException("The owner HWND must belong to this process.", "owner");

                if ((((uint)GetWindowLong(owner, GwlStyle)) & WsChild) != 0)
                    throw new ArgumentException("A child HWND cannot own the native host.", "owner");
            }

            if (GetWindow(_hwnd, GwOwner) == owner)
                return;

            SetLastErrorNative(0);
            IntPtr previousOwner = SetWindowLongPtr(_hwnd, GwlpHwndParent, owner);
            int error = Marshal.GetLastWin32Error();
            if (previousOwner == IntPtr.Zero && error != 0)
                throw new Win32Exception(error, "Could not change the native host owner.");
            if (GetWindow(_hwnd, GwOwner) != owner)
                throw new InvalidOperationException("The native host owner did not change as requested.");
        }

        private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hwnd, index, value);
            return new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
        }

        private static bool IsTopMost(IntPtr hwnd)
        {
            return (((uint)GetWindowLong(hwnd, GwlExStyle)) & WsExTopMost) != 0;
        }

        private PinkyNativeHostSnapshot ReadSnapshot()
        {
            if (_threadException != null)
                throw new InvalidOperationException("A native host operation failed.", _threadException);
            IntPtr hwnd = _hwnd;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                throw new InvalidOperationException("The native host window no longer exists.");

            Rect client;
            if (!GetClientRect(hwnd, out client))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetClientRect failed for the native host.");
            Point topLeft = new Point(client.Left, client.Top);
            Point bottomRight = new Point(client.Right, client.Bottom);
            if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ClientToScreen failed for the native host.");

            uint dpi = GetDpiForWindow(hwnd);
            IntPtr context = GetWindowDpiAwarenessContext(hwnd);
            int awareness = GetAwarenessFromDpiAwarenessContext(context);
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return new PinkyNativeHostSnapshot(hwnd, (int)pid, _processBirthFileTime,
                Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y), dpi,
                context, awareness, AreDpiAwarenessContextsEqual(context, DpiContextPerMonitorV2),
                IsWindowVisible(hwnd), (((uint)GetWindowLong(hwnd, GwlExStyle)) & WsExTopMost) != 0);
        }

        private void DispatchPending()
        {
            Action action;
            while (_pending.TryDequeue(out action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _threadException = ex;
                }
            }
        }

        private static IntPtr StaticWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            PinkyNativeHost host;
            if (Hosts.TryGetValue(hwnd, out host))
                return host.HandleWindowMessage(hwnd, message, wParam, lParam);
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        private IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmAppDispatch)
            {
                DispatchPending();
                return IntPtr.Zero;
            }
            if (message == WmAppClose || message == 0x0010)
            {
                ShowWindow(hwnd, SwHide);
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            }
            if (message == WmDestroy)
            {
                _hwnd = IntPtr.Zero;
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            if (message == WmNcDestroy)
            {
                PinkyNativeHost ignored;
                Hosts.TryRemove(hwnd, out ignored);
            }
            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        private static void ValidateBounds(Rectangle bounds)
        {
            if (bounds.Width < 1 || bounds.Height < 1 || bounds.Width > MaxDimension || bounds.Height > MaxDimension)
                throw new ArgumentOutOfRangeException("bounds", "Host dimensions must be between 1 and 16384 physical pixels.");
        }

        private static long GetCurrentProcessBirthFileTime()
        {
            FileTime creation, exit, kernel, user;
            if (!GetProcessTimes(GetCurrentProcess(), out creation, out exit, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessTimes failed for the host process.");
            return unchecked((long)(((ulong)creation.High << 32) | creation.Low));
        }

        private sealed class SnapshotRequest
        {
            internal readonly object Gate = new object();
            internal readonly ManualResetEvent Done = new ManualResetEvent(false);
            internal bool Abandoned;
            internal PinkyNativeHostSnapshot Value;
            internal Exception Error;
        }

        public sealed class PinkyNativeHostSnapshot
        {
            internal PinkyNativeHostSnapshot(IntPtr hwnd, int processId, long processBirthFileTime,
                Rectangle clientRectPhysical, uint dpi, IntPtr dpiAwarenessContext,
                int dpiAwareness, bool isPerMonitorV2, bool visible, bool topMost)
            {
                Hwnd = hwnd;
                ProcessId = processId;
                ProcessBirthFileTime = processBirthFileTime;
                ClientRectPhysical = clientRectPhysical;
                Dpi = dpi;
                DpiAwarenessContext = dpiAwarenessContext;
                DpiAwareness = dpiAwareness;
                IsPerMonitorV2 = isPerMonitorV2;
                Visible = visible;
                TopMost = topMost;
            }

            public IntPtr Hwnd { get; private set; }
            public int ProcessId { get; private set; }
            public long ProcessBirthFileTime { get; private set; }
            public Rectangle ClientRectPhysical { get; private set; }
            public uint Dpi { get; private set; }
            public IntPtr DpiAwarenessContext { get; private set; }
            public int DpiAwareness { get; private set; }
            public bool IsPerMonitorV2 { get; private set; }
            public bool Visible { get; private set; }
            public bool TopMost { get; private set; }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClassEx
        {
            public uint Size;
            public uint Style;
            public WindowProc WindowProcedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr BackgroundBrush;
            public string MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Hwnd;
            public uint Value;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterClass(string className, IntPtr instance);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int exitCode);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
        private static extern void SetLastErrorNative(uint errorCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(uint objectIndex);
    }
}
