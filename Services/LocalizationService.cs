using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DockNest.Services;

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Chinese = Load("zh-CN");
    private static readonly IReadOnlyDictionary<string, string> English = Load("en-US");
    private static ResourceDictionary? resources;
    public static string CurrentLanguage { get; private set; } = "zh-CN";
    public static event Action? LanguageChanged;

    // Keep the existing Chinese default for old profiles and unsupported language codes.
    public static string Normalize(string? language) =>
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

    public static string Get(string key)
    {
        var strings = CurrentLanguage == "en-US" ? English : Chinese;
        return strings.TryGetValue(key, out var text) ? text : Chinese[key];
    }

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(CurrentLanguage), Get(key), arguments);

    public static void Apply(string? language)
    {
        Application.Current?.Dispatcher.VerifyAccess();
        var normalized = Normalize(language);
        bool changed = normalized != CurrentLanguage;
        CurrentLanguage = normalized;
        if (Application.Current is { } app)
        {
            var next = new ResourceDictionary();
            foreach (var key in Chinese.Keys) next["Text." + key] = Get(key);
            // Add the new dictionary first so existing DynamicResource bindings never lose their keys.
            app.Resources.MergedDictionaries.Add(next);
            if (resources is not null) app.Resources.MergedDictionaries.Remove(resources);
            resources = next;
        }
        if (changed) LanguageChanged?.Invoke();
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"DockNest.Localization.{language}.json")
            ?? throw new InvalidDataException($"Missing language resource: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Invalid language resource: {language}");
    }
}
