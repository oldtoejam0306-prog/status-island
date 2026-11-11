using System;
using System.Drawing;
using System.Runtime.InteropServices;
public class GlassWin {
  [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [StructLayout(LayoutKind.Sequential, Pack=1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
  [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
  [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
  public static void MakeLayered(IntPtr hwnd) {
    int ex = GetWindowLong(hwnd, -20);
    SetWindowLong(hwnd, -20, ex | 0x80000);
  }
  [ThreadStatic] public static int LastPushError;
  public static bool Push(IntPtr hwnd, Bitmap bmp, Point loc) {
    return Push(hwnd,bmp,loc,255);
  }
  public static bool Push(IntPtr hwnd, Bitmap bmp, Point loc, byte opacity) {
    LastPushError=0;
    IntPtr screenDc = GetDC(IntPtr.Zero);
    IntPtr memDc=IntPtr.Zero,hBmp=IntPtr.Zero,old=IntPtr.Zero;
    try {
    if(screenDc==IntPtr.Zero){LastPushError=6;return false;}
    memDc = CreateCompatibleDC(screenDc);
    if(memDc==IntPtr.Zero){LastPushError=8;return false;}
    hBmp = bmp.GetHbitmap(Color.FromArgb(0));
    old = SelectObject(memDc, hBmp);
    if(old==IntPtr.Zero||old==new IntPtr(-1)){LastPushError=6;return false;}
    SIZE size = new SIZE(); size.cx = bmp.Width; size.cy = bmp.Height;
    POINT src = new POINT(); src.x = 0; src.y = 0;
    POINT dst = new POINT(); dst.x = loc.X; dst.y = loc.Y;
    BLENDFUNCTION bf = new BLENDFUNCTION(); bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = opacity; bf.AlphaFormat = 1;
    bool ok=UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, 2);
    if(!ok)LastPushError=Marshal.GetLastWin32Error();
    return ok;
    } finally {
      if(old!=IntPtr.Zero&&old!=new IntPtr(-1))SelectObject(memDc,old);
      if(hBmp!=IntPtr.Zero)DeleteObject(hBmp);
      if(memDc!=IntPtr.Zero)DeleteDC(memDc);
      if(screenDc!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screenDc);
    }
  }
}
