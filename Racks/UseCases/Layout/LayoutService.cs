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

    public LayoutService(IRackRegistry registry, IRackSettingsStore store, IAuditSink audit)
    {
        _audit = audit;
        _executor = new LayoutImportExecutor(registry, audit);
        _exporter = new LayoutExporter(store, audit);
    }

    /// <summary>The service over the real registry. It follows <c>InstanceController.appName</c>; tests build their own.</summary>
    public static LayoutService Default { get; } = new(HkcuRackRegistry.ForApp(), RackSettingsRepository.Default, AuditLog.Forwarder);

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
        return _executor.Execute(validated.Racks!, replaceExisting, source);
    }

    private ImportResult Rejected(string source, string reason)
    {
        LayoutAudit.Write(_audit, LayoutAudit.ImportKind, source, null, reason, "Rejected", "the file was refused; nothing was changed");
        return ImportResult.Rejected(reason);
    }
}
