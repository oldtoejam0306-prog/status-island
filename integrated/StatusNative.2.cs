using System;
using System.Runtime.InteropServices;
public class AgentWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  public static bool Activate(IntPtr hwnd) {
    if (hwnd == IntPtr.Zero) return false;
    ShowWindow(hwnd, IsIconic(hwnd) ? 9 : 5);
    IntPtr fg = GetForegroundWindow();
    uint fgPid;
    uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
    uint myThread = GetCurrentThreadId();
    bool attached = false;
    if (fgThread != 0 && fgThread != myThread) { attached = AttachThreadInput(myThread, fgThread, true); }
    bool ok = SetForegroundWindow(hwnd);
    if (attached) { AttachThreadInput(myThread, fgThread, false); }
    return ok;
  }
}