namespace DockNest.Models;

public sealed class AppState
{
    public int Version { get; set; } = 1;
    public List<BookmarkGroup> Groups { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public bool AlwaysOnTop { get; set; }
    public bool AutoHide { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 2; // MOD_CONTROL
    public uint HotkeyKey { get; set; } = 0x20; // VK_SPACE
    public string GitExecutable { get; set; } = "git";
    public double WindowWidth { get; set; } = 1080;
    public double WindowHeight { get; set; } = 720;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}
