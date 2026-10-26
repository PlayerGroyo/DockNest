using System.Runtime.InteropServices;

namespace DockNest.Interop;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool Contains(Point point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(nint handle, int id);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint handle, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] internal static extern nint GetLastActivePopup(nint handle);

    internal static Rect GetWorkArea(nint handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
            throw new InvalidOperationException("无法获取显示器工作区域。");
        return info.Work;
    }
}
