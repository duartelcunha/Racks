using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// Writes already validated racks (<see cref="ValidatedRack"/>) through <see cref="IRackRegistry"/> (INV-IMPORT-2). Nothing
/// about the file is judged here: the racks come from <see cref="LayoutValidator"/>, so nothing touches the registry before
/// validation completed.
/// <list type="number">
/// <item><b>Snapshot</b> the keys about to change (replace mode: every rack key; merge mode: the keys named in the file), with
/// every value's kind and data.</item>
/// <item>Replace mode only: <b>delete</b> every existing rack key.</item>
/// <item><b>Write</b> each rack (the stored forms of <see cref="ValidatedRack.Writes"/>).</item>
/// <item><b>Verify</b>: read every written rack back and compare it with what was written.</item>
/// </list>
/// A failure in 2, 3 or 4 restores the snapshot (delete what is there, write the old values back with their kinds) and
/// then CHECKS the restore. A restore that cannot finish is never swallowed: the result carries
/// <see cref="ImportResult.RollbackFailed"/> and what went wrong, an audit record <see cref="LayoutAudit.RollbackFailedKind"/> is
/// written, and the caller tells the user. Nothing here throws.
/// </summary>
public sealed class LayoutImportExecutor
{
    private readonly IRackRegistry _registry;
    private readonly IAuditSink _audit;

    public LayoutImportExecutor(IRackRegistry registry, IAuditSink audit)
    {
        _registry = registry;
        _audit = audit;
    }

    /// <param name="source">Where the layout came from, for the audit record (a file path).</param>
    public ImportResult Execute(IReadOnlyList<ValidatedRack> racks, bool replaceExisting, string source = "")
    {
        if (racks.Count == 0) return ImportResult.Imported(0);   // nothing to write: nothing is touched, even in replace mode
        string mode = replaceExisting ? "replace" : "merge";

        Snapshot snapshot;
        try
        {
            snapshot = TakeSnapshot(racks, replaceExisting);
        }
        catch (Exception ex)
        {
            var failed = new ImportResult(ImportStatus.Failed, 0, $"The current racks could not be read: {ex.Message}");
            Record(source, failed, $"mode={mode}; nothing was changed");
            return failed;
        }

        try
        {
            if (replaceExisting)
                foreach (var key in snapshot.Values.Keys) _registry.DeleteKey(key);
            foreach (var rack in racks) _registry.Apply(rack.Name, rack.Writes);
            Verify(racks);
        }
        catch (Exception ex)
        {
            return Roll(snapshot, racks, replaceExisting, ex, source, mode);
        }

        LayoutAudit.Write(_audit, LayoutAudit.ImportKind, source, _registry.Path, "", "Succeeded", $"mode={mode}; racks={racks.Count}");
        return ImportResult.Imported(racks.Count);
    }

    // ---- snapshot ------------------------------------------------------------------------------------------------------

    private sealed record Snapshot(IReadOnlyList<string> AllKeys, Dictionary<string, IReadOnlyDictionary<string, RegistryValue>> Values);

    private Snapshot TakeSnapshot(IReadOnlyList<ValidatedRack> racks, bool replaceExisting)
    {
        var all = _registry.GetRackKeyNames();
        var wanted = new HashSet<string>(racks.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, IReadOnlyDictionary<string, RegistryValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in all)
            if (replaceExisting || wanted.Contains(key))
                values[key] = new Dictionary<string, RegistryValue>(_registry.ReadValuesExact(key), StringComparer.OrdinalIgnoreCase);
        return new Snapshot(all.ToList(), values);
    }

    // ---- verify --------------------------------------------------------------------------------------------------------

