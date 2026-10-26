using System.Windows;
using DockNest.Services;

namespace DockNest;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? wakeEvent;
    private RegisteredWaitHandle? wakeRegistration;
    private TrayIconService? trayIcon;
    private bool ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\DockNest.SingleInstance", out ownsMutex);
        if (!ownsMutex)
        {
            try { using var existing = EventWaitHandle.OpenExisting(@"Local\DockNest.ShowWindow"); existing.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("DockNest 正在启动，请稍后再试。", "DockNest"); }
            Shutdown();
            return;
        }
        try
        {
            wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DockNest.ShowWindow");
            var store = new StateStore();
            var window = new MainWindow(store, store.Load());
            MainWindow = window;
            trayIcon = new TrayIconService(window);
            wakeRegistration = ThreadPool.RegisterWaitForSingleObject(wakeEvent, (_, _) =>
                Dispatcher.BeginInvoke(() => window.Reveal()), null, Timeout.Infinite, false);
            window.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DockNest 无法启动", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        trayIcon?.Dispose();
        wakeRegistration?.Unregister(null);
        wakeEvent?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
