using System.Text.RegularExpressions;

namespace Racks.Core;

public static class FileCatalog
{
    public static IReadOnlyList<CatalogItem> Read(IEnumerable<RackDefinition> racks, out List<string> errors, CancellationToken cancellation = default)
    {
        errors = new();
        var items = new List<CatalogItem>();
        foreach (var rack in racks)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (var path in Directory.EnumerateFileSystemEntries(rack.Folder))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(path);
                    if (name.StartsWith('.') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rack.IncludedNames != null && !rack.IncludedNames.Contains(name, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)) continue;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        var directory = (attributes & FileAttributes.Directory) != 0;
                        items.Add(new(rack.Id, rack.Title, path, name, directory, directory ? 0 : new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)));
                    }
                    catch (IOException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{rack.Title}: {ex.Message}"); }
        }
        return items;
    }

    public static IEnumerable<CatalogItem> Search(IEnumerable<CatalogItem> items, string query)
    {
        query = query.Trim();
        return items.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RackTitle);
    }

    public static List<OrganizeGroup> Organize(string folder) => Directory.EnumerateFileSystemEntries(folder)
        .Where(x => !Path.GetFileName(x).StartsWith('.') && (File.GetAttributes(x) & (FileAttributes.Hidden | FileAttributes.System)) == 0)
        .GroupBy(Category).Select(x => new OrganizeGroup { Name = x.Key, Paths = x.OrderBy(Path.GetFileName).ToList() })
        .OrderByDescending(x => x.Paths.Count).ThenBy(x => x.Name).ToList();

    public static string Category(string path)
    {
        if (Directory.Exists(path)) return "Folders";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" or ".heic" => "Images",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".rtf" or ".pages" => "Documents",
            ".xls" or ".xlsx" or ".csv" or ".numbers" => "Spreadsheets",
            ".ppt" or ".pptx" or ".key" => "Presentations",
            ".mp4" or ".mov" or ".mkv" or ".webm" => "Videos",
            ".mp3" or ".wav" or ".flac" or ".m4a" => "Audio",
            ".zip" or ".7z" or ".tar" or ".gz" => "Archives",
            ".lnk" or ".url" or ".app" or ".exe" => "Apps",
            _ => "Other"
        };
    }
}

public static class RuleMatcher
{
    public static bool Matches(RoutingRule rule, string path)
    {
        var name = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(rule.Extensions))
        {
            var extensions = rule.Extensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => "." + x.TrimStart('.', '*'));
            if (!extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) return false;
        }
        if (!string.IsNullOrWhiteSpace(rule.NameContains) && !name.Contains(rule.NameContains, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(rule.Regex) && !Regex.IsMatch(name, rule.Regex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50))) return false;
        return true;
    }

    public static void Validate(RoutingRule rule, AppSettings settings)
    {
        if (!Directory.Exists(rule.Source)) throw new IOException("Select an available source folder.");
        var destination = settings.Racks.FirstOrDefault(x => x.Id == rule.DestinationRackId) ?? throw new IOException("Select a destination rack.");
        if (string.IsNullOrWhiteSpace(rule.Extensions) && string.IsNullOrWhiteSpace(rule.NameContains) && string.IsNullOrWhiteSpace(rule.Regex))
            throw new IOException("Add a file type or filename condition.");
        if (settings.Racks.Any(x => SafeFiles.IsWithin(rule.Source, x.Folder)) || SafeFiles.IsWithin(destination.Folder, rule.Source))
            throw new IOException("Routing sources and rack folders must be separate to prevent loops.");
        if (rule.Regex.Length > 500) throw new IOException("The expression is too long.");
        if (!string.IsNullOrWhiteSpace(rule.Regex)) _ = new Regex(rule.Regex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
    }
}
