using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace DockNest.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly MainWindow window;
    private readonly Icon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.NotifyIcon notifyIcon;
    private bool disposed;

    internal TrayIconService(MainWindow window)
    {
        this.window = window;
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/DockNest;component/Assets/DockNest.ico"))
            ?? throw new InvalidOperationException("无法加载 DockNest 托盘图标。");
        using (resource.Stream)
        using (var sourceIcon = new Icon(resource.Stream))
            icon = (Icon)sourceIcon.Clone();

        menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatch(window.Reveal));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatch(window.ExitFromTray));
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "DockNest · 文件与文件夹收藏",
            ContextMenuStrip = menu
        };
        notifyIcon.MouseClick += OnMouseClick;
        notifyIcon.MouseDoubleClick += OnMouseClick;
        try { notifyIcon.Visible = true; }
        catch { Dispose(); throw; }
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) Dispatch(window.Reveal);
    }

    private void Dispatch(Action action) => window.Dispatcher.BeginInvoke(() =>
    {
        if (!disposed) action();
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
    }
}
