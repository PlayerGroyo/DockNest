using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DockNest.Models;

public enum BookmarkKind { File, Folder, Url, NetworkPath }

public partial class Bookmark : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string target = "";
    [ObservableProperty] private BookmarkKind kind;

    [JsonIgnore] public string KindLabel => Kind switch
    {
        BookmarkKind.Folder => "文件夹",
        BookmarkKind.Url => "网页",
        BookmarkKind.NetworkPath => "网络位置",
        _ => "文件"
    };

    [JsonIgnore] public string IconGlyph => Kind switch
    {
        BookmarkKind.Folder => "\uE8B7",
        BookmarkKind.Url => "\uE774",
        BookmarkKind.NetworkPath => "\uE968",
        _ => "\uE8A5"
    };
}

public partial class BookmarkGroup : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [ObservableProperty] private string name = "新分组";
    public ObservableCollection<Bookmark> Items { get; set; } = [];
}
