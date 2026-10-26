using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DockNest.Services;

namespace DockNest.Views;

public sealed class GitWindow : Window
{
    private sealed record Operation(string Label, string[] Arguments, bool ChangesRepository = false)
    {
        public override string ToString() => Label;
    }

    private static readonly Operation[] Operations =
    [
        new("查看状态", ["status", "--short", "--branch"]),
        new("查看未暂存差异", ["diff", "--no-ext-diff", "--no-textconv"]),
        new("查看已暂存差异", ["diff", "--cached", "--no-ext-diff", "--no-textconv"]),
        new("查看最近提交", ["log", "-30", "--oneline", "--decorate"]),
        new("获取远程更新", ["fetch"], true),
        new("拉取（仅快进）", ["pull", "--ff-only"], true),
        new("暂存仓库全部更改", ["add", "--all"], true),
        new("提交已暂存更改", ["commit"], true),
        new("推送当前分支", ["push"], true)
    ];
    private readonly GitService git;
    private readonly string target;
    private readonly ComboBox operation = new() { ItemsSource = Operations, SelectedIndex = 0, Width = 190 };
    private readonly TextBox message = new() { MinWidth = 200, Margin = new Thickness(10, 0, 10, 0), ToolTip = "提交信息：仅用于提交已暂存更改" };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
    private readonly TextBlock repositoryLabel = new() { Text = "正在识别所属仓库…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18) };
    private readonly Button run = new() { Content = "执行", IsEnabled = false, MinWidth = 76 };
    private readonly Button cancel = new() { Content = "取消操作", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button terminal = new() { Content = "在终端打开", IsEnabled = false, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private CancellationTokenSource? cancellation;
    private string? repository;
    private bool running;

    public GitWindow(string target, string executable)
    {
        this.target = target;
        git = new GitService(executable);
        Title = "Git · DockNest";
        WindowStyle = WindowStyle.SingleBorderWindow;
        AllowsTransparency = false;
        Width = 840; Height = 610; MinWidth = 720; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SidebarBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "仓库操作", FontSize = 23, FontWeight = FontWeights.SemiBold });
        repositoryLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        heading.Children.Add(repositoryLabel);
        root.Children.Add(heading);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(operation, Dock.Left); toolbar.Children.Add(operation);
        DockPanel.SetDock(cancel, Dock.Right); toolbar.Children.Add(cancel);
        DockPanel.SetDock(run, Dock.Right); toolbar.Children.Add(run);
        toolbar.Children.Add(message);
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        Grid.SetRow(output, 2); root.Children.Add(output);
        Grid.SetRow(terminal, 3); root.Children.Add(terminal);
        Content = root;
        run.SetResourceReference(StyleProperty, "PrimaryButton");
        message.Text = "";
        message.IsEnabled = false;
        operation.SelectionChanged += (_, _) => message.IsEnabled = ((Operation)operation.SelectedItem).Arguments[0] == "commit" && !running;
        run.Click += async (_, _) => await ExecuteAsync();
        cancel.Click += (_, _) => cancellation?.Cancel();
        terminal.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = repository!, UseShellExecute = true }); }
            catch (Exception error) { output.Text = error.Message; }
        };
        Loaded += async (_, _) => await InitializeAsync();
        Closing += OnClosing;
    }

    private async Task InitializeAsync()
    {
        SetBusy(true);
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        try
        {
            repository = await git.FindRepositoryAsync(target, tokenSource.Token);
            repositoryLabel.Text = repository ?? "此位置不属于 Git 工作区。";
            if (repository is not null)
            {
                var result = await git.RunAsync(repository, Operations[0].Arguments, tokenSource.Token);
                output.Text = result.Output;
            }
            else output.Text = "请收藏仓库内的文件或文件夹后，再打开 Git 操作。";
        }
        catch (OperationCanceledException) { output.Text = "已取消。"; }
        catch (Exception error) { repositoryLabel.Text = "无法读取仓库"; output.Text = error.Message + "\n\n请检查 Git 安装位置和目录权限；可在设置中指定 git.exe。"; }
        finally { cancellation = null; SetBusy(false); }
    }

    private async Task ExecuteAsync()
    {
        if (repository is null || running) return;
        var selected = (Operation)operation.SelectedItem;
        var arguments = selected.Arguments.ToList();
        if (arguments[0] == "commit")
        {
            if (string.IsNullOrWhiteSpace(message.Text)) { output.Text = "请输入提交信息。只会提交已经暂存的更改。"; message.Focus(); return; }
            arguments.Add("-m"); arguments.Add(message.Text.Trim());
        }
        if (selected.ChangesRepository && MessageBox.Show(this,
            $"仓库：{repository}\n\n操作：{selected.Label}\n命令：git {string.Join(" ", arguments)}\n\n确认执行？",
            "确认 Git 操作", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        SetBusy(true);
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        output.Text = $"> git {string.Join(" ", arguments)}\n\n正在执行…";
        try
        {
            var result = await git.RunAsync(repository, arguments, tokenSource.Token);
            output.Text = $"> git {string.Join(" ", arguments)}\n\n{result.Output}\n\n退出码：{result.ExitCode}";
            if (result.Success && arguments[0] == "commit") message.Clear();
        }
        catch (OperationCanceledException) { output.AppendText("\n已取消进程。操作可能已部分完成，请重新查看仓库状态。"); }
        catch (Exception error) { output.AppendText("\n" + error.Message); }
        finally { cancellation = null; SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        running = busy;
        run.IsEnabled = !busy && repository is not null;
        cancel.IsEnabled = busy;
        operation.IsEnabled = !busy;
        message.IsEnabled = !busy && ((Operation)operation.SelectedItem).Arguments[0] == "commit";
        terminal.IsEnabled = !busy && repository is not null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!running) return;
        cancellation?.Cancel();
        output.AppendText("\n正在停止操作，停止后可关闭窗口。");
        e.Cancel = true;
    }
}
