using L = DockNest.Services.LocalizationService;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DockNest.Services;

namespace DockNest.Views;

public sealed class GitWindow : Window
{
    private sealed record Operation(string LabelKey, string[] Arguments, bool ChangesRepository = false)
    {
        public string Label => L.Get(LabelKey);
        public override string ToString() => Label;
    }

    private static readonly Operation[] Operations =
    [
        new("GitStatus", ["status", "--short", "--branch"]),
        new("GitDiff", ["diff", "--no-ext-diff", "--no-textconv"]),
        new("GitStagedDiff", ["diff", "--cached", "--no-ext-diff", "--no-textconv"]),
        new("GitLog", ["log", "-30", "--oneline", "--decorate"]),
        new("GitFetch", ["fetch"], true),
        new("GitPull", ["pull", "--ff-only"], true),
        new("GitStageAll", ["add", "--all"], true),
        new("GitCommit", ["commit"], true),
        new("GitPush", ["push"], true)
    ];
    private readonly GitService git;
    private readonly string target;
    private readonly ComboBox operation = new() { ItemsSource = Operations, SelectedIndex = 0, Width = 240 };
    private readonly TextBox message = new() { MinWidth = 110, Margin = new Thickness(10, 0, 10, 0), ToolTip = L.Get("CommitTooltip") };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"), FontSize = 12 };
    private readonly TextBlock repositoryLabel = new() { Text = L.Get("FindingRepository"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18) };
    private readonly Button run = new() { Content = L.Get("Execute"), IsEnabled = false, MinWidth = 76 };
    private readonly Button cancel = new() { Content = L.Get("CancelOperation"), IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button terminal = new() { Content = L.Get("OpenTerminal"), IsEnabled = false, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
        heading.Children.Add(new TextBlock { Text = L.Get("RepositoryOperations"), FontSize = 23, FontWeight = FontWeights.SemiBold });
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
            repositoryLabel.Text = repository ?? L.Get("NotRepository");
            if (repository is not null)
            {
                var result = await git.RunAsync(repository, Operations[0].Arguments, tokenSource.Token);
                output.Text = result.Output;
            }
            else output.Text = L.Get("BookmarkRepositoryHint");
        }
        catch (OperationCanceledException) { output.Text = L.Get("Canceled"); }
        catch (Exception error) { repositoryLabel.Text = L.Get("RepositoryReadFailed"); output.Text = error.Message + "\n\n" + L.Get("GitSetupHint"); }
        finally { cancellation = null; SetBusy(false); }
    }

    private async Task ExecuteAsync()
    {
        if (repository is null || running) return;
        var selected = (Operation)operation.SelectedItem;
        var arguments = selected.Arguments.ToList();
        if (arguments[0] == "commit")
        {
            if (string.IsNullOrWhiteSpace(message.Text)) { output.Text = L.Get("CommitRequired"); message.Focus(); return; }
            arguments.Add("-m"); arguments.Add(message.Text.Trim());
        }
        if (selected.ChangesRepository && MessageBox.Show(this,
            L.Format("GitConfirm", repository, selected.Label, string.Join(" ", arguments)),
            L.Get("GitConfirmTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        SetBusy(true);
        using var tokenSource = new CancellationTokenSource();
        cancellation = tokenSource;
        output.Text = $"> git {string.Join(" ", arguments)}\n\n{L.Get("Running")}";
        try
        {
            var result = await git.RunAsync(repository, arguments, tokenSource.Token);
            output.Text = $"> git {string.Join(" ", arguments)}\n\n{result.Output}\n\n{L.Format("ExitCode", result.ExitCode)}";
            if (result.Success && arguments[0] == "commit") message.Clear();
        }
        catch (OperationCanceledException) { output.AppendText("\n" + L.Get("GitCanceled")); }
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
        output.AppendText("\n" + L.Get("GitStopping"));
        e.Cancel = true;
    }
}
