using System.Text.Json;
using System.Text.Json.Serialization;

namespace Racks.Core;

public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{Path.GetFileName(path)} is empty.");

    public static void Write<T>(string path, T value, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup ? path + ".bak" : null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class SettingsStore(AppPaths paths)
{
    public AppSettings Load()
    {
        if (!File.Exists(paths.Settings)) return new();
        var settings = JsonStore.Read<AppSettings>(paths.Settings);
        Validate(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        JsonStore.Write(paths.Settings, settings);
    }

    public AppSettings RestoreBackup()
    {
        var settings = JsonStore.Read<AppSettings>(paths.Settings + ".bak");
        Validate(settings);
        if (File.Exists(paths.Settings)) File.Copy(paths.Settings, paths.Settings + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), false);
        JsonStore.Write(paths.Settings, settings, backup: false);
        return settings;
    }

    public static void Validate(AppSettings settings)
    {
        if (settings.Version != 2) throw new InvalidDataException("This settings version needs a different Racks release.");
        if (settings.Racks is null || settings.Rules is null || settings.Racks.Count > 1000 || settings.Rules.Count > 1000)
            throw new InvalidDataException("Invalid rack or rule collection.");
        if (settings.Racks.Any(x => x == null) || settings.Rules.Any(x => x == null)) throw new InvalidDataException("Empty rack or rule entry.");
        if (settings.Racks.Select(x => x.Id).Distinct().Count() != settings.Racks.Count)
            throw new InvalidDataException("Duplicate rack identifiers.");
        foreach (var rack in settings.Racks)
        {
            if (rack.Id == Guid.Empty || string.IsNullOrWhiteSpace(rack.Title) || rack.Title.Length > 200 ||
                !Enum.IsDefined(rack.Kind) || !Path.IsPathFullyQualified(rack.Folder))
                throw new InvalidDataException("A rack has an invalid name, type, identifier, or folder.");
            if (string.IsNullOrWhiteSpace(rack.FontFamily) || rack.FontFamily.Length > 200 || rack.Background is null || rack.Foreground is null || rack.Accent is null || rack.Sort is null)
                throw new InvalidDataException("A rack has invalid appearance settings.");
            if (rack.IncludedNames?.Any(x => !SafeFiles.IsLeafName(x)) == true)
                throw new InvalidDataException("Invalid legacy file reference.");
            if (!double.IsFinite(rack.X) || !double.IsFinite(rack.Y) || !double.IsFinite(rack.Width) || !double.IsFinite(rack.Height) ||
                rack.Width is < 240 or > 10000 || rack.Height is < 120 or > 10000 ||
                !double.IsFinite(rack.Opacity) || rack.Opacity is < .2 or > 1 ||
                !double.IsFinite(rack.FontSize) || rack.FontSize is < 8 or > 48 || rack.IconSize is < 16 or > 128)
                throw new InvalidDataException("A rack has invalid dimensions or appearance values.");
        }
        foreach (var rule in settings.Rules)
            if (rule.Id == Guid.Empty || rule.Name is null || rule.Source is null || rule.Extensions is null || rule.NameContains is null || rule.Regex is null || rule.Regex.Length > 500)
                throw new InvalidDataException("A routing rule has invalid fields.");
        if (settings.Rules.Select(x => x.Id).Distinct().Count() != settings.Rules.Count) throw new InvalidDataException("Duplicate rule identifiers.");
    }
}
