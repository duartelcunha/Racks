using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Racks.Infrastructure.Layout;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// Builds the layout file from the validated racks of the settings store and writes it atomically
/// (<see cref="LayoutFileWriter"/>). The JSON has the shape every earlier build wrote (<see cref="LayoutDocument"/>): single
/// values under <c>Values</c> in their stored form (numbers as numbers, decimals as invariant text, switches as
/// <c>True</c>/<c>False</c>), lists under <c>MultiStringValues</c>, so an old export imports and a new one imports in an old
/// build. A value the loader refuses is not exported (it would be refused on import too). Nothing here throws for a bad
/// destination: the result says what went wrong and the destination is untouched.
/// </summary>
public sealed class LayoutExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IRackSettingsStore _store;
    private readonly IAuditSink _audit;

    public LayoutExporter(IRackSettingsStore store, IAuditSink audit)
    {
        _store = store;
        _audit = audit;
    }

    /// <summary>The layout document of the racks as the loader reads them now.</summary>
    public LayoutDocument Build()
    {
        var document = new LayoutDocument { Version = LayoutLimits.CurrentVersion, ExportedBy = "Racks", ExportedAt = DateTime.UtcNow, Racks = new() };
        foreach (var loaded in _store.LoadAll().Racks)
        {
            // The Name value is the rack's identity (the loader uses it, not the key), so key and Name in the file always agree.
            string name = loaded.Settings.Name ?? loaded.KeyName;
            var rack = new LayoutDocumentRack { Name = name, Values = new(), MultiStringValues = new() };
            foreach (var d in RackSettingsSchema.Persisted)
            {
                object? value = d.Name == RackSettingsSchema.NameField ? name : loaded.Settings.Get(d.Name);
                if (value == null) continue;
                if (!RackSettingsWriter.TryFormat(d, value, bulk: false, out var write, out _) || write == null || write.Value.IsRemoval) continue;
                if (write.Value.Kind == RegistryValueKind.MultiString) rack.MultiStringValues[d.Name] = (string[])write.Value.Data!;
                else rack.Values[d.Name] = write.Value.Data;
            }
            document.Racks.Add(rack);
        }
        return document;
    }

    public string BuildJson() => JsonSerializer.Serialize(Build(), Json);

    /// <summary>Writes the layout to <paramref name="path"/> in one atomic step. Never throws.</summary>
    public ExportResult WriteTo(string path)
    {
        ExportResult result;
        try
        {
            var document = Build();
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(document, Json));
            int count = document.Racks!.Count;
            if (count > LayoutLimits.MaxRacks || bytes.Length > LayoutLimits.MaxFileBytes)
            {
                // A file this build would refuse to read back is not a backup.
                result = ExportResult.Fail($"The layout is too large to export: Racks reads at most {LayoutLimits.MaxRacks} racks and {LayoutLimits.MaxFileBytes / (1024 * 1024)} MB.");
            }
            else
            {
                var written = LayoutFileWriter.Write(path, bytes);
                result = written.Ok ? ExportResult.Done(count) : ExportResult.Fail(written.Error);
            }
        }
        catch (Exception ex)
        {
            result = ExportResult.Fail(ex.Message);
        }
        LayoutAudit.Write(_audit, LayoutAudit.ExportKind, "racks", path, result.Message, result.Ok ? "Succeeded" : "Failed", $"racks={result.RackCount}");
        return result;
    }
}
