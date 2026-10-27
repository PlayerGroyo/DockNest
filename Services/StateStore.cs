using L = DockNest.Services.LocalizationService;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DockNest.Models;

namespace DockNest.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "state.json");
    private string? recoveredDataPath;
    public string? RecoveryNotice => recoveredDataPath is null ? null : L.Format("StateRecovered", recoveredDataPath);

    public StateStore(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DockNest");
    }

    public AppState Load()
    {
        if (!File.Exists(FilePath)) return new AppState();
        try { return Read(FilePath); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            AppState recovered;
            try { recovered = Read(FilePath + ".bak"); }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException(L.Format("StateReadFailed", FilePath), error);
            }

            var damagedPath = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}";
            File.Copy(FilePath, damagedPath, false);
            File.Copy(FilePath + ".bak", FilePath, true);
            recoveredDataPath = damagedPath;
            return recovered;
        }
    }

    public void Save(AppState state)
    {
        Validate(state);
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = FilePath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, Options);
            stream.Flush(true);
        }

        if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, FilePath + ".bak");
        else File.Move(temporaryPath, FilePath);
    }

    private static AppState Read(string path)
    {
        using var stream = File.OpenRead(path);
        var state = JsonSerializer.Deserialize<AppState>(stream, Options)
                    ?? throw new InvalidDataException(L.Get("StateEmpty"));
        Validate(state);
        return state;
    }

    private static void Validate(AppState state)
    {
        if (state.Version != 1)
            throw new NotSupportedException(L.Get("StateNewer"));
        if (state.Groups is null || state.Settings is null)
            throw new InvalidDataException(L.Get("StateInvalid"));
        var ids = new HashSet<Guid>();
        foreach (var group in state.Groups)
        {
            if (group is null || group.Id == Guid.Empty || !ids.Add(group.Id) ||
                string.IsNullOrWhiteSpace(group.Name) || group.Items is null)
                throw new InvalidDataException(L.Get("GroupInvalid"));
            foreach (var item in group.Items)
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                    string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Target) ||
                    !Enum.IsDefined(item.Kind))
                    throw new InvalidDataException(L.Get("BookmarkInvalid"));
        }
    }
}
