using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DockNest.Models;
using DockNest.Interop;
using DockNest.Services;
using DockNest.ViewModels;
using DockNest.Views;

namespace DockNest;

public partial class MainWindow : Window
{
    private readonly bool desktopIntegration;
    private HotkeyService? hotkey;
    private EdgeDockController? docking;
    private Point dragStart;
    private Bookmark? pressedItem;
    private bool dragged;
    private int modalDepth;
    public MainViewModel ViewModel { get; }

    public MainWindow(StateStore store, AppState state, bool enableDesktopIntegration = true)
    {
        desktopIntegration = enableDesktopIntegration;
        ViewModel = new MainViewModel(store, state);
        InitializeComponent();
        DataContext = ViewModel;
        ThemeService.Apply(state.Settings.Theme);
        Width = double.IsFinite(state.Settings.WindowWidth) ? Math.Clamp(state.Settings.WindowWidth, 780, 2560) : 1080;
        Height = double.IsFinite(state.Settings.WindowHeight) ? Math.Clamp(state.Settings.WindowHeight, 520, 1600) : 720;
        if (state.Settings.WindowLeft is double left && state.Settings.WindowTop is double top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left; Top = top;
        }
        Topmost = state.Settings.AlwaysOnTop;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += (_, _) => { hotkey?.Dispose(); docking?.Dispose(); };
        Loaded += (_, _) =>
        {
            docking?.KeepOnScreen();
            if (store.RecoveryNotice is not null) ReportError(store.RecoveryNotice);
        };
        UpdateHotkeyLabel();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!desktopIntegration) return;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        docking = new EdgeDockController(this, source) { Enabled = ViewModel.Settings.AutoHide };
        docking.StateChanged += () =>
        {
            var edge = docking.Edge switch { DockEdge.Left => "左侧", DockEdge.Right => "右侧", DockEdge.Top => "顶部", DockEdge.Bottom => "底部", _ => "" };
            DockStatus.Text = docking.Edge == DockEdge.None ? "自由悬浮" : edge + (docking.IsHidden ? " · 已隐藏" : " · 自动隐藏");
        };
        hotkey = new HotkeyService(source, () => { if (modalDepth == 0) docking.Toggle(); else Activate(); });
        if (!hotkey.TryRegister(ViewModel.Settings.HotkeyModifiers, ViewModel.Settings.HotkeyKey))
            Dispatcher.BeginInvoke(() =>
            {
                ViewModel.Status = "快捷键被占用 · 请在设置中更换组合";
                UpdateHotkeyLabel();
                ReportError("显示 / 隐藏快捷键注册失败，可能已被输入法或其他程序占用。请打开右上角设置更换组合。");
            });
        UpdateHotkeyLabel();
    }

    public void Reveal()
    {
        if (modalDepth > 0)
        {
            NativeMethods.SetForegroundWindow(NativeMethods.GetLastActivePopup(new WindowInteropHelper(this).Handle));
            return;
        }
        if (docking is not null) docking.Reveal(true);
        else { Show(); WindowState = WindowState.Normal; Activate(); }
    }

    internal void ExitFromTray()
    {
        // Finish the active editor/operation before exiting, just as with the disabled owner window.
        if (modalDepth > 0) Reveal();
        else Close();
    }

    private void UpdateHotkeyLabel() => HotkeyLabel.Text =
        HotkeyService.Format(ViewModel.Settings.HotkeyModifiers, ViewModel.Settings.HotkeyKey) +
        (desktopIntegration && hotkey is not null && !hotkey.IsRegistered ? "   未注册" : "   唤出 / 隐藏");

    private bool ShowEditor(Window dialog)
    {
        using var suspension = docking?.Suspend();
        modalDepth++;
        try { dialog.Owner = this; return dialog.ShowDialog() == true; }
        finally { modalDepth--; }
    }

    private void ReportError(string message)
    {
        ViewModel.Status = message.ReplaceLineEndings(" ");
        using var suspension = docking?.Suspend();
        modalDepth++;
        try { MessageBox.Show(this, message, "DockNest", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { modalDepth--; }
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception error) { ReportError(error.Message); }
    }

    private bool Confirm(string message)
    {
        using var suspension = docking?.Suspend();
        modalDepth++;
        try { return MessageBox.Show(this, message, "DockNest", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK; }
        finally { modalDepth--; }
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e) => AddGroup();

    private void AddGroup()
    {
        var dialog = new NameDialog("新建分组");
        if (ShowEditor(dialog)) Try(() => { ViewModel.AddGroup(dialog.Value); ViewModel.Status = "分组已创建"; });
    }

    private BookmarkGroup EnsureGroup() => ViewModel.SelectedGroup ?? ViewModel.Groups.FirstOrDefault() ?? ViewModel.AddGroup("常用");

    private void AddBookmark_Click(object sender, RoutedEventArgs e) => Try(() =>
    {
        var group = EnsureGroup();
        var dialog = new BookmarkDialog(ViewModel.Groups, group);
        if (ShowEditor(dialog) && dialog.Result is not null)
        {
            ViewModel.AddBookmark(dialog.SelectedGroup, dialog.Result);
            ViewModel.SelectedGroup = dialog.SelectedGroup;
            ViewModel.Status = "已添加收藏 · " + dialog.Result.Name;
        }
    });

    private void EditBookmark(Bookmark item) => Try(() =>
    {
        var source = ViewModel.FindGroup(item);
        var dialog = new BookmarkDialog(ViewModel.Groups, source, item);
        if (!ShowEditor(dialog) || dialog.Result is null || source is null) return;
        var index = source.Items.IndexOf(item);
        source.Items.Remove(item);
        var edited = dialog.Result;
        edited.Id = item.Id;
        if (dialog.SelectedGroup == source) source.Items.Insert(index, edited);
        else dialog.SelectedGroup.Items.Add(edited);
        ViewModel.Save();
        ViewModel.Status = "收藏已更新";
    });

    private void RemoveBookmark(Bookmark item)
    {
        if (!Confirm($"移除收藏“{item.Name}”？\n只移除收藏记录，原文件和文件夹保持不变。")) return;
        Try(() => { ViewModel.FindGroup(item)?.Items.Remove(item); ViewModel.Save(); ViewModel.Status = "收藏已移除"; });
    }

    private async void OpenBookmark(Bookmark item)
    {
        ViewModel.Status = "正在打开 · " + item.Name;
        try
        {
            await Task.Run(() => TargetService.Open(item));
            ViewModel.Status = "已打开 · " + item.Name;
        }
        catch (Exception error) { ReportError("无法打开目标，可右键编辑地址。\n" + error.Message); }
    }

    private void AllBookmarks_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedGroup = null;
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { ViewModel.SearchText = ""; SearchBox.Focus(); }

    private void Theme_Click(object sender, RoutedEventArgs e) => Try(() =>
    {
        ViewModel.Settings.Theme = ViewModel.Settings.Theme == "Light" ? "Dark" : "Light";
        ThemeService.Apply(ViewModel.Settings.Theme);
        ViewModel.Save();
    });

    private void Settings_Click(object sender, RoutedEventArgs e) => Try(() =>
    {
        var dialog = new SettingsWindow(ViewModel.Settings, (modifiers, key) => hotkey?.TryRegister(modifiers, key) ?? true, ViewModel.DataPath);
        if (!ShowEditor(dialog)) return;
        if (docking is not null) docking.Enabled = ViewModel.Settings.AutoHide;
        Topmost = ViewModel.Settings.AlwaysOnTop;
        UpdateHotkeyLabel();
        ViewModel.Save();
        ViewModel.Status = "设置已保存";
    });

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } }
    }

    private void ToggleMaximize()
    {
        docking?.Undock();
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Minimize_Click(object sender, RoutedEventArgs e) { docking?.Undock(); WindowState = WindowState.Minimized; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!desktopIntegration) return;
        docking?.Undock();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        ViewModel.Settings.WindowLeft = bounds.Left;
        ViewModel.Settings.WindowTop = bounds.Top;
        ViewModel.Settings.WindowWidth = bounds.Width;
        ViewModel.Settings.WindowHeight = bounds.Height;
        try { ViewModel.Save(); }
        catch (Exception error)
        {
            e.Cancel = true;
            ReportError("数据未能保存，窗口暂未关闭。请检查目录权限或磁盘空间后重试。\n" + error.Message);
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T found) return found;
            child = child is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private Bookmark? ItemAt(object source) => FindParent<ListBoxItem>(source as DependencyObject)?.DataContext as Bookmark;

    private void Bookmark_MouseDown(object sender, MouseButtonEventArgs e)
    {
        pressedItem = FindParent<ButtonBase>(e.OriginalSource as DependencyObject) is null && e.ClickCount == 1 ? ItemAt(e.OriginalSource) : null;
        dragStart = e.GetPosition(this);
        dragged = false;
    }

    private void Bookmark_MouseMove(object sender, MouseEventArgs e)
    {
        if (pressedItem is null || dragged || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragged = true;
        using var suspension = docking?.Suspend();
        DragDrop.DoDragDrop(BookmarkList, new DataObject("DockNest.Bookmark", pressedItem), DragDropEffects.Move);
        pressedItem = null;
    }

    private void Bookmark_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var item = pressedItem;
        pressedItem = null;
        if (!dragged && item is not null && FindParent<ButtonBase>(e.OriginalSource as DependencyObject) is null && ItemAt(e.OriginalSource) == item)
        { OpenBookmark(item); e.Handled = true; }
    }

    private void Bookmark_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is not Bookmark item) return;
        BookmarkList.SelectedItem = item;
        ShowBookmarkMenu(item, FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)!);
        e.Handled = true;
    }

    private void BookmarkMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Bookmark item } element) ShowBookmarkMenu(item, element);
        e.Handled = true;
    }

    private void CopyBookmark_Click(object sender, RoutedEventArgs e) => RunBookmarkAction(sender, e, CopyBookmark);
    private void EditBookmark_Click(object sender, RoutedEventArgs e) => RunBookmarkAction(sender, e, EditBookmark);
    private void RevealBookmark_Click(object sender, RoutedEventArgs e) => RunBookmarkAction(sender, e, RevealTarget);
    private void GitBookmark_Click(object sender, RoutedEventArgs e) => RunBookmarkAction(sender, e, ShowBookmarkGit);

    private void RunBookmarkAction(object sender, RoutedEventArgs e, Action<Bookmark> action)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: Bookmark item }) return;
        BookmarkList.SelectedItem = item;
        Try(() => action(item));
    }

    private void CopyBookmark(Bookmark item)
    {
        // Clipboard monitors can briefly hold the clipboard while receiving an update.
        System.Windows.Forms.Clipboard.SetDataObject(item.Target, true, 5, 100);
        ViewModel.Status = "地址已复制";
    }

    private void ShowBookmarkGit(Bookmark item) => ShowEditor(new GitWindow(item.Target, ViewModel.Settings.GitExecutable));

    private MenuItem Menu(string name, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = name, IsEnabled = enabled };
        item.Click += (_, _) => Try(action);
        return item;
    }

    private void ShowContext(ContextMenu menu, FrameworkElement target)
    {
        var suspension = docking?.Suspend();
        menu.PlacementTarget = target;
        menu.Closed += (_, _) => suspension?.Dispose();
        menu.IsOpen = true;
    }

    private void ShowBookmarkMenu(Bookmark item, FrameworkElement element)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("打开", () => OpenBookmark(item)));
        menu.Items.Add(Menu("编辑收藏…", () => EditBookmark(item)));
        menu.Items.Add(Menu("复制地址", () => CopyBookmark(item)));
        if (item.Kind != BookmarkKind.Url)
        {
            menu.Items.Add(Menu("在资源管理器中查看", () => RevealTarget(item)));
            menu.Items.Add(Menu("Git 仓库操作…", () => ShowBookmarkGit(item)));
        }
        var source = ViewModel.FindGroup(item);
        var move = new MenuItem { Header = "移动到分组", IsEnabled = ViewModel.Groups.Count > 1 };
        foreach (var group in ViewModel.Groups.Where(g => g != source))
            move.Items.Add(Menu(group.Name, () => { ViewModel.MoveBookmark(item, group); ViewModel.Status = "已移动到 " + group.Name; }));
        menu.Items.Add(move);
        var index = source?.Items.IndexOf(item) ?? -1;
        menu.Items.Add(Menu("上移", () => { source!.Items.Move(index, index - 1); ViewModel.Save(); }, index > 0));
        menu.Items.Add(Menu("下移", () => { source!.Items.Move(index, index + 1); ViewModel.Save(); }, source is not null && index < source.Items.Count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("移除收藏", () => RemoveBookmark(item)));
        ShowContext(menu, element);
    }

    private async void RevealTarget(Bookmark item)
    {
        try { await Task.Run(() => TargetService.Reveal(item)); }
        catch (Exception error) { ReportError(error.Message); }
    }

    private void Group_RightClick(object sender, MouseButtonEventArgs e)
    {
        var container = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not BookmarkGroup group) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu("重命名…", () =>
        {
            var dialog = new NameDialog("重命名分组", group.Name);
            if (!ShowEditor(dialog)) return;
            if (ViewModel.Groups.Any(g => g != group && string.Equals(g.Name, dialog.Value, StringComparison.CurrentCultureIgnoreCase)))
                throw new ArgumentException("已经有同名分组。");
            group.Name = dialog.Value;
            ViewModel.Save();
        }));
        var index = ViewModel.Groups.IndexOf(group);
        menu.Items.Add(Menu("上移", () => { ViewModel.Groups.Move(index, index - 1); ViewModel.Save(); }, index > 0));
        menu.Items.Add(Menu("下移", () => { ViewModel.Groups.Move(index, index + 1); ViewModel.Save(); }, index < ViewModel.Groups.Count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("删除分组", () =>
        {
            if (!Confirm($"删除分组“{group.Name}”及其中的 {group.Items.Count} 条收藏记录？\n原文件和文件夹保持不变。")) return;
            ViewModel.Groups.Remove(group);
            if (ViewModel.SelectedGroup == group) ViewModel.SelectedGroup = null;
            ViewModel.Save();
        }));
        ShowContext(menu, container);
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("DockNest.Bookmark") ? DragDropEffects.Move :
            e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        var group = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as BookmarkGroup;
        if (group is null) return;
        HandleDrop(e, group);
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Handled) return;
        Try(() => HandleDrop(e, EnsureGroup()));
        e.Handled = true;
    }

    private void HandleDrop(DragEventArgs e, BookmarkGroup group) => Try(() =>
    {
        if (e.Data.GetData("DockNest.Bookmark") is Bookmark existing)
        {
            ViewModel.MoveBookmark(existing, group);
            ViewModel.SelectedGroup = group;
            return;
        }
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ??
                    (e.Data.GetData(DataFormats.UnicodeText) as string)?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var added = 0;
        var duplicates = 0;
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try { if (ViewModel.AddBookmark(group, TargetService.Create(path))) added++; else duplicates++; }
            catch (Exception error) { errors.Add(path + "：" + error.Message); }
        }
        ViewModel.SelectedGroup = group;
        ViewModel.Status = $"已添加 {added} 个收藏" + (duplicates > 0 ? $" · 跳过 {duplicates} 个重复地址" : "");
        if (errors.Count > 0) ReportError($"已添加 {added} 个收藏；以下地址未能添加：\n" + string.Join("\n", errors.Take(6)));
    });

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { AddBookmark_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N) { AddGroup(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ViewModel.SearchText = ""; e.Handled = true; }
        else if (Keyboard.FocusedElement is not TextBox && FindParent<ButtonBase>(Keyboard.FocusedElement as DependencyObject) is null && BookmarkList.SelectedItem is Bookmark item)
        {
            if (e.Key == Key.Enter) { OpenBookmark(item); e.Handled = true; }
            else if (e.Key == Key.F2) { EditBookmark(item); e.Handled = true; }
            else if (e.Key == Key.Delete) { RemoveBookmark(item); e.Handled = true; }
        }
    }
}
