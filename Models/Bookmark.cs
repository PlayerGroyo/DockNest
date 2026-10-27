using L = DockNest.Services.LocalizationService;
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

    internal void RefreshLanguage() => OnPropertyChanged(nameof(KindLabel));

    [JsonIgnore] public string KindLabel => Kind switch
    {
        BookmarkKind.Folder => L.Get("KindFolder"),
        BookmarkKind.Url => L.Get("KindUrl"),
        BookmarkKind.NetworkPath => L.Get("KindNetwork"),
        _ => L.Get("KindFile")
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
    [ObservableProperty] private string name = L.Get("DefaultGroupName");
    public ObservableCollection<Bookmark> Items { get; set; } = [];
}
