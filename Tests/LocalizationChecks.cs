using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DockNest;
using DockNest.Models;
using DockNest.Services;
using DockNest.Views;
using Forms = System.Windows.Forms;
using L = DockNest.Services.LocalizationService;

internal static class LocalizationChecks
{
    internal static void Run(Action<bool, string> check, string fixtures)
    {
        var chinese = ReadStrings("zh-CN");
        var english = ReadStrings("en-US");
        check(chinese.Keys.Order().SequenceEqual(english.Keys.Order()) && english.Values.All(v => !string.IsNullOrWhiteSpace(v)),
            "English and Chinese resources have matching, nonempty translations");
        check(chinese.All(p => CompositeFormat.Parse(p.Value).MinimumArgumentCount == CompositeFormat.Parse(english[p.Key]).MinimumArgumentCount),
            "Translated messages preserve all format arguments");
        var legacy = JsonSerializer.Deserialize<AppState>("{\"Version\":1,\"Groups\":[],\"Settings\":{\"Theme\":\"Dark\"}}")!;
        check(legacy.Settings.Language == "zh-CN", "Profiles without a language keep the existing Chinese default");
        L.Apply("unrecognized");
        check(L.CurrentLanguage == "zh-CN" && L.Get("Settings") == "设置", "Unsupported languages safely fall back to Chinese");

        var state = new AppState();
        var store = new StateStore(Path.Combine(fixtures, "language-profile"));
        var window = new MainWindow(store, state, false) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        var group = window.ViewModel.AddGroup("开发空间");
        window.ViewModel.AddGroup("工作文档");
        var bookmark = new Bookmark { Name = "中文项目", Target = @"D:\Source\DockNest", Kind = BookmarkKind.Folder };
        window.ViewModel.AddBookmark(group, bookmark);
        window.ViewModel.SelectedGroup = null;
        using var tray = new TrayIconService(window);
        var notify = Field<Forms.NotifyIcon>(tray, "notifyIcon");
        var traces = new StringWriter();
        using var listener = new TextWriterTraceListener(traces);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            window.Show();
            window.UpdateLayout();
            var original = File.ReadAllBytes(store.FilePath);
            EditSettings("en-US", false);
            check(L.CurrentLanguage == "zh-CN" && state.Settings.Language == "zh-CN" && File.ReadAllBytes(store.FilePath).SequenceEqual(original),
                "Canceling a language selection leaves the active language and saved profile unchanged");

            EditSettings("en-US", true);
            Pump();
            check(L.CurrentLanguage == "en-US" && store.Load().Settings.Language == "en-US", "Saving Settings switches language immediately and persists it");
            check(window.ViewModel.Heading == "All bookmarks" && window.ViewModel.CountLabel == "1 bookmark" && window.ViewModel.Status == "Settings saved",
                "Main heading, singular count and save status update without reopening the window");
            var labels = Descendants(window).OfType<TextBlock>().Select(t => t.Text).ToArray();
            check(labels.Contains("Add bookmark") && labels.Contains("Folder") && labels.Contains("Floating") && labels.Any(t => t.Contains("Ctrl + Space")),
                "Live XAML labels, bookmark kinds and status bar switch to English");
            check(Descendants(window).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Copy address" && Equals(b.ToolTip, "Copy address")),
                "Row tooltips and accessibility names switch to English");
            check(notify.ContextMenuStrip!.Items[0].Text == "Show main window" && notify.ContextMenuStrip.Items[2].Text == "Exit" && notify.Text.Contains("bookmarks"),
                "The existing tray icon and its menu update in place");
            var saved = store.Load();
            check(saved.Groups[0].Name == "开发空间" && saved.Groups[0].Items[0].Name == "中文项目" && saved.Groups[0].Items[0].Target == bookmark.Target,
                "Language changes preserve user group names, bookmark names and paths");

            var editor = new BookmarkDialog(window.ViewModel.Groups, group);
            Program.Render(editor, "language-add-bookmark-en.png", 560, 600);
            check(editor.Title == "Add bookmark" && Descendants(editor.Content as DependencyObject).OfType<Button>().Any(b => Equals(b.Content, "Choose folder")),
                "New bookmark dialogs use the selected language");
            editor.Close();
            var settings = new SettingsWindow(state.Settings, (_, _) => true, @"C:\Users\Demo\AppData\Local\DockNest");
            Program.Render(settings, "language-settings-en.png", 560, 700);
            check(settings.Title == "Settings" && Descendants((DependencyObject)settings.Content).OfType<ComboBox>().Single(c => c.Name == "LanguageSelector").SelectedValue as string == "en-US",
                "Reopened Settings displays the saved language");
            settings.Close();
            var git = new GitWindow(store.DirectoryPath, "git");
            check(Field<ComboBox>(git, "operation").Items.Cast<object>().First().ToString() == "Status" && Equals(Field<Button>(git, "run").Content, "Run"),
                "Git operation labels are translated when a dialog is opened");
            git.Close();
            try { TargetService.Create(""); check(false, "Target validation should reject an empty address"); }
            catch (ArgumentException e) { check(e.Message == "Enter a path or URL.", "App validation errors use the selected language"); }

            window.ViewModel.SelectedGroup = group;
            window.ViewModel.SearchText = "中文";
            window.ViewModel.FilterIndex = 1;
            EditSettings("zh-CN", true);
            Pump();
            check(window.ViewModel.SelectedGroup == group && window.ViewModel.SearchText == "中文" && window.ViewModel.FilterIndex == 1 && window.ViewModel.VisibleItems.Single() == bookmark,
                "Switching languages preserves the selected group, search, type filter and matching bookmarks");
            check(notify.ContextMenuStrip.Items[0].Text == "显示主窗口" && bookmark.KindLabel == "文件夹" && window.ViewModel.CountLabel == "1 个收藏",
                "Switching back restores Chinese tray text, bookmark kinds and counts");
            EditSettings("en-US", true);
            window.Close();
            var restarted = new MainWindow(store, store.Load(), false);
            check(restarted.ViewModel.Heading == "All bookmarks" && restarted.ViewModel.Settings.Language == "en-US",
                "A fresh main window restores the saved English language");
            restarted.Close();

            var failedSettings = new AppSettings();
            L.Apply("zh-CN");
            var conflict = new SettingsWindow(failedSettings, (_, _) => false, store.DirectoryPath) { Opacity = 0, ShowActivated = false };
            conflict.Loaded += (_, _) => conflict.Dispatcher.BeginInvoke(() =>
            {
                var selector = Descendants(conflict).OfType<ComboBox>().Single(c => c.Name == "LanguageSelector");
                selector.SelectedValue = "en-US";
                Descendants(conflict).OfType<Button>().Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                conflict.Close();
            });
            var accepted = conflict.ShowDialog();
            check(accepted != true && failedSettings.Language == "zh-CN" && L.CurrentLanguage == "zh-CN",
                "A failed shortcut validation does not partially apply a language change");
            listener.Flush();
            check(string.IsNullOrWhiteSpace(traces.ToString()), "Language switching produces no WPF binding warnings: " + traces);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            tray.Dispose();
            if (window.IsVisible) window.Close();
            L.Apply("zh-CN");
        }

        void EditSettings(string language, bool save)
        {
            Exception? failure = null;
            var visited = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                var dialog = window.OwnedWindows.OfType<SettingsWindow>().SingleOrDefault(w => w.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                try
                {
                    var selector = Descendants(dialog).OfType<ComboBox>().Single(c => c.Name == "LanguageSelector");
                    selector.SelectedValue = language;
                    visited = true;
                    if (save) Descendants(dialog).OfType<Button>().Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.Close();
                }
                catch (Exception error) { failure = error; dialog.Close(); }
            };
            timer.Start();
            try
            {
                var button = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.Get("Settings"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally { timer.Stop(); }
            if (failure is not null) throw failure;
            if (!visited) throw new InvalidOperationException("The real Settings button did not open its dialog.");
        }
    }

    private static Dictionary<string, string> ReadStrings(string language)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream($"DockNest.Localization.{language}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null) yield break;
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
