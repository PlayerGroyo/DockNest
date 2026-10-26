using System.Diagnostics;
using System.IO;
using DockNest.Models;

namespace DockNest.Services;

public static class TargetService
{
    public static Bookmark Create(string target, string? name = null)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("请填写路径或网址。");
        BookmarkKind kind;
        string suggestedName;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (string.IsNullOrWhiteSpace(uri.Host)) throw new ArgumentException("请输入完整的网址。");
            kind = BookmarkKind.Url;
            target = uri.AbsoluteUri;
            suggestedName = uri.Host;
        }
        else
        {
            if (!Path.IsPathFullyQualified(target) || target.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
                target.Contains('*') || target.Contains('?') ||
                target.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
                target.StartsWith("\\\\.\\", StringComparison.Ordinal))
                throw new ArgumentException("请输入完整的本地路径、\\\\服务器\\共享路径，或 http / https 网址。");
            target = Path.GetFullPath(target);
            // UNC shares can be offline. Do not probe them while adding or rendering a bookmark.
            if (target.StartsWith("\\\\", StringComparison.Ordinal)) kind = BookmarkKind.NetworkPath;
            else if (Directory.Exists(target)) kind = BookmarkKind.Folder;
            else if (File.Exists(target)) kind = BookmarkKind.File;
            else throw new ArgumentException("本地路径不存在，请检查路径后重试。");
            suggestedName = Path.GetFileName(Path.TrimEndingDirectorySeparator(target));
            if (string.IsNullOrEmpty(suggestedName)) suggestedName = target;
        }
        return new Bookmark { Name = string.IsNullOrWhiteSpace(name) ? suggestedName : name.Trim(), Target = target, Kind = kind };
    }

    public static bool IsSameTarget(string left, string right)
    {
        if (Uri.TryCreate(left, UriKind.Absolute, out var leftUri) &&
            Uri.TryCreate(right, UriKind.Absolute, out var rightUri) &&
            leftUri.Scheme is "http" or "https" && rightUri.Scheme is "http" or "https")
            return string.Equals(leftUri.AbsoluteUri, rightUri.AbsoluteUri, StringComparison.Ordinal);
        return string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
    }

    public static void Open(Bookmark item)
    {
        // Revalidate saved targets at the boundary; JSON files may have been edited externally.
        var checkedItem = Create(item.Target);
        Process.Start(new ProcessStartInfo(checkedItem.Target) { UseShellExecute = true });
    }

    public static void Reveal(Bookmark item)
    {
        if (item.Kind == BookmarkKind.Url) throw new InvalidOperationException("网页没有本地所在位置。");
        var checkedItem = Create(item.Target);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        if (checkedItem.Kind == BookmarkKind.File)
            start.Arguments = $"/select,\"{checkedItem.Target}\"";
        else start.ArgumentList.Add(checkedItem.Target);
        Process.Start(start);
    }
}
