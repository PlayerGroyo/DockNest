using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DockNest.Services;
using DockNest.Interop;
using DockNest;
using DockNest.Models;
using System.IO;

internal static class EdgeDockChecks
{
    internal static void Run(Action<bool, string> check, string fixtures)
    {
        var window = new Window
        {
            Style = null,
            Width = 400, Height = 300, Opacity = 0, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, AllowsTransparency = false
        };
        window.Show();
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
        var pointer = new NativeMethods.Point { X = info.Work.Right - 20, Y = info.Work.Bottom - 20 };
        using var docking = new EdgeDockController(window, source, () => pointer);
        try
        {
            // Place the invisible test window away from the real cursor, without moving user input.
            GetCursorPos(out var cursor);
            var x = cursor.X < (info.Work.Left + info.Work.Right) / 2
                ? info.Work.Right - 450 : info.Work.Left + 50;
            SendMessage(hwnd, 0x0231, 0, 0);
            SetWindowPos(hwnd, 0, x, info.Work.Top + 4, 400, 300, 0x0014);
            SendMessage(hwnd, 0x0232, 0, 0);
            check(docking.Edge == DockEdge.Top, "Top docking through native drag enter/exit messages");
            Pump(1800);
            check(docking.IsHidden && !window.IsVisible, "Top docking automatically hides after mouse leaves (real dispatcher timer)");
            var strip = Field<Window>(docking, "edgeHandle");
            CheckStrip(DockEdge.Top);
            check(strip.Style is null && strip.MinWidth == 0 && strip.MinHeight == 0,
                "Edge trigger does not inherit normal-window minimum dimensions");

            foreach (var edge in new[] { DockEdge.Right, DockEdge.Bottom, DockEdge.Left, DockEdge.Top })
            {
                docking.Undock();
                var left = edge == DockEdge.Left ? info.Work.Left + 4 : edge == DockEdge.Right ? info.Work.Right - 404 : info.Work.Left + 100;
                var top = edge == DockEdge.Top ? info.Work.Top + 4 : edge == DockEdge.Bottom ? info.Work.Bottom - 304 : info.Work.Top + 100;
                SetWindowPos(hwnd, 0, left, top, 400, 300, 0x0014);
                docking.SnapToEdge();
                check(docking.Edge == edge, $"Dock to {edge}");
                docking.Toggle();
                CheckStrip(edge);
                docking.Reveal(false);
                check(!strip.IsVisible && window.IsVisible, $"Revealing from {edge} removes the colored strip");
            }

            CheckHoverReveal(window, docking, hwnd, strip, info.Work,
                (x, y) => pointer = new NativeMethods.Point { X = x, Y = y }, check);

            void CheckStrip(DockEdge edge)
            {
                GetWindowRect(new WindowInteropHelper(strip).Handle, out var rect);
                GetWindowRect(hwnd, out var visible);
                var pixels = (int)Math.Ceiling(6 * GetDpiForWindow(hwnd) / 96.0);
                bool horizontal = edge is DockEdge.Top or DockEdge.Bottom;
                check((horizontal ? rect.Bottom - rect.Top : rect.Right - rect.Left) == pixels,
                    $"{edge} auto-hide exposes exactly 6 DIP ({pixels} physical pixels), not a solid window");
                check(horizontal ? rect.Left == visible.Left && rect.Right == visible.Right : rect.Top == visible.Top && rect.Bottom == visible.Bottom,
                    $"{edge} trigger spans the original window edge");
                check(rect.Left >= info.Work.Left && rect.Top >= info.Work.Top && rect.Right <= info.Work.Right && rect.Bottom <= info.Work.Bottom,
                    $"{edge} trigger stays inside the monitor work area");
                check(docking.IsHidden && !window.IsVisible && strip.IsVisible, $"Only the {edge} edge trigger remains visible");
            }
        }
        finally
        {
            docking.Dispose();
            window.Close();
        }

        CheckMainWindowHover(check, fixtures);
    }

