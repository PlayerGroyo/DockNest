using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DockNest.Interop;

namespace DockNest.Services;

public enum DockEdge { None, Left, Right, Top, Bottom }

public sealed class EdgeDockController : IDisposable
{
    private readonly Window window;
    private readonly HwndSource source;
    private readonly DispatcherTimer timer;
    private readonly Func<NativeMethods.Point?> readCursor;
    private NativeMethods.Rect visibleRect;
    private NativeMethods.Rect triggerRect;
    private Window? edgeHandle;
    private long outsideSince;
    private long shownAt;
    private bool moving;
    private bool enabled = true;
    private int suspensions;
    private bool raisedForDock;
    public DockEdge Edge { get; private set; }
    public bool IsHidden { get; private set; }
    public bool Enabled
    {
        get => enabled;
        set { enabled = value; if (!value) Undock(); }
    }
    public event Action? StateChanged;

    public EdgeDockController(Window window, HwndSource source) : this(window, source, ReadCursor)
    {
    }

    // Physical screen coordinates; a separate reader also lets the native-window tests control hover input.
    internal EdgeDockController(Window window, HwndSource source, Func<NativeMethods.Point?> readCursor)
    {
        this.window = window;
        this.source = source;
        this.readCursor = readCursor;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += Tick;
        source.AddHook(WindowProc);
    }

    public IDisposable Suspend()
    {
        suspensions++;
        Reveal(false);
        return new Suspension(this);
    }

