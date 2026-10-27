using System.Text.Json;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// The JSON shape of a layout file. What the parser reads is UNTRUSTED: nothing in it is checked yet. The exporter writes
/// the same shape, so a file exported by any earlier build still imports. Values are JSON scalars; numbers and strings
/// arrive as <see cref="JsonElement"/>.
/// </summary>
public sealed class LayoutDocument
{
    public int Version { get; set; } = LayoutLimits.CurrentVersion;
    public string ExportedBy { get; set; } = "Racks";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public List<LayoutDocumentRack?>? Racks { get; set; } = new();
}

/// <summary>One rack of a <see cref="LayoutDocument"/>. <see cref="Name"/> becomes a registry key name.</summary>
public sealed class LayoutDocumentRack
{
    public string? Name { get; set; } = "";
    public Dictionary<string, object?>? Values { get; set; } = new();
    public Dictionary<string, string[]?>? MultiStringValues { get; set; } = new();
}

/// <summary>What the parser made of the bytes: the untrusted document, or why it was refused.</summary>
public sealed record LayoutParseResult(LayoutDocument? Document, string? Error)
{
    public bool Ok => Error == null;
    public static LayoutParseResult Fail(string error) => new(null, error);
}

/// <summary>
/// One rack that passed every check, in the form the registry stores it. <see cref="Writes"/> is exactly what the executor
/// writes (stored kinds as the settings repository writes them); <see cref="Settings"/> is the same data typed.
/// </summary>
public sealed class ValidatedRack
{
    internal ValidatedRack(string name, RackSettings settings, IReadOnlyList<RegistryWrite> writes)
    {
        Name = name;
        Settings = settings;
        Writes = writes;
    }

    /// <summary>The registry key name: one safe segment, unique in the file (not case sensitive).</summary>
    public string Name { get; }

    /// <summary>The validated values, typed. A property is null when the file did not carry that value.</summary>
    public RackSettings Settings { get; }

    /// <summary>The registry changes for this rack, in order.</summary>
    public IReadOnlyList<RegistryWrite> Writes { get; }

    /// <summary>The single-value entries as stored (canonical names; an empty AssignedFiles reads as an empty text).</summary>
    public IReadOnlyDictionary<string, object> Values => Build(multi: false, v => v);

    /// <summary>The list entries (REG_MULTI_SZ) as stored.</summary>
    public IReadOnlyDictionary<string, string[]> MultiStringValues => Build(multi: true, v => (string[])v);

    private Dictionary<string, T> Build<T>(bool multi, Func<object, T> convert)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var w in Writes)
        {
            bool isMulti = w.Kind == Microsoft.Win32.RegistryValueKind.MultiString;
            if (isMulti != multi) continue;
            result[w.Name] = convert(w.IsRemoval ? "" : w.Data!);
        }
        return result;
    }
}

/// <summary>The checked layout, or the reason the whole file is refused.</summary>
public sealed record LayoutValidationResult(IReadOnlyList<ValidatedRack>? Racks, string? Error, IReadOnlyList<DroppedValue>? Dropped = null)
{
    public bool Ok => Error == null;
    public static LayoutValidationResult Fail(string error) => new(null, error);
}

public enum ImportStatus
{
    /// <summary>The racks were written (or the file held none: <see cref="ImportResult.ImportedCount"/> is then 0).</summary>
    Imported,
    /// <summary>The file was refused (or could not be read). Nothing was touched.</summary>
    Rejected,
    /// <summary>The registry work failed. See <see cref="ImportResult.StateChanged"/>, <see cref="ImportResult.RolledBack"/> and <see cref="ImportResult.RollbackFailed"/>.</summary>
    Failed,
}

/// <summary>
/// How an import ended. It never throws into the UI. For <see cref="ImportStatus.Failed"/>: if
/// <see cref="StateChanged"/> is false nothing was modified; otherwise the previous racks were put back
/// (<see cref="RolledBack"/>) or could NOT be fully put back (<see cref="RollbackFailed"/>, with
/// <see cref="RollbackDetails"/>), which the caller must tell the user.
/// </summary>
public sealed record ImportResult(
    ImportStatus Status,
    int ImportedCount,
    string Message,
    bool StateChanged = false,
    bool RolledBack = false,
    bool RollbackFailed = false,
    string RollbackDetails = "",
    IReadOnlyList<DroppedValue>? Dropped = null,
    string? RecoveryFile = null,
    string? RecoveryError = null)
{
    public static ImportResult Imported(int count) => new(ImportStatus.Imported, count, "");
    public static ImportResult Rejected(string reason) => new(ImportStatus.Rejected, 0, reason);
}

/// <summary>How an export ended. It never throws into the UI.</summary>
public sealed record ExportResult(bool Ok, string Message, int RackCount = 0)
{
    public static ExportResult Done(int racks) => new(true, "", racks);
    public static ExportResult Fail(string message) => new(false, message);
}

/// <summary>
/// One value of an imported layout that was not used as written, exactly as the registry loader would not use it: it was out of
/// range, not a number, not True/False, not a defined enum member, or a list over its caps or with bad items. The default
/// applies (or, for a number the loader clamps and a list's good items, the corrected value). The rest of the file is imported.
/// </summary>
public sealed record DroppedValue(string Rack, string Field, string Reason)
{
    public override string ToString() => $"rack '{Rack}': {Field} ({Reason})";
}
