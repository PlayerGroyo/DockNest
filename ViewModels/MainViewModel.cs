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
    [ObservableProperty] private string status = "就绪 · 将文件或文件夹拖入窗口即可收藏";

    public string Heading => SelectedGroup?.Name ?? "全部收藏";
    public string Description => SelectedGroup is null ? "常用的文件、文件夹和链接，都在这里。" : "一个空间，收好这一组常用位置。";
    public int TotalCount => Groups.Sum(g => g.Items.Count);
    public string CountLabel => $"{VisibleItems.Count} 个收藏";
    public bool IsEmpty => VisibleItems.Count == 0;
    public string EmptyTitle => SearchText.Length > 0 || FilterIndex != 0 ? "没有找到匹配的收藏" : "把常用的位置，收进来";
    public string EmptyDescription => SearchText.Length > 0 || FilterIndex != 0 ? "换个关键词，或试试其他类型。" : "拖入文件或文件夹，也可以添加网页和网络位置。\n只保存地址，原文件始终留在原处。";
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
        if (name.Length == 0) throw new ArgumentException("请输入分组名称。");
        if (Groups.Any(g => string.Equals(g.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            throw new ArgumentException("已经有同名分组。");
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
            throw new InvalidOperationException("目标分组已经收藏了这个地址。");
        source.Items.Remove(item);
        destination.Items.Add(item);
        Save();
        return true;
    }
}
