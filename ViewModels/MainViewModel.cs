using L = DockNest.Services.LocalizationService;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DockNest.Models;
using DockNest.Services;

namespace DockNest.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StateStore store;
    public ObservableCollection<BookmarkGroup> Groups { get; }
    public ObservableCollection<Bookmark> VisibleItems { get; } = [];
    public AppSettings Settings { get; }
    [ObservableProperty] private BookmarkGroup? selectedGroup;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private int filterIndex;
    [ObservableProperty] private string status = L.Get("Ready");

    public string Heading => SelectedGroup?.Name ?? L.Get("AllBookmarks");
    public string Description => SelectedGroup is null ? L.Get("AllDescription") : L.Get("GroupDescription");
    public int TotalCount => Groups.Sum(g => g.Items.Count);
    public string CountLabel => L.Format(VisibleItems.Count == 1 ? "CountOne" : "CountMany", VisibleItems.Count);
    public bool IsEmpty => VisibleItems.Count == 0;
    public string EmptyTitle => SearchText.Length > 0 || FilterIndex != 0 ? L.Get("NoMatches") : L.Get("EmptyTitle");
    public string EmptyDescription => SearchText.Length > 0 || FilterIndex != 0 ? L.Get("NoMatchesHint") : L.Get("EmptyDescription");
    public string DataPath => store.DirectoryPath;

    public MainViewModel(StateStore store, AppState state)
    {
        this.store = store;
        Groups = new ObservableCollection<BookmarkGroup>(state.Groups);
        Settings = state.Settings;
        Refresh();
    }

    partial void OnSelectedGroupChanged(BookmarkGroup? value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnFilterIndexChanged(int value) => Refresh();

    public void Refresh()
    {
        var query = SearchText.Trim();
        var items = SelectedGroup is null ? Groups.SelectMany(g => g.Items) : SelectedGroup.Items.AsEnumerable();
        if (query.Length > 0)
            items = items.Where(i => i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                     i.Target.Contains(query, StringComparison.OrdinalIgnoreCase));
        BookmarkKind? kind = FilterIndex switch { 1 => BookmarkKind.Folder, 2 => BookmarkKind.File, 3 => BookmarkKind.Url, 4 => BookmarkKind.NetworkPath, _ => null };
        if (kind is not null) items = items.Where(i => i.Kind == kind);
        VisibleItems.Clear();
        foreach (var item in items) VisibleItems.Add(item);
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        foreach (var item in Groups.SelectMany(group => group.Items)) item.RefreshLanguage();
        foreach (var property in new[] { nameof(Heading), nameof(Description), nameof(TotalCount), nameof(CountLabel), nameof(IsEmpty), nameof(EmptyTitle), nameof(EmptyDescription) })
            OnPropertyChanged(property);
    }

    public void Save()
    {
        store.Save(new AppState { Groups = Groups.ToList(), Settings = Settings });
        Refresh();
    }

    public BookmarkGroup AddGroup(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException(L.Get("GroupNameRequired"));
        if (Groups.Any(g => string.Equals(g.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new ArgumentException(L.Get("DuplicateGroup"));
        var group = new BookmarkGroup { Name = name };
        Groups.Add(group);
        SelectedGroup = group;
        Save();
        return group;
    }

    public bool AddBookmark(BookmarkGroup group, Bookmark item)
    {
        if (group.Items.Any(i => TargetService.IsSameTarget(i.Target, item.Target))) return false;
        group.Items.Add(item);
        Save();
        return true;
    }

    public BookmarkGroup? FindGroup(Bookmark item) => Groups.FirstOrDefault(g => g.Items.Contains(item));

    public bool MoveBookmark(Bookmark item, BookmarkGroup destination)
    {
        var source = FindGroup(item);
        if (source is null || source == destination) return false;
        if (destination.Items.Any(i => TargetService.IsSameTarget(i.Target, item.Target)))
            throw new InvalidOperationException(L.Get("DuplicateMove"));
        source.Items.Remove(item);
        destination.Items.Add(item);
        Save();
        return true;
    }
}
