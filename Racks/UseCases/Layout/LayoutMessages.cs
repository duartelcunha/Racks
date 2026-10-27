using Racks.Properties;

namespace Racks.UseCases.Layout;

/// <summary>A message for the user: a title and a text.</summary>
public sealed record LayoutMessage(string Title, string Text);

/// <summary>
/// The texts the user sees after an import or an export (new strings are in <c>Lang.resx</c>). Pure: the dialog layer
/// shows them, the tests read them. A success that needs no message (racks were imported with nothing left out, a file was
/// written) gives null.
/// </summary>
public static class LayoutMessages
{
    /// <summary>Most dropped values listed in the message; the audit log has them all (up to its own cap).</summary>
    public const int MaxListedDropped = 8;

    public static LayoutMessage? ForImport(ImportResult result)
    {
        string title = Lang.Layout_Import_Title;
        switch (result.Status)
        {
            case ImportStatus.Imported:
                if (result.ImportedCount == 0) return new LayoutMessage(title, Lang.Layout_Import_Empty);
                return result.Dropped is { Count: > 0 } dropped ? new LayoutMessage(title, DroppedText(result.ImportedCount, dropped)) : null;
            case ImportStatus.Rejected:
                return new LayoutMessage(title, string.Format(Lang.Layout_Import_Rejected, result.Message));
            default:
                if (result.RollbackFailed)
                    return new LayoutMessage(title, string.Format(Lang.Layout_Import_RollbackFailed, result.Message, RecoveryText(result)));
                return new LayoutMessage(title, string.Format(result.RolledBack ? Lang.Layout_Import_Failed : Lang.Layout_Import_Rejected, result.Message));
        }
    }

    public static LayoutMessage? ForExport(ExportResult result) =>
        result.Ok ? null : new LayoutMessage(Lang.Layout_Export_Title, string.Format(Lang.Layout_Export_Failed, result.Message));

    /// <summary>For an exception that escaped everything else (a dialog that failed to open): the user still sees something.</summary>
    public static LayoutMessage Unexpected(bool import, Exception ex) =>
        import
            ? new LayoutMessage(Lang.Layout_Import_Title, string.Format(Lang.Layout_Import_Unexpected, ex.Message))
            : new LayoutMessage(Lang.Layout_Export_Title, string.Format(Lang.Layout_Export_Failed, ex.Message));

    // Where the copy of the previous racks is, or that none could be written (and why).
    private static string RecoveryText(ImportResult result) =>
        result.RecoveryFile != null
            ? string.Format(Lang.Layout_Import_RecoverySaved, result.RecoveryFile)
            : string.Format(Lang.Layout_Import_RecoveryFailed, result.RecoveryError ?? "unknown error");

    private static string DroppedText(int imported, IReadOnlyList<DroppedValue> dropped)
    {
        string list = string.Join("\n", dropped.Take(MaxListedDropped).Select(d => "- " + d));
        if (dropped.Count > MaxListedDropped) list += "\n" + string.Format(Lang.Layout_Import_DroppedMore, dropped.Count - MaxListedDropped);
        return string.Format(Lang.Layout_Import_Dropped, imported, list);
    }
}
