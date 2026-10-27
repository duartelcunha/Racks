using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Registry;

namespace Racks.UseCases.Layout;

/// <summary>
/// Writes already validated racks (<see cref="ValidatedRack"/>) through <see cref="IRackRegistry"/> (INV-IMPORT-2). Nothing
/// about the file is judged here: the racks come from <see cref="LayoutValidator"/>, so nothing touches the registry before
/// validation completed.
/// <list type="number">
/// <item><b>Snapshot</b> the keys about to change (replace mode: every rack key; merge mode: the keys named in the file), with
/// every value's kind and data (exact, REG_EXPAND_SZ unexpanded).</item>
/// <item>Replace mode only: <b>delete</b> every existing rack key.</item>
/// <item><b>Write</b> each rack (the stored forms of <see cref="ValidatedRack.Writes"/>).</item>
/// <item><b>Verify</b>: read every written rack back and compare it with what was written.</item>
/// </list>
/// A failure in 2, 3 or 4 rolls back. The rollback is PRECISE: it only touches the keys the run actually reached (each key is
/// recorded just before its delete or write is attempted), so a failure at the first delete leaves every other rack exactly as
/// it was. A reached key that already equals its snapshot is left alone; the others are deleted and written back from the
/// snapshot with their kinds. Then the restore is CHECKED against the snapshot. A restore that cannot finish is never swallowed:
/// the result carries <see cref="ImportResult.RollbackFailed"/> and what went wrong, an audit record
/// <see cref="LayoutAudit.RollbackFailedKind"/> is written, and because the in-memory snapshot is then the user's only copy of
/// their racks it is ALWAYS saved as a recovery layout file (<see cref="LayoutRecovery"/>, newest 5 kept) whose path (or the
/// reason it could not be written) is in the result and the audit record.
/// <para>Limits, stated plainly: the snapshot and the restore check cover the VALUES of the rack keys. A rack key may in theory
/// have nested subkeys; the app writes none, the snapshot does not copy them and <c>CheckRestore</c> does not compare them. A
/// recovery file holds what a layout file can hold (no binary values, no names Racks does not know). Nothing here throws.</para>
/// </summary>
public sealed class LayoutImportExecutor
{
    private readonly IRackRegistry _registry;
    private readonly IAuditSink _audit;
    private readonly string _recoveryDirectory;

    /// <param name="recoveryDirectory">Where the recovery file goes when a rollback fails (<see cref="Racks.Infrastructure.Layout.RecoveryFolder.DefaultDirectory"/> in the app; a temp folder in tests).</param>
    public LayoutImportExecutor(IRackRegistry registry, IAuditSink audit, string recoveryDirectory)
    {
        _registry = registry;
        _audit = audit;
        _recoveryDirectory = recoveryDirectory;
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

        // Every key is recorded BEFORE its delete or write is attempted, so a failure leaves a precise list of what may have changed.
        var touched = new List<string>();
        try
        {
            if (replaceExisting)
                foreach (var key in snapshot.Values.Keys)
                {
                    touched.Add(key);
                    _registry.DeleteKey(key);
                }
            foreach (var rack in racks)
            {
                if (!touched.Contains(rack.Name, StringComparer.OrdinalIgnoreCase)) touched.Add(rack.Name);
                _registry.Apply(rack.Name, rack.Writes);
            }
            Verify(racks);
        }
        catch (Exception ex)
        {
            return Roll(snapshot, touched, ex, source, mode);
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

    private ImportResult Roll(Snapshot snapshot, List<string> touched, Exception cause, string source, string mode)
    {
        var problems = new List<string>();
        HashSet<string> existing;
        try
        {
            existing = new HashSet<string>(_registry.GetRackKeyNames(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Cannot even list: assume every key the run reached exists, and let the check below say what it finds.
            existing = new HashSet<string>(snapshot.AllKeys.Concat(touched), StringComparer.OrdinalIgnoreCase);
            problems.Add($"the rack list could not be read: {ex.Message}");
        }

        foreach (var key in touched)
        {
            snapshot.Values.TryGetValue(key, out var wanted);
            try
            {
                if (IsAsWanted(key, wanted, existing)) continue;   // the failed step never changed this key
            }
            catch (Exception ex)
            {
                problems.Add($"could not read '{key}': {ex.Message}");
                continue;
            }

            if (existing.Contains(key))
            {
                try { _registry.DeleteKey(key); }
                catch (Exception ex) { problems.Add($"could not remove '{key}': {ex.Message}"); }
            }
            if (wanted == null) continue;   // it did not exist before: removed is right
            try
            {
                _registry.Apply(key, wanted.Select(v => RegistryWrite.Set(v.Key, v.Value.Data, v.Value.Kind)).ToList());
            }
            catch (Exception ex) { problems.Add($"could not restore '{key}': {ex.Message}"); }
        }
        problems.AddRange(CheckRestore(snapshot, touched));

        string details = string.Join("; ", problems);
        bool restoreFailed = problems.Count > 0;
        string? recoveryFile = null, recoveryError = null;
        if (restoreFailed)
            (recoveryFile, recoveryError) = LayoutRecovery.Save(_recoveryDirectory, snapshot.Values);

        var result = new ImportResult(ImportStatus.Failed, 0, cause.Message, StateChanged: true,
            RolledBack: !restoreFailed, RollbackFailed: restoreFailed, RollbackDetails: details,
            RecoveryFile: recoveryFile, RecoveryError: recoveryError);

        Record(source, result, $"mode={mode}; rolledBack={!restoreFailed}");
        if (restoreFailed)
            LayoutAudit.Write(_audit, LayoutAudit.RollbackFailedKind, source, _registry.Path,
                "the previous racks could not be fully restored after a failed import", "Failed",
                $"mode={mode}; cause={cause.Message}; {Shorten(details)}; "
                + (recoveryFile != null ? $"recoveryFile={recoveryFile}" : $"recoveryFile=NOT WRITTEN ({recoveryError})"));
        return result;
    }

    // Does this key already hold exactly what the snapshot says (or not exist, when it did not before)?
    private bool IsAsWanted(string key, IReadOnlyDictionary<string, RegistryValue>? wanted, HashSet<string> existing)
    {
        if (wanted == null) return !existing.Contains(key);
        if (!existing.Contains(key)) return false;
        var actual = _registry.ReadValuesExact(key);
        if (actual.Count != wanted.Count) return false;
        foreach (var (name, want) in wanted)
            if (!actual.TryGetValue(name, out var got) || got.Kind != want.Kind || !SameData(want.Data, got.Data)) return false;
        return true;
    }

    // Reads the registry again and compares the keys the run reached with the snapshot: present or absent as before, the same
    // values with the same kinds. (Keys the run never reached are not looked at; nested subkeys are not compared.)
    private IEnumerable<string> CheckRestore(Snapshot snapshot, List<string> touched)
    {
        var problems = new List<string>();
        try
        {
            var now = new HashSet<string>(_registry.GetRackKeyNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var key in touched)
            {
                if (!snapshot.Values.TryGetValue(key, out var expected))
                {
                    if (now.Contains(key)) problems.Add($"rack '{key}' was left behind by the import");
                    continue;
                }
                if (!now.Contains(key)) { problems.Add($"rack '{key}' is missing after the restore"); continue; }
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
