using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DockNest;
using DockNest.Interop;
using DockNest.Models;
using DockNest.Services;
using DockNest.Views;
using Forms = System.Windows.Forms;

internal static class TrayIconChecks
{
    internal static void Run(Action<bool, string> check, string fixtures)
    {
        var store = new StateStore(Path.Combine(fixtures, "tray-profile"));
        var state = new AppState { Settings = new AppSettings { HotkeyModifiers = 7, HotkeyKey = 0x87 } };
        var window = new MainWindow(store, state) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        using var tray = new TrayIconService(window);
        // Read only the framework's native identity, then exercise the real shell registration/callbacks.
        var notify = Field<Forms.NotifyIcon>(tray, "notifyIcon");
        var native = Field<Forms.NativeWindow>(notify, "_window");
        var identity = new IconIdentifier
        {
            Size = (uint)Marshal.SizeOf<IconIdentifier>(),
            Window = native.Handle,
            Id = Convert.ToUInt32(Field<object>(notify, "_id"))
        };
        var callback = Convert.ToUInt32(typeof(Forms.NotifyIcon).GetField("WM_TRAYMOUSEMESSAGE", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
        try
        {
            check(IsRegistered(ref identity), "Tray icon is registered with Windows before the main window is shown");
            window.Show();
            var docking = Field<EdgeDockController>(window, "docking");
            docking.Toggle();
            check(!window.IsVisible && IsRegistered(ref identity), "Tray icon remains available while the main window is hidden");
            Click(0x0202); // WM_LBUTTONUP, as delivered by the notification area.
            check(window.IsVisible, "Native tray single-click restores the hidden main window");

            window.WindowState = WindowState.Minimized;
            Click(0x0203); // WM_LBUTTONDBLCLK.
            Click(0x0202);
            check(window.IsVisible && window.WindowState == WindowState.Normal, "Native tray double-click restores a minimized main window");

            var hwnd = new WindowInteropHelper(window).Handle;
            var work = NativeMethods.GetWorkArea(hwnd);
            NativeMethods.SetWindowPos(hwnd, 0, work.Left + 50, work.Top, 0, 0, 0x0015);
            docking.SnapToEdge();
            docking.Toggle();
            check(docking.IsHidden && IsRegistered(ref identity), "Tray icon remains registered during edge auto-hide");
            notify.ContextMenuStrip!.Items[0].PerformClick();
            Pump();
            check(window.IsVisible && !docking.IsHidden && docking.Edge == DockEdge.Top,
                "Tray show command restores an edge-hidden window and preserves docking");
            docking.Undock();

            var dialog = new NameDialog("Tray modal check") { Opacity = 0, ShowInTaskbar = false };
            dialog.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Click(0x0202);
                    notify.ContextMenuStrip.Items[2].PerformClick();
                    Pump();
                    check(!closed && dialog.IsVisible, "Tray actions preserve an active modal editor instead of discarding it");
                }
                finally { dialog.Close(); }
            });
            typeof(MainWindow).GetMethod("ShowEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [dialog]);

            System.ComponentModel.CancelEventHandler cancel = (_, e) => e.Cancel = true;
            window.Closing += cancel;
            notify.ContextMenuStrip.Items[2].PerformClick();
            Pump();
            check(!closed && IsRegistered(ref identity), "Canceled exit keeps the window and tray icon available");
            window.Closing -= cancel;
            state.Settings.Theme = "Light";
            notify.ContextMenuStrip.Items[2].PerformClick();
            Pump();
            check(closed && store.Load().Settings.Theme == "Light", "Tray exit closes through the normal data-saving path");

            tray.Dispose();
            tray.Dispose();
            check(!IsRegistered(ref identity), "Disposal removes the native tray icon and is safe to repeat");
        }
        finally
        {
            tray.Dispose();
            if (!closed) window.Close();
        }

        void Click(uint message)
        {
            SendMessage(identity.Window, callback, (nint)identity.Id, (nint)message);
            Pump();
        }
    }

    private static bool IsRegistered(ref IconIdentifier identity) => Shell_NotifyIconGetRect(ref identity, out _) == 0;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identity, out NativeMethods.Rect rect);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
