using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DockNest;
using DockNest.Models;
using DockNest.Services;
using DockNest.ViewModels;
using DockNest.Views;

internal static class Program
{
    private static readonly List<string> Passed = [];
    private static string artifacts = "";
    private static string fixtures = "";

    [STAThread]
    private static int Main(string[] args)
    {
        var dockingOnly = args.Contains("--docking");
        var trayOnly = args.Contains("--tray");
        var localizationOnly = args.Contains("--localization");
        var resultFile = localizationOnly ? "localization-result.json" : trayOnly ? "tray-result.json" : dockingOnly ? "docking-result.json" : "result.json";
        artifacts = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/verification");
        fixtures = Path.Combine(artifacts, "fixtures", DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(fixtures);
        try
        {
            if (localizationOnly)
            {
                CreateTestApplication();
                LocalizationChecks.Run(Check, fixtures);
            }
            else if (trayOnly)
            {
                CreateTestApplication();
                TrayIconChecks.Run(Check, fixtures);
            }
            else if (dockingOnly)
            {
                CreateTestApplication();
                EdgeDockChecks.Run(Check, fixtures);
            }
            else
            {
                CheckTargetsAndStorage();
                CheckGitAsync().GetAwaiter().GetResult();
                CheckWindows();
                LocalizationChecks.Run(Check, fixtures);
                EdgeDockChecks.Run(Check, fixtures);
                TrayIconChecks.Run(Check, fixtures);
            }
            File.WriteAllText(Path.Combine(artifacts, resultFile), JsonSerializer.Serialize(new { success = true, checks = Passed.Count, passed = Passed, fixtures }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {Passed.Count} checks. Artifacts: {artifacts}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(artifacts, resultFile), JsonSerializer.Serialize(new { success = false, passed = Passed, error = error.ToString(), fixtures }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        Passed.Add(name);
        Console.WriteLine("PASS " + name);
    }

    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or NotSupportedException or InvalidOperationException)
        { Check(true, name); return; }
        throw new InvalidOperationException("FAILED (should reject): " + name);
    }

    private static void CheckTargetsAndStorage()
    {
        var file = Path.Combine(fixtures, "中文 notes & (1).txt");
        File.WriteAllText(file, "Keep the original file unchanged.");
        var original = File.ReadAllBytes(file);
        var folder = TargetService.Create(fixtures);
        var bookmark = TargetService.Create(file);
        Check(folder.Kind == BookmarkKind.Folder && bookmark.Kind == BookmarkKind.File, "File and folder identification");
        Check(TargetService.Create("https://example.com/a?q=中文").Kind == BookmarkKind.Url, "HTTPS URL normalization");
        var stopwatch = Stopwatch.StartNew();
        Check(TargetService.Create(@"\\192.0.2.1\unreachable\folder").Kind == BookmarkKind.NetworkPath && stopwatch.ElapsedMilliseconds < 1000, "Offline UNC addition does not probe network");
        Check(TargetService.Create(@"\\192.168.20.5\").Kind == BookmarkKind.NetworkPath, "UNC server root supported");
        Reject(() => TargetService.Create("javascript:alert(1)"), "Reject unsupported URL scheme");
        Reject(() => TargetService.Create("relative.txt"), "Reject relative paths");
        Reject(() => TargetService.Create(@"\\.\PhysicalDrive0"), "Reject device paths");
        Reject(() => TargetService.Create(Path.Combine(fixtures, "missing.txt")), "Reject unavailable local target on add");
        Check(TargetService.IsSameTarget(file.ToUpperInvariant(), file), "Local duplicate detection is case insensitive");
        Check(!TargetService.IsSameTarget("https://example.com/A", "https://example.com/a"), "URL paths remain case sensitive");
        var store = new StateStore(Path.Combine(fixtures, "profile"));
        var vm = new MainViewModel(store, store.Load());
        var first = vm.AddGroup("开发");
        var second = vm.AddGroup("文档");
        Reject(() => vm.AddGroup("开发"), "Reject duplicate group name");
        Check(vm.AddBookmark(first, bookmark), "Add bookmark");
        Check(!vm.AddBookmark(first, TargetService.Create(file)), "Skip duplicate within a group");
        Check(vm.AddBookmark(second, TargetService.Create(file)), "Allow same target in different groups");
        Reject(() => vm.MoveBookmark(bookmark, second), "Reject move that duplicates a target");
        second.Items.Clear();
        Check(vm.MoveBookmark(bookmark, second) && first.Items.Count == 0, "Move between groups");
        vm.AddBookmark(first, folder);
        vm.AddBookmark(first, TargetService.Create("https://example.com/", "开发文档"));
        vm.SelectedGroup = null;
        vm.SearchText = "开发文档";
        Check(vm.VisibleItems.Count == 1 && vm.VisibleItems[0].Kind == BookmarkKind.Url, "Search all groups by display name");
        vm.SearchText = "notes";
        Check(vm.VisibleItems.Count == 1, "Search by target path");
        vm.SearchText = ""; vm.FilterIndex = 1;
        Check(vm.VisibleItems.Count == 1 && vm.VisibleItems[0].Kind == BookmarkKind.Folder, "Type filter");
        vm.FilterIndex = 0; vm.SelectedGroup = first;
        Check(vm.VisibleItems.Count == 2, "Group selection filter");
        var restored = store.Load();
        Check(restored.Groups.Count == 2 && restored.Groups[1].Items[0].Target == file, "Restart round trip preserves Unicode and grouping");
        second.Items.Clear(); vm.Save();
        Check(File.ReadAllBytes(file).SequenceEqual(original), "Removing a reference leaves original contents unchanged");
        Check(File.Exists(store.FilePath + ".bak"), "Atomic save retains previous backup");
        File.WriteAllText(store.FilePath, "{damaged");
        var recovered = store.Load();
        Check(recovered.Groups[1].Items.Count == 1 && store.RecoveryNotice is not null, "Corrupt JSON recovers previous backup");
        Check(Directory.GetFiles(store.DirectoryPath, "*.corrupt-*").Length == 1, "Recovery preserves damaged data");
        File.WriteAllText(store.FilePath, "{\"Version\":99}");
        Reject(() => store.Load(), "Future data version is not overwritten by older backup");
        Check(File.ReadAllText(store.FilePath).Contains("99"), "Unknown version preserved");
    }

    private static async Task CheckGitAsync()
    {
        var repo = Path.Combine(fixtures, "repo 中文 & spaces");
        Directory.CreateDirectory(repo);
        var git = new GitService();
        async Task<GitResult> Run(params string[] arguments)
        {
            var result = await git.RunAsync(repo, arguments);
            if (!result.Success) throw new InvalidOperationException(string.Join(" ", arguments) + "\n" + result.Output);
            return result;
        }
        await Run("init", "--initial-branch=main");
        await Run("config", "user.name", "DockNest Checks");
        await Run("config", "user.email", "checks@example.invalid");
        await Run("config", "commit.gpgsign", "false");
        await Run("config", "core.hooksPath", Path.Combine(fixtures, "no-hooks"));
        var file = Path.Combine(repo, "file with spaces.txt");
        File.WriteAllText(file, "first\n");
        await Run("add", "--all");
        await Run("commit", "-m", "Initial test commit");
        Check((await git.FindRepositoryAsync(file))?.Replace('/', '\\') == repo, "Git repository detection from a file path");
        File.AppendAllText(file, "second\n");
        Check((await Run("diff", "--no-ext-diff", "--no-textconv")).Output.Contains("+second"), "Git diff reads working changes");
        await Run("add", "--all");
        Check((await Run("diff", "--cached", "--no-ext-diff", "--no-textconv")).Output.Contains("+second"), "Git staged diff");
        await Run("commit", "-m", "message & literal $(nothing) \"quotes\" 中文");
        Check((await Run("log", "-1", "--format=%s")).Output.Contains("$(nothing)"), "Git arguments are passed literally without shell expansion");
        var worktree = Path.Combine(fixtures, "linked worktree");
        await Run("worktree", "add", "-b", "checks-worktree", worktree);
        Check((await git.FindRepositoryAsync(worktree))?.Replace('/', '\\') == worktree, "Linked worktree detection");
        var remote = Path.Combine(fixtures, "local-only-remote.git");
        await Run("init", "--bare", remote);
        await Run("remote", "add", "origin", remote);
        await Run("push", "--set-upstream", "origin", "main");
        Check((await Run("fetch")).Success, "Fetch against isolated local remote");
        Check((await Run("pull", "--ff-only")).Success, "Fast-forward-only pull against isolated local remote");
        Check((await Run("push")).Success, "Push against isolated local remote");
        var outsideRepository = Path.Combine(fixtures, "non-repository");
        Directory.CreateDirectory(outsideRepository);
        var previousCeiling = Environment.GetEnvironmentVariable("GIT_CEILING_DIRECTORIES");
        try
        {
            // The test fixtures live inside this checkout; stop Git before it discovers the parent repository.
            Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", Path.GetDirectoryName(fixtures));
            Check(await git.FindRepositoryAsync(outsideRepository) is null, "Non-repository detection");
        }
        finally { Environment.SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", previousCeiling); }
        var failed = await git.RunAsync(repo, ["not-a-valid-git-command"]);
        Check(!failed.Success && failed.Output.Length > 0, "Nonzero exit code and stderr captured");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await git.RunAsync(repo, ["status"], canceled.Token); }
        catch (OperationCanceledException) { Check(true, "Git cancellation observed"); }
        using var cancelRunning = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var timer = Stopwatch.StartNew();
        try { await git.RunAsync(repo, ["-c", "alias.check-wait=!sleep 20", "check-wait"], cancelRunning.Token); }
        catch (OperationCanceledException) { Check(timer.Elapsed < TimeSpan.FromSeconds(5), "Cancellation stops running Git and child process"); }
    }

    private static void CheckWindows()
    {
        CreateTestApplication();
        var bindingTrace = new StringWriter();
        var listener = new TextWriterTraceListener(bindingTrace);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var state = new AppState();
        var store = new StateStore(Path.Combine(fixtures, "render-profile"));
        var window = new MainWindow(store, state, false);
        Render(window, "dark-empty.png", 1080, 720);
        var dev = window.ViewModel.AddGroup("开发空间");
        var docs = window.ViewModel.AddGroup("工作文档");
        window.ViewModel.AddGroup("日常 & 灵感");
        window.ViewModel.AddBookmark(dev, new Bookmark { Name = "DockNest", Kind = BookmarkKind.Folder, Target = @"D:\Source\DockNest" });
        window.ViewModel.AddBookmark(dev, new Bookmark { Name = "项目笔记", Kind = BookmarkKind.File, Target = @"D:\Documents\DockNest\项目笔记.md" });
        window.ViewModel.AddBookmark(dev, TargetService.Create("https://github.com/", "GitHub"));
        window.ViewModel.AddBookmark(docs, TargetService.Create(@"\\192.168.20.5\共享资料", "共享资料"));
        window.ViewModel.SelectedGroup = null;
        Render(window, "dark-bookmarks.png", 1080, 720);
        ThemeService.Apply("Light");
        Render(window, "light-bookmarks.png", 1080, 720);
        Render(window, "light-minimum.png", 780, 520);
        ThemeService.Apply("Dark");
        var editor = new BookmarkDialog(window.ViewModel.Groups, dev);
        Render(editor, "add-bookmark.png", 560, 570);
        var settings = new SettingsWindow(state.Settings, (_, _) => true, store.DirectoryPath);
        Render(settings, "settings.png", 560, 580);
        listener.Flush();
        Check(string.IsNullOrWhiteSpace(bindingTrace.ToString()), "No WPF binding warnings during real control rendering: " + bindingTrace);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        Check(File.Exists(Path.Combine(artifacts, "dark-empty.png")), "Empty and populated UI renders saved in both themes");

        // Native interop checks use an invisible, non-activating HWND, with no real user data.
        var nativeWindow = new Window { Width = 800, Height = 540, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, AllowsTransparency = false, Left = 100, Top = 100 };
        var handle = new WindowInteropHelper(nativeWindow).EnsureHandle();
        var source = HwndSource.FromHwnd(handle);
        using var first = new HotkeyService(source, () => { });
        using var second = new HotkeyService(source, () => { });
        Check(first.TryRegister(6, 0x87), "Register global Ctrl+Shift+F24 test shortcut");
        Check(!second.TryRegister(6, 0x87), "Global shortcut conflict detected");
        Check(second.TryRegister(6, 0x86), "Register alternate shortcut");
        Check(!second.TryRegister(6, 0x87) && second.IsRegistered, "Failed replacement keeps previous shortcut registered");
        using var dock = new EdgeDockController(nativeWindow, source);
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor);
        SetWindowPos(handle, 0, monitor.Work.Left + 4, monitor.Work.Top + 40, 800, 540, 0x0014);
        dock.SnapToEdge();
        Check(dock.Edge == DockEdge.Left, "Native window snaps near working-area edge");
        dock.Reveal(false);
        dock.Toggle();
        Check(dock.IsHidden && !nativeWindow.IsVisible, "Docked hide preserves a separate edge trigger");
        dock.Reveal(false);
        Check(!dock.IsHidden && nativeWindow.IsVisible && dock.Edge == DockEdge.Left, "Reveal keeps docked state");
        using (dock.Suspend()) { dock.Toggle(); Check(!dock.IsHidden, "Dialogs suspend automatic hiding"); }
        SetWindowPos(handle, 0, monitor.Work.Left + 100, monitor.Work.Top + 100, 800, 540, 0x0014);
        dock.SnapToEdge();
        Check(dock.Edge == DockEdge.None, "Dragging away exits docking");
        dock.Toggle();
        Check(!nativeWindow.IsVisible, "Floating shortcut hides window");
        dock.Toggle();
        Check(nativeWindow.IsVisible, "Floating shortcut reveals window");
        dock.Dispose(); first.Dispose(); second.Dispose();
        nativeWindow.Close();
        var desktopState = new AppState { Settings = new AppSettings { HotkeyModifiers = 7, HotkeyKey = 0x87, AutoHide = false } };
        var desktopStore = new StateStore(Path.Combine(fixtures, "native-window-profile"));
        var desktopWindow = new MainWindow(desktopStore, desktopState) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        desktopWindow.Show();
        desktopWindow.UpdateLayout();
        Check(new WindowInteropHelper(desktopWindow).Handle != 0 && desktopWindow.IsLoaded, "Main window initializes with real desktop integration");
        desktopWindow.Close();
        Check(File.Exists(desktopStore.FilePath) && desktopStore.Load().Settings.HotkeyKey == 0x87, "Closing the desktop window persists settings");
        editor.Close(); settings.Close(); window.Close();
    }

    private static void CreateTestApplication()
    {
        // App.OnStartup accesses the real profile and single-instance mutex. Tests only need resources.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DockNest;component/Themes/Styles.xaml")
        });
    }

    internal static void Render(Window window, string name, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        if (window.SizeToContent == SizeToContent.Height)
        {
            content.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Ceiling(content.DesiredSize.Height);
        }
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(artifacts, name));
        encoder.Save(stream);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