    private sealed class Suspension(EdgeDockController owner) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { disposed = true; owner.suspensions--; owner.shownAt = Stopwatch.GetTimestamp(); } }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) { Reveal(false); moving = true; timer.Stop(); }
        else if (message == 0x0232) { moving = false; SnapToEdge(); }
        else if (message is 0x007E or 0x02E0) // Display layout or per-monitor DPI changed.
            window.Dispatcher.BeginInvoke(() => { Undock(); KeepOnScreen(); });
        return 0;
    }

    public void KeepOnScreen()
    {
        if (window.WindowState != WindowState.Normal) return;
        NativeMethods.GetWindowRect(source.Handle, out var rect);
        var work = NativeMethods.GetWorkArea(source.Handle);
        Move(Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - rect.Width)),
             Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - rect.Height)));
    }

    public void SnapToEdge()
    {
        if (!Enabled || window.WindowState != WindowState.Normal) { Undock(); return; }
        NativeMethods.GetWindowRect(source.Handle, out var rect);
        var work = NativeMethods.GetWorkArea(source.Handle);
        var threshold = 12 * Scale;
        var candidates = new[]
        {
            (DockEdge.Left, Math.Abs(rect.Left - work.Left)),
            (DockEdge.Right, Math.Abs(rect.Right - work.Right)),
            (DockEdge.Top, Math.Abs(rect.Top - work.Top)),
            (DockEdge.Bottom, Math.Abs(rect.Bottom - work.Bottom))
        };
        var closest = candidates.OrderBy(c => c.Item2).First();
        if (closest.Item2 > threshold) { Undock(); return; }
        Edge = closest.Item1;
        var x = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - rect.Width));
        var y = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - rect.Height));
        if (Edge == DockEdge.Left) x = work.Left;
        if (Edge == DockEdge.Right) x = work.Right - rect.Width;
        if (Edge == DockEdge.Top) y = work.Top;
        if (Edge == DockEdge.Bottom) y = work.Bottom - rect.Height;
        Move(x, y);
        NativeMethods.GetWindowRect(source.Handle, out visibleRect);
        shownAt = Stopwatch.GetTimestamp();
        outsideSince = 0;
        timer.Start();
        StateChanged?.Invoke();
    }

    public void Toggle()
    {
        if (Edge != DockEdge.None)
        {
            if (IsHidden || !window.IsVisible || window.WindowState == WindowState.Minimized) Reveal(true);
            else Conceal();
        }
        else if (!window.IsVisible || window.WindowState == WindowState.Minimized) Reveal(true);
        else window.Hide();
    }

    public void Reveal(bool activate)
    {
        var wasDockedHidden = IsHidden && Edge != DockEdge.None;
        if (!window.IsVisible)
        {
            var previous = window.ShowActivated;
            window.ShowActivated = activate;
            window.Show();
            window.ShowActivated = previous;
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (wasDockedHidden) Move(visibleRect.Left, visibleRect.Top);
        // ShowActivated=false only restores visibility. A foreground (including topmost) app can
        // still cover the panel, so lift it without taking keyboard focus while it is docked.
        if (Edge != DockEdge.None)
        {
            NativeMethods.SetWindowPos(source.Handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            raisedForDock = true;
        }
        edgeHandle?.Hide();
        IsHidden = false;
        shownAt = Stopwatch.GetTimestamp();
        outsideSince = 0;
        if (activate) { window.Activate(); NativeMethods.SetForegroundWindow(source.Handle); }
        StateChanged?.Invoke();
    }

    public void Undock()
    {
        if (IsHidden) Reveal(false);
        edgeHandle?.Hide();
        Edge = DockEdge.None;
        RestoreTopmostPreference();
        timer.Stop();
        StateChanged?.Invoke();
    }

    private double Scale => Math.Max(96, NativeMethods.GetDpiForWindow(source.Handle)) / 96.0;

    private void Conceal()
    {
        if (IsHidden || Edge == DockEdge.None || suspensions > 0 || moving) return;
        NativeMethods.GetWindowRect(source.Handle, out visibleRect);
        var work = NativeMethods.GetWorkArea(source.Handle);
        var strip = (int)Math.Ceiling(6 * Scale);
        triggerRect = visibleRect;
        if (Edge == DockEdge.Left) { triggerRect.Left = work.Left; triggerRect.Right = work.Left + strip; }
        if (Edge == DockEdge.Right) { triggerRect.Left = work.Right - strip; triggerRect.Right = work.Right; }
        if (Edge == DockEdge.Top) { triggerRect.Top = work.Top; triggerRect.Bottom = work.Top + strip; }
        if (Edge == DockEdge.Bottom) { triggerRect.Top = work.Bottom - strip; triggerRect.Bottom = work.Bottom; }
        // A separate strip avoids placing the full hidden window on an adjacent monitor.
        if (edgeHandle is null)
        {
            edgeHandle = new Window
            {
                // WPF UI's implicit Window style enforces a 460 x 320 DIP minimum.
                // The edge trigger must stay independent of normal application window styles.
                Style = null, MinWidth = 0, MinHeight = 0,
                Width = 6, Height = 6, WindowStyle = WindowStyle.None, AllowsTransparency = false,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                Topmost = true, Background = new SolidColorBrush(Color.FromRgb(80, 164, 145)),
                Title = "DockNest edge"
            };
            edgeHandle.MouseEnter += EdgeHandle_MouseEnter;
        }
        edgeHandle.Show();
        var stripHandle = new WindowInteropHelper(edgeHandle).Handle;
        NativeMethods.SetWindowPos(stripHandle, 0, triggerRect.Left, triggerRect.Top,
            triggerRect.Width, triggerRect.Height, 0x0004 | 0x0010);
        IsHidden = true;
        window.Hide();
        RestoreTopmostPreference();
        outsideSince = 0;
        StateChanged?.Invoke();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (moving || suspensions > 0 || Mouse.Captured is not null || Mouse.LeftButton == MouseButtonState.Pressed) return;
        if (readCursor() is not { } cursor) return;
        if (IsHidden)
        {
            if (triggerRect.Contains(cursor)) Reveal(false);
            return;
        }
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return;
        NativeMethods.GetWindowRect(source.Handle, out var rect);
        var inside = rect.Contains(cursor);
        if (inside) { outsideSince = 0; return; }
        if (Stopwatch.GetElapsedTime(shownAt).TotalMilliseconds < 900) return;
        if (outsideSince == 0) outsideSince = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(outsideSince).TotalMilliseconds >= 300) Conceal();
    }

    private void Move(int x, int y) => NativeMethods.SetWindowPos(source.Handle, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);

    private static NativeMethods.Point? ReadCursor() => NativeMethods.GetCursorPos(out var cursor) ? cursor : null;

    private void EdgeHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (IsHidden && !moving && suspensions == 0) Reveal(false);
    }

    private void RestoreTopmostPreference()
    {
        if (!raisedForDock || source.IsDisposed) return;
        NativeMethods.SetWindowPos(source.Handle, new nint(window.Topmost ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        raisedForDock = false;
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Tick;
        source.RemoveHook(WindowProc);
        RestoreTopmostPreference();
        if (edgeHandle is not null) edgeHandle.MouseEnter -= EdgeHandle_MouseEnter;
        edgeHandle?.Close();
    }
}
