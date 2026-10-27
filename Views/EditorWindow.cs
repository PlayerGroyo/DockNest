using L = DockNest.Services.LocalizationService;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DockNest.Models;
using DockNest.Services;
using Microsoft.Win32;

namespace DockNest.Views;

// Small task dialogs share the same spacing and application resources.
public class EditorWindow : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(28, 24, 28, 8) };
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(28, 12, 28, 24) };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };

    public EditorWindow(string title, string description)
    {
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        AllowsTransparency = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        SetResourceReference(BackgroundProperty, "SidebarBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new DockPanel();
        DockPanel.SetDock(Actions, Dock.Bottom);
        panel.Children.Add(Actions);
        panel.Children.Add(Fields);
        Content = panel;
        Fields.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), LineHeight = 22 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Fields.Children.Add(hint);
        Error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    protected void Field(string label, UIElement input)
    {
        Fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 16, 0, 8), FontSize = 12 });
        Fields.Children.Add(input);
    }

    protected Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 82 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action();
        Actions.Children.Add(button);
        return button;
    }

    protected void Finish(Action save)
    {
        Fields.Children.Add(Error);
        ActionButton(L.Get("Cancel"), Close);
        ActionButton(L.Get("Save"), () => { try { save(); DialogResult = true; } catch (Exception error) { Error.Text = error.Message; } }, true).IsDefault = true;
    }
}

public sealed class NameDialog : EditorWindow
{
    private readonly TextBox name = new();
    public string Value => name.Text.Trim();
    public NameDialog(string title, string initial = "") : base(title, L.Get("GroupHint"))
    {
        name.Text = initial;
        name.MaxLength = 60;
        Field(L.Get("GroupName"), name);
        Finish(() => { if (Value.Length == 0) throw new ArgumentException(L.Get("GroupNameRequired")); });
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}

public sealed class BookmarkDialog : EditorWindow
{
    private readonly TextBox name = new() { MaxLength = 120 };
    private readonly TextBox target = new();
    private readonly ComboBox group = new() { DisplayMemberPath = "Name" };
    private readonly Bookmark? original;
    public Bookmark? Result { get; private set; }
    public BookmarkGroup SelectedGroup => (BookmarkGroup)group.SelectedItem;

    public BookmarkDialog(IEnumerable<BookmarkGroup> groups, BookmarkGroup? selected, Bookmark? item = null)
        : base(item is null ? L.Get("AddBookmark") : L.Get("EditBookmark"), L.Get("BookmarkHint"))
    {
        original = item;
        group.ItemsSource = groups;
        group.SelectedItem = selected ?? groups.FirstOrDefault();
        name.Text = item?.Name ?? "";
        target.Text = item?.Target ?? "";
        Field(L.Get("Address"), target);
        var browse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        var fileButton = new Button { Content = L.Get("ChooseFile"), Padding = new Thickness(12, 6, 12, 6) };
        fileButton.Click += (_, _) => { var picker = new OpenFileDialog(); if (picker.ShowDialog(this) == true) target.Text = picker.FileName; };
        var folderButton = new Button { Content = L.Get("ChooseFolder"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        folderButton.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) target.Text = picker.FolderName; };
        browse.Children.Add(fileButton);
        browse.Children.Add(folderButton);
        Fields.Children.Add(browse);
        var example = new TextBlock { Text = L.Get("AddressExample"), Margin = new Thickness(0, 9, 0, 0), FontSize = 11 };
        example.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Fields.Children.Add(example);
        Field(L.Get("DisplayName"), name);
        Field(L.Get("BookmarkGroup"), group);
        Finish(() =>
        {
            if (group.SelectedItem is null) throw new ArgumentException(L.Get("GroupRequired"));
            // A missing target may still be renamed without losing the existing reference.
            Result = original is not null && target.Text.Trim() == original.Target
                ? new Bookmark { Id = original.Id, Name = string.IsNullOrWhiteSpace(name.Text) ? original.Name : name.Text.Trim(), Target = original.Target, Kind = original.Kind }
                : TargetService.Create(target.Text, name.Text);
            if (SelectedGroup.Items.Any(i => i.Id != original?.Id && TargetService.IsSameTarget(i.Target, Result.Target)))
                throw new ArgumentException(L.Get("DuplicateBookmark"));
        });
        Loaded += (_, _) => target.Focus();
    }
}

public sealed class SettingsWindow : EditorWindow
{
    private sealed record LanguageOption(string Code, string Name);
    private uint modifiers, key;
    public SettingsWindow(AppSettings settings, Func<uint, uint, bool> registerHotkey, string dataPath)
        : base(L.Get("Settings"), L.Get("SettingsHint"))
    {
        var language = new ComboBox
        {
            Name = "LanguageSelector", DisplayMemberPath = "Name", SelectedValuePath = "Code",
            ItemsSource = new[] { new LanguageOption("zh-CN", "简体中文"), new LanguageOption("en-US", "English") },
            SelectedValue = L.Normalize(settings.Language)
        };
        System.Windows.Automation.AutomationProperties.SetName(language, L.Get("Language"));
        Field(L.Get("Language"), language);
        var languageHint = new TextBlock { Text = L.Get("LanguageHint"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
        languageHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Fields.Children.Add(languageHint);
        modifiers = settings.HotkeyModifiers;
        key = settings.HotkeyKey;
        var hotkey = new TextBox { IsReadOnly = true, Text = HotkeyService.Format(modifiers, key) };
        hotkey.PreviewKeyDown += (_, e) =>
        {
            var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) { e.Handled = true; return; }
            if (pressed is Key.Tab or Key.Escape) return;
            var combination = Keyboard.Modifiers;
            if ((combination & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                Error.Text = L.Get("HotkeyModifiersRequired");
                e.Handled = true;
                return;
            }
            modifiers = (uint)combination;
            key = (uint)KeyInterop.VirtualKeyFromKey(pressed);
            hotkey.Text = HotkeyService.Format(modifiers, key);
            Error.Text = "";
            e.Handled = true;
        };
        Field(L.Get("HotkeyField"), hotkey);
        var autoHide = new CheckBox { Content = L.Get("AutoHide"), IsChecked = settings.AutoHide, Margin = new Thickness(0, 18, 0, 0) };
        var topmost = new CheckBox { Content = L.Get("AlwaysOnTop"), IsChecked = settings.AlwaysOnTop, Margin = new Thickness(0, 12, 0, 0) };
        autoHide.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        topmost.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        Fields.Children.Add(autoHide);
        Fields.Children.Add(topmost);
        var git = new TextBox { Text = settings.GitExecutable };
        Field(L.Get("GitExecutable"), git);
        var path = new TextBox { Text = dataPath, IsReadOnly = true };
        Field(L.Get("DataDirectory"), path);
        Finish(() =>
        {
            if (string.IsNullOrWhiteSpace(git.Text)) throw new ArgumentException(L.Get("GitPathRequired"));
            if (!registerHotkey(modifiers, key)) throw new InvalidOperationException(L.Get("HotkeyChangeFailed"));
            settings.HotkeyModifiers = modifiers;
            settings.HotkeyKey = key;
            settings.AutoHide = autoHide.IsChecked == true;
            settings.AlwaysOnTop = topmost.IsChecked == true;
            settings.GitExecutable = git.Text.Trim().Trim('"');
            settings.Language = (string)language.SelectedValue;
        });
    }
}
