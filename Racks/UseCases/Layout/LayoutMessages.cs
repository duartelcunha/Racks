using Racks.Properties;

namespace Racks.UseCases.Layout;

/// <summary>A message for the user: a title and a text.</summary>
public sealed record LayoutMessage(string Title, string Text);

/// <summary>
/// The texts the user sees after an import or an export (new strings are in <c>Lang.resx</c>). Pure: the dialog layer
/// shows them, the tests read them. A success that needs no message (racks were imported, a file was written) gives null.
/// </summary>
public static class LayoutMessages
{
    public static LayoutMessage? ForImport(ImportResult result)
    {
        string title = Lang.Layout_Import_Title;
        switch (result.Status)
        {
            case ImportStatus.Imported:
                return result.ImportedCount > 0 ? null : new LayoutMessage(title, Lang.Layout_Import_Empty);
            case ImportStatus.Rejected:
                return new LayoutMessage(title, string.Format(Lang.Layout_Import_Rejected, result.Message));
            default:
                if (result.RollbackFailed)
                    return new LayoutMessage(title, string.Format(Lang.Layout_Import_RollbackFailed, result.Message));
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
}