    private void Verify(IReadOnlyList<ValidatedRack> racks)
    {
        foreach (var rack in racks)
        {
            var stored = _registry.ReadValuesExact(rack.Name);
            foreach (var write in rack.Writes)
            {
                bool has = stored.TryGetValue(write.Name, out var actual);
                if (write.IsRemoval)
                {
                    if (has) throw new InvalidOperationException($"The registry kept '{write.Name}' of rack '{rack.Name}' although it should be empty.");
                }
                else if (!has || actual.Kind != write.Kind || !SameData(write.Data, actual.Data))
                {
                    throw new InvalidOperationException($"The registry did not keep '{write.Name}' of rack '{rack.Name}' as written.");
                }
            }
        }
    }

    private static bool SameData(object? expected, object? actual) => (expected, actual) switch
    {
        (string[] a, string[] b) => a.SequenceEqual(b),
        (byte[] a, byte[] b) => a.SequenceEqual(b),
        _ => Equals(expected, actual),
    };

    // ---- rollback ------------------------------------------------------------------------------------------------------

    private ImportResult Roll(Snapshot snapshot, IReadOnlyList<ValidatedRack> racks, bool replaceExisting, Exception cause, string source, string mode)
    {
        // Every key the import may have changed: all that existed (replace mode) plus every key it wrote.
        var touched = new HashSet<string>(racks.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        if (replaceExisting) touched.UnionWith(snapshot.Values.Keys);

        var problems = new List<string>();
        foreach (var key in touched)
        {
            try { _registry.DeleteKey(key); }
            catch (Exception ex) { problems.Add($"could not remove '{key}': {ex.Message}"); }
        }
        foreach (var (key, values) in snapshot.Values)
        {
            try
            {
                var writes = values.Select(v => RegistryWrite.Set(v.Key, v.Value.Data, v.Value.Kind)).ToList();
                _registry.Apply(key, writes);
            }
            catch (Exception ex) { problems.Add($"could not restore '{key}': {ex.Message}"); }
        }
        problems.AddRange(CheckRestore(snapshot, touched));

        string details = string.Join("; ", problems);
        bool restoreFailed = problems.Count > 0;
        var result = new ImportResult(ImportStatus.Failed, 0, cause.Message, StateChanged: true,
            RolledBack: !restoreFailed, RollbackFailed: restoreFailed, RollbackDetails: details);

        Record(source, result, $"mode={mode}; rolledBack={!restoreFailed}");
        if (restoreFailed)
            LayoutAudit.Write(_audit, LayoutAudit.RollbackFailedKind, source, _registry.Path,
                "the previous racks could not be fully restored after a failed import", "Failed", $"mode={mode}; cause={cause.Message}; {Shorten(details)}");
        return result;
    }

    // Reads the registry again and compares it with the snapshot: the same keys, the same values with the same kinds.
    private IEnumerable<string> CheckRestore(Snapshot snapshot, HashSet<string> touched)
    {
        var problems = new List<string>();
        try
        {
            var now = new HashSet<string>(_registry.GetRackKeyNames(), StringComparer.OrdinalIgnoreCase);
            var before = new HashSet<string>(snapshot.AllKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in before.Except(now)) problems.Add($"rack '{key}' is missing after the restore");
            foreach (var key in now.Except(before)) problems.Add($"rack '{key}' was left behind by the import");

            foreach (var (key, expected) in snapshot.Values)
            {
                if (!now.Contains(key)) continue;
                var actual = _registry.ReadValuesExact(key);
                if (actual.Count != expected.Count)
                    problems.Add($"rack '{key}' has {actual.Count} value(s) after the restore, expected {expected.Count}");
                foreach (var (name, want) in expected)
                    if (!actual.TryGetValue(name, out var got) || got.Kind != want.Kind || !SameData(want.Data, got.Data))
                        problems.Add($"rack '{key}': '{name}' was not restored exactly");
            }
        }
        catch (Exception ex)
        {
            problems.Add($"the restore could not be checked: {ex.Message}");
        }
        return problems;
    }

    private void Record(string source, ImportResult result, string detail) =>
        LayoutAudit.Write(_audit, LayoutAudit.ImportKind, source, _registry.Path, result.Message, "Failed", detail);

    private static string Shorten(string text) => text.Length <= 600 ? text : text[..600] + "...";
}
