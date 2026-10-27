using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// Layout import and export as the UI uses them: parser, then validator (with the security policy), then the executor for an
/// import; the exporter for an export. Every outcome is a result object, every failure is audited, nothing throws into the
/// UI. The parts are separate classes so each one is tested alone; this class only puts them in order.
/// </summary>
public sealed class LayoutService
{
    private readonly LayoutImportExecutor _executor;
    private readonly LayoutExporter _exporter;
    private readonly IAuditSink _audit;

    /// <param name="recoveryDirectory">Where a recovery file goes if an import fails and cannot restore the previous racks.</param>
    public LayoutService(IRackRegistry registry, IRackSettingsStore store, IAuditSink audit, string recoveryDirectory)
    {
        _audit = audit;
        _executor = new LayoutImportExecutor(registry, audit, recoveryDirectory);
        _exporter = new LayoutExporter(store, audit);
    }

    /// <summary>The service over the real registry. It follows <c>InstanceController.appName</c>; tests build their own.</summary>
    public static LayoutService Default { get; } = new(HkcuRackRegistry.ForApp(), RackSettingsRepository.Default, AuditLog.Forwarder,
        Racks.Infrastructure.Layout.RecoveryFolder.DefaultDirectory());

    /// <summary>Imports a layout file: bounded read, parse, validate, then write (all or nothing).</summary>
    public ImportResult ImportFile(string path, bool replaceExisting)
    {
        try
        {
            var check = PathPolicy.ValidateFullPath(path);
            if (!check.Ok) return Rejected(path, $"The file path is not usable: {check.Reason}");
            return Import(LayoutParser.ReadFile(path), replaceExisting, path);
        }
        catch (Exception ex)
        {
            return Rejected(path, ex.Message);
        }
    }

    /// <summary>Imports layout text that is already in memory (the same checks as a file).</summary>
    public ImportResult ImportJson(string json, bool replaceExisting)
    {
        try
        {
            return Import(LayoutParser.Parse(json), replaceExisting, "(text)");
        }
        catch (Exception ex)
        {
            return Rejected("(text)", ex.Message);
        }
    }

    /// <summary>Exports the current racks to <paramref name="path"/>, atomically.</summary>
    public ExportResult ExportToFile(string path) => _exporter.WriteTo(path);

    private ImportResult Import(LayoutParseResult parsed, bool replaceExisting, string source)
    {
        if (!parsed.Ok) return Rejected(source, parsed.Error!);
        var validated = LayoutValidator.Validate(parsed.Document!);
        if (!validated.Ok) return Rejected(source, validated.Error!);
        var result = _executor.Execute(validated.Racks!, replaceExisting, source);
        if (result.Status != ImportStatus.Imported || validated.Dropped is not { Count: > 0 } dropped) return result;

        // The racks were imported, but some values were not used as written: say which, to the user and to the audit log.
        foreach (var value in dropped.Take(MaxDroppedRecords))
            LayoutAudit.Write(_audit, LayoutAudit.ValueDroppedKind, source, null, value.Reason, "Skipped", $"rack={value.Rack}; field={value.Field}");
        if (dropped.Count > MaxDroppedRecords)
            LayoutAudit.Write(_audit, LayoutAudit.ValueDroppedKind, source, null, $"{dropped.Count - MaxDroppedRecords} more values were not used", "Skipped", $"total={dropped.Count}");
        return result with { Dropped = dropped };
    }

    private const int MaxDroppedRecords = 50;

    private ImportResult Rejected(string source, string reason)
    {
        LayoutAudit.Write(_audit, LayoutAudit.ImportKind, source, null, reason, "Rejected", "the file was refused; nothing was changed");
        return ImportResult.Rejected(reason);
    }
}