    private static void CheckMainWindowHover(Action<bool, string> check, string fixtures)
    {
        var window = new MainWindow(new StateStore(Path.Combine(fixtures, "hover-profile")), new AppState(), false)
        {
            Opacity = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        window.Show();
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info);
        var pointer = new NativeMethods.Point { X = info.Work.Right - 20, Y = info.Work.Bottom - 20 };
        using var docking = new EdgeDockController(window, source, () => pointer);
        try
        {
            SetWindowPos(hwnd, 0, info.Work.Left + 100, info.Work.Top + 4, 0, 0, 0x0015);
            docking.SnapToEdge();
            docking.Toggle();
            var strip = Field<Window>(docking, "edgeHandle");
            CheckHoverReveal(window, docking, hwnd, strip, info.Work,
                (x, y) => pointer = new NativeMethods.Point { X = x, Y = y },
                (passed, name) => check(passed, "MainWindow: " + name), cycles: 1);
        }
        finally
        {
            docking.Dispose();
            window.Close();
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void CheckHoverReveal(Window window, EdgeDockController docking, nint hwnd, Window strip,
        NativeRect work, Action<int, int> movePointer, Action<bool, string> check, int cycles = 2)
    {
        var cover = new Window
        {
            Style = null, Width = 500, Height = 400, Opacity = 0,
            Topmost = true, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = false
        };
        try
        {
            docking.Undock();
            SetWindowPos(hwnd, 0, work.Left + 100, work.Top + 4, 400, 300, 0x0014);
            docking.SnapToEdge();
            movePointer(work.Right - 20, work.Bottom - 20);
            Pump(1800);
            check(docking.IsHidden, "Pointer outside automatically hides before hover test");

            cover.Show();
            var coverHwnd = new WindowInteropHelper(cover).Handle;
            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                SetWindowPos(coverHwnd, new nint(-1), work.Left + 100, work.Top, 500, 400, 0x0010);
                var foreground = GetForegroundWindow();
                GetWindowRect(hwnd, out var original);
                GetWindowRect(new WindowInteropHelper(strip).Handle, out var stripRect);
                movePointer((stripRect.Left + stripRect.Right) / 2, (stripRect.Top + stripRect.Bottom) / 2);
                if (cycle == 2)
                {
                    strip.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    check(window.IsVisible && !docking.IsHidden, "MouseEnter on the strip expands immediately without waiting for polling");
                }
                Pump(350);
                check(window.IsVisible && !docking.IsHidden && !strip.IsVisible, $"Hover cycle {cycle}: timer expands window and removes trigger");
                check(IsAbove(hwnd, coverHwnd), $"Hover cycle {cycle}: panel is above an overlapping topmost application window");
                check(GetForegroundWindow() == foreground, $"Hover cycle {cycle}: revealing does not steal keyboard focus");
                check(!window.Topmost && IsNativeTopmost(hwnd), $"Hover cycle {cycle}: temporary native elevation preserves the user's Topmost setting");

                GetWindowRect(hwnd, out var expanded);
                check(expanded.Equals(original), $"Hover cycle {cycle}: original window bounds are preserved");
                movePointer((expanded.Left + expanded.Right) / 2, (expanded.Top + expanded.Bottom) / 2);
                Pump(1400);
                check(window.IsVisible && !docking.IsHidden, $"Hover cycle {cycle}: moving into panel keeps it open");
                movePointer(work.Right - 20, work.Bottom - 20);
                Pump(700);
                check(docking.IsHidden && !window.IsVisible && strip.IsVisible, $"Hover cycle {cycle}: leaving panel hides it again");
                check(!IsNativeTopmost(hwnd), $"Hover cycle {cycle}: hiding removes temporary elevation");
            }
            docking.Reveal(false);
            docking.Undock();
            check(!window.Topmost && !IsNativeTopmost(hwnd), "Undocking restores normal window layering");
            window.Topmost = true;
            docking.SnapToEdge();
            docking.Toggle();
            docking.Reveal(false);
            docking.Undock();
            check(window.Topmost && IsNativeTopmost(hwnd), "Undocking preserves an explicitly enabled AlwaysOnTop setting");
        }
        finally
        {
            cover.Close();
        }
    }

    private static bool IsAbove(nint first, nint second)
    {
        for (var current = GetWindow(second, 3); current != 0; current = GetWindow(current, 3))
            if (current == first) return true;
        return false;
    }

    private static bool IsNativeTopmost(nint hwnd) => (GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0;

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
