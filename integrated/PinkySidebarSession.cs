using System;
using System.Diagnostics;
using System.Drawing;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IslandIntegrated
{
    public sealed class PinkySidebarSession : IDisposable
    {
        private readonly PinkyHostConnection connection;
        private readonly object ioGate = new object();
        private readonly string nonce;
        private PinkyNativeHost host;
        private PinkyPanelLease lastPanel = new PinkyPanelLease();
        private Rectangle lastBounds = new Rectangle(0, 0, 1, 1);
        private long sequence;
        private volatile bool disposed;
        private volatile bool failed;

        public PinkySidebarSession(string configurationFile)
        {
            connection = PinkyHostConnection.FromConfiguration(configurationFile);
            nonce = String.Empty;
            if (!connection.Enabled)
                return;

            try
            {
                nonce = NewNonce();
                host = new PinkyNativeHost(new Rectangle(0, 0, 1, 1), false, connection.OwnedPopupPresentation);
            }
            catch
            {
                failed = true;
            }
        }

        public bool Enabled
        {
            get { return connection.Enabled && !connection.AccessDenied && host != null && !failed && !disposed; }
        }

        public bool OwnedPopupPresentation
        {
            get { return connection.OwnedPopupPresentation; }
        }

        public Task<PinkyPanelLease> UpdateAsync(Rectangle physicalBounds, bool visible)
        {
            return UpdateAsync(physicalBounds, visible, IntPtr.Zero);
        }

        public Task<PinkyPanelLease> UpdateAsync(Rectangle physicalBounds, bool visible, IntPtr owner)
        {
            return UpdateAsync(physicalBounds, visible, owner, null);
        }

        public Task<PinkyPanelLease> UpdateAsync(Rectangle physicalBounds, bool visible, IntPtr owner, string theme)
        {
            return Task.Run(delegate
            {
                lock (ioGate)
                {
                    if (!Enabled)
                    {
                        if (connection.AccessDenied)
                            StopHostAfterFailure();
                        return new PinkyPanelLease();
                    }

                    try
                    {
                        if (theme != null)
                            connection.PublishAppearance(theme);
                        var activeHost = host;
                        lastBounds = physicalBounds;
                        activeHost.SetOwner(owner);
                        activeHost.SetPlacement(physicalBounds, visible);
                        var snapshot = activeHost.Snapshot();
                        if (!snapshot.IsPerMonitorV2)
                            throw new InvalidOperationException("The native host DPI context changed.");

                        connection.PublishHost(CreateLease(snapshot, visible && !disposed));
                        if (disposed || connection.AccessDenied)
                        {
                            StopHostAfterFailure();
                            return new PinkyPanelLease();
                        }

                        lastPanel = connection.ReadPanel(nonce, DateTimeOffset.UtcNow);
                        if (connection.AccessDenied)
                        {
                            StopHostAfterFailure();
                            return new PinkyPanelLease();
                        }
                        return lastPanel;
                    }
                    catch
                    {
                        StopHostAfterFailure();
                        return new PinkyPanelLease();
                    }
                }
            });
        }

        public static Rectangle PhysicalClientBounds(Control control, Rectangle rectangle)
        {
            var first = control.PointToScreen(rectangle.Location);
            var last = control.PointToScreen(new Point(rectangle.Right, rectangle.Bottom));
            if (!LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref first) ||
                !LogicalToPhysicalPointForPerMonitorDPI(control.Handle, ref last))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not map the native host slot.");
            return Rectangle.FromLTRB(first.X, first.Y, last.X, last.Y);
        }

        public void Dispose()
        {
            disposed = true;
            if (host == null)
                return;

            bool entered = false;
            try
            {
                entered = Monitor.TryEnter(ioGate, 6000);
                if (entered && !connection.AccessDenied)
                {
                    connection.ClearHost(nonce);
                    WaitForDetach();
                }
                ReleaseHost();
            }
            finally
            {
                if (entered)
                    Monitor.Exit(ioGate);
            }
        }

        private void WaitForDetach()
        {
            if (connection.AccessDenied || !lastPanel.Verified || lastPanel.Phase != "attached")
                return;

            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 1500)
            {
                var current = connection.ReadPanel(nonce, DateTimeOffset.UtcNow);
                if (current.Verified && current.Phase != "attached")
                    return;
                Thread.Sleep(25);
            }
        }

        private void StopHostAfterFailure()
        {
            failed = true;
            try
            {
                if (!connection.AccessDenied)
                {
                    connection.ClearHost(nonce);
                    WaitForDetach();
                }
            }
            catch { }
            try
            {
                ReleaseHost();
            }
            catch { }
        }

        private void ReleaseHost()
        {
            var releasingHost = Interlocked.Exchange(ref host, null);
            if (releasingHost == null)
                return;
            try
            {
                releasingHost.SetPlacement(lastBounds, false);
            }
            catch { }
            releasingHost.Dispose();
        }

        private PinkyHostLease CreateLease(PinkyNativeHost.PinkyNativeHostSnapshot snapshot, bool visible)
        {
            return new PinkyHostLease
            {
                Nonce = nonce,
                Sequence = ++sequence,
                ProcessId = snapshot.ProcessId,
                ProcessBirthFileTime = snapshot.ProcessBirthFileTime,
                Hwnd = snapshot.Hwnd,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Visible = visible && snapshot.Visible,
                ClientRectPhysical = snapshot.ClientRectPhysical,
                Dpi = checked((int)snapshot.Dpi)
            };
        }

        private static string NewNonce()
        {
            var bytes = new byte[16];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref Point point);
    }
}
