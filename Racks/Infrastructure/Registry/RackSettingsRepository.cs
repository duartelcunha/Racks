using System.Globalization;
using Microsoft.Win32;
using Racks.Infrastructure.Logging;

namespace Racks.Infrastructure.Registry;

/// <summary>One rack as the loader found it: the registry key it came from and its validated settings.</summary>
public sealed record LoadedRack(string KeyName, RackSettings Settings)
{
    /// <summary>
    /// True when the only things found were benign normalisations (a stored number clamped, no Name value): the caller may
    /// save the rack once in normal form with <see cref="IRackSettingsStore.Normalize"/>. Never true for a rack that had a refused value.
    /// </summary>
    public bool NeedsNormalization { get; init; }
}

/// <summary>What <see cref="IRackSettingsStore.LoadAll"/> found: the racks that can be loaded, and every value that was refused or changed on the way.</summary>
public sealed record RackLoadResult(IReadOnlyList<LoadedRack> Racks, IReadOnlyList<SettingIssue> Issues);

/// <summary>
/// Where a rack's settings live. <c>Instance</c> writes through <c>Instance.Store</c> (default:
/// <see cref="RackSettingsRepository.Default"/>); tests replace it. No member throws: a failure is audited and shown
/// by the returned value, so a registry problem never reaches the UI as an exception.
/// </summary>
public interface IRackSettingsStore
{
    /// <summary>Reads and validates every saved rack. One bad rack never stops the others.</summary>
    RackLoadResult LoadAll();

    /// <summary>The global style defaults a new rack starts from: only the values that were present and valid are set.</summary>
    RackSettings ReadGlobalStyleDefaults();

    /// <summary>Writes every persisted value of <paramref name="rack"/> (a full save, as <c>WriteInstanceToKey</c> always did).</summary>
    bool Save(Instance rack);

    /// <summary>
    /// One explicit full save of a rack whose only findings were benign normalisations, audited as <c>SettingNormalized</c>.
    /// Racks with refused values are never passed here: their raw data stays as the user left it.
    /// </summary>
    bool Normalize(Instance rack);

    /// <summary>Writes one value after it changed (the write-through of <c>Instance</c>).</summary>
    bool SaveField(Instance rack, string propertyName);

    /// <summary>Writes one value of a "Default Style" instance to the registry root (the global default). Ignores names that are not global.</summary>
    bool SaveGlobalDefault(Instance rack, string propertyName);

    /// <summary>Removes a rack's key with everything under it. False when it did not exist, the name is not safe, or the delete failed.</summary>
    bool Delete(string name);

    /// <summary>Removes the saved global style defaults (and nothing else under the root). Returns how many values were removed.</summary>
    int ResetGlobalStyleDefaults();
}

/// <summary>
/// The only place that reads and writes the per-rack registry values and the global style defaults (INV-REG-2).
/// <list type="bullet">
/// <item><b>Load</b>: <see cref="LoadAll"/> reads each rack key inside its own <c>try</c>, validates every value against
/// <see cref="RackSettingsSchema"/> and returns typed <see cref="RackSettings"/>. It never writes. Each refused or changed
/// value is one <c>SettingFallback</c> audit record; a merely missing value is not recorded.</item>
/// <item><b>Write</b>: value kinds are the ones every build has written (ints REG_DWORD, switches "True"/"False", lists as
/// before); doubles are written as invariant text from P6, whatever the user's culture. Readers accept both. A failed
/// write is a <c>SettingWriteFailed</c> record and a <c>false</c> result.</item>
/// <item><b>Global defaults</b>: <see cref="ReadGlobalStyleDefaults"/> validates each of the 32 values on its own; one bad
/// value does not skip the rest.</item>
/// </list>
/// </summary>
public sealed class RackSettingsRepository : IRackSettingsStore
{
    public const int MaxRawChars = 200;

    /// <summary>The repository over the real registry; follows <c>InstanceController.appName</c>.</summary>
    public static RackSettingsRepository Default { get; } = new(
        HkcuRackRegistry.ForApp(), HkcuRegistryRoot.ForApp(), AuditLog.Forwarder, OpenAppRootWritable);

    private readonly IRackRegistry _racks;
    private readonly IRegistryRoot _root;
    private readonly IAuditSink _audit;
    private readonly Func<RegistryKey?>? _openRootWritable;
    private readonly HashSet<string> _reportedGlobals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedRackIssues = new(StringComparer.Ordinal);
    private const int MaxRememberedIssues = 4096;

    public RackSettingsRepository(IRackRegistry racks, IRegistryRoot root, IAuditSink? audit = null, Func<RegistryKey?>? openRootWritable = null)
    {
        _racks = racks;
        _root = root;
        _audit = audit ?? AuditLog.Forwarder;
        _openRootWritable = openRootWritable;
    }

    private static RegistryKey? OpenAppRootWritable() =>
        global::Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\" + InstanceController.appName, writable: true);

    // ---- load ------------------------------------------------------------------------------------------------------

    public RackLoadResult LoadAll()
    {
        var racks = new List<LoadedRack>();
        var issues = new List<SettingIssue>();

        IReadOnlyList<string> keyNames;
        try
        {
            keyNames = _racks.GetRackKeyNames();
        }
        catch (Exception ex)
        {
            Report(new SettingIssue("(all racks)", "Instances", "(unreadable)", $"read failed: {ex.GetType().Name}: {ex.Message}", "no racks loaded", "Failed"), _racks.Path);
            return new RackLoadResult(racks, issues);
        }

        foreach (var keyName in keyNames)
        {
            var rackIssues = new List<SettingIssue>();
            try
            {
                var values = _racks.ReadValues(keyName);
                var settings = RackSettingsValidator.ValidateRack(keyName, values, rackIssues);
                if (settings != null)
                    racks.Add(new LoadedRack(keyName, settings) { NeedsNormalization = rackIssues.Count > 0 && rackIssues.All(i => i.Normalizes) });
            }
            catch (Exception ex)
            {
                rackIssues.Add(new SettingIssue(keyName, "(rack)", "(unreadable)", $"load failed: {ex.GetType().Name}: {ex.Message}",
                    "rack not loaded; its registry data is left untouched", "Failed"));
            }
            // The source is the real registry key, not the Name value (they can differ). A load no longer writes normalised
            // values back, so the same finding would repeat on every start and reload: once per process is enough.
            foreach (var issue in rackIssues)
                if (FirstTime(keyName + "|" + issue.Field + "|" + issue.Raw)) Report(issue, _racks.Path + @"\" + keyName + @"\" + issue.Field);
            issues.AddRange(rackIssues);
        }
        return new RackLoadResult(racks, issues);
    }

    public RackSettings ReadGlobalStyleDefaults()
    {
        var defaults = new RackSettings();
        foreach (var descriptor in RackSettingsSchema.GlobalDefaults)
        {
            try
            {
                var raw = _root.Read(descriptor.Name);
                if (raw == null) continue;
                var issues = new List<SettingIssue>();
                RackSettingsValidator.Apply(defaults, descriptor, raw.Value, RackSettingsValidator.GlobalDefaultsLabel, issues);
                foreach (var issue in issues) ReportGlobalOnce(issue);
            }
            catch (Exception ex)
            {
                ReportGlobalOnce(new SettingIssue(RackSettingsValidator.GlobalDefaultsLabel, descriptor.Name, "(unreadable)",
                    $"read failed: {ex.GetType().Name}: {ex.Message}", RackSettingsValidator.DefaultText(descriptor)));
            }
        }
        return defaults;
    }

    // ---- write -------------------------------------------------------------------------------------------------------

    public bool Save(Instance rack) => WriteRack(rack, RackSettingsSchema.Persisted.OrderBy(d => d.Name == RackSettingsSchema.NameField ? 0 : 1), bulk: true);

    public bool Normalize(Instance rack)
    {
        bool ok = Save(rack);
        if (!ok) return false;
        try
        {
            _audit.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, "SettingNormalized", _racks.Path + @"\" + rack.Name, null,
                "stored values were clamped or the Name was missing", "Succeeded", $"rack={rack.Name}; the rack was saved once in normal form"));
        }
        catch
        {
            // best-effort
        }
        return true;
    }

    public bool SaveField(Instance rack, string propertyName)
    {
        // A name the schema does not know is not stored (IsShortcutsOnly, SettingDefault). The Name is written by a full
        // save only: a rename is WriteOverInstanceToKey's job.
        if (!RackSettingsSchema.TryGet(propertyName, out var d) || !d.IsPersisted || d.Name == RackSettingsSchema.NameField) return true;
        return WriteRack(rack, new[] { d }, bulk: false);
    }

    private bool WriteRack(Instance rack, IEnumerable<SettingDescriptor> descriptors, bool bulk)
    {
        string? name = rack.Name;
        // A rack with no name has no key (a rack on a drive root has an empty name): there is nothing to write and nothing to report.
        if (string.IsNullOrEmpty(name)) return false;
        if (!RackSettingsSchema.IsValidRackName(name))
        {
            ReportWriteFailed(_racks.Path + @"\" + name, "unsafe rack name", $"rack={name}");
            return false;
        }

        bool ok = true;
        var writes = new List<RegistryWrite>();
        foreach (var d in descriptors)
        {
            if (!RackSettingsMapper.HasMember(d.Name)) continue;
            if (!RackSettingsWriter.TryFormat(d, RackSettingsMapper.Read(rack, d.Name), bulk, out var write, out string? problem))
            {
                ReportWriteFailed(_racks.Path + @"\" + name + @"\" + d.Name, problem ?? "not writable", $"rack={name}; field={d.Name}");
                ok = false;
                continue;
            }
            if (write != null) writes.Add(write.Value);
        }
        if (writes.Count == 0) return ok;

        try
        {
            _racks.Apply(name!, writes);
        }
        catch (Exception ex)
        {
            ReportWriteFailed(_racks.Path + @"\" + name, $"{ex.GetType().Name}: {ex.Message}", $"rack={name}; values={writes.Count}");
            return false;
        }
        return ok;
    }

    public bool SaveGlobalDefault(Instance rack, string propertyName)
    {
        if (!RackSettingsSchema.TryGet(propertyName, out var d) || !d.IsPersisted || Instance.NotGlobalProperties.Contains(d.Name)) return true;
        if (!RackSettingsMapper.HasMember(d.Name)) return true;
        if (!RackSettingsWriter.TryFormatGlobal(d, RackSettingsMapper.Read(rack, d.Name), out string? text, out string? problem))
        {
            if (problem != null) ReportWriteFailed(_root.Path + @"\" + d.Name, problem, $"field={d.Name}");
            return problem == null;
        }
        try
        {
            _root.Write(d.Name, text!, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            ReportWriteFailed(_root.Path + @"\" + d.Name, $"{ex.GetType().Name}: {ex.Message}", $"field={d.Name}");
            return false;
        }
    }

    public bool Delete(string name)
    {
        if (!RackSettingsSchema.IsValidRackName(name))
        {
            ReportWriteFailed(_racks.Path + @"\" + name, "unsafe rack name: nothing deleted", $"rack={name}");
            return false;
        }
        try
        {
            return _racks.DeleteKey(name);
        }
        catch (Exception ex)
        {
            ReportWriteFailed(_racks.Path + @"\" + name, $"{ex.GetType().Name}: {ex.Message}", $"rack={name}; delete");
            return false;
        }
    }

    public int ResetGlobalStyleDefaults()
    {
        if (_openRootWritable == null) return 0;
        try
        {
            using var key = _openRootWritable();
            return key == null ? 0 : Racks.Core.DefaultStyleReset.Run(key);
        }
        catch (Exception ex)
        {
            ReportWriteFailed(_root.Path, $"{ex.GetType().Name}: {ex.Message}", "reset of the global style defaults");
            return 0;
        }
    }

    // ---- audit ---------------------------------------------------------------------------------------------------------

    private bool FirstTime(string key)
    {
        lock (_reportedRackIssues)
        {
            if (_reportedRackIssues.Count >= MaxRememberedIssues) _reportedRackIssues.Clear();   // bounded; at worst a line repeats
            return _reportedRackIssues.Add(key);
        }
    }

    private void ReportGlobalOnce(SettingIssue issue)
    {
        // A new rack reads the defaults again each time, so a bad stored default would repeat; once per value and text is enough.
        lock (_reportedGlobals)
            if (!_reportedGlobals.Add(issue.Field + "|" + issue.Raw)) return;
        Report(issue, _root.Path + @"\" + issue.Field);
    }

    private void Report(SettingIssue issue, string source)
    {
        try
        {
            string detail = $"rack={issue.Rack}; field={issue.Field}; raw={Shorten(issue.Raw)}; fallback={issue.Fallback}";
            _audit.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, RegistryAudit.FallbackKind, source, null, issue.Reason, issue.Status, detail));
        }
        catch
        {
            // Auditing is best-effort; it must never turn a settings read into an error.
        }
    }

    private void ReportWriteFailed(string source, string reason, string detail)
    {
        try
        {
            _audit.Write(new AuditEvent(Guid.NewGuid().ToString("N"), DateTime.UtcNow, RegistryAudit.WriteFailedKind, source, null, reason, "Failed", detail));
        }
        catch
        {
            // best-effort
        }
    }

    internal static string Shorten(string text) => text.Length <= MaxRawChars ? text : text[..MaxRawChars] + "...";
}

/// <summary>How a value is written: the same stored forms every build used, doubles in invariant text.</summary>
internal static class RackSettingsWriter
{
    /// <summary>
    /// The registry write for one value. <paramref name="write"/> null with no <paramref name="problem"/> means "nothing to
    /// store" (a null text). A problem means the value cannot be stored (a non-finite number).
    /// <paramref name="bulk"/> keeps the two historic list rules: a full save leaves an empty or missing list value alone
    /// (<c>AssignedFiles</c> excepted: it is removed), the single-value write stores the empty list.
    /// </summary>
    public static bool TryFormat(SettingDescriptor d, object? value, bool bulk, out RegistryWrite? write, out string? problem)
    {
        write = null;
        problem = null;
        switch (d.Kind)
        {
            case SettingKind.Double:
                if (value is not double number) return true;
                if (!double.IsFinite(number)) { problem = "not a finite number: not stored"; return false; }
                write = RegistryWrite.Set(d.Name, number.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
                return true;

            case SettingKind.Int:
                if (value is int whole) write = RegistryWrite.Set(d.Name, whole, RegistryValueKind.DWord);
                return true;

            case SettingKind.Bool:
                if (value is bool flag) write = RegistryWrite.Set(d.Name, flag ? "True" : "False", RegistryValueKind.String);
                return true;

            case SettingKind.Enum:
                if (value != null) write = RegistryWrite.Set(d.Name, value.ToString()!, RegistryValueKind.String);
                return true;

            case SettingKind.String or SettingKind.Path or SettingKind.RackName or SettingKind.FontFamily or SettingKind.Color:
                string? text = value as string;
                if (text == null && d.Name is "AutoRouteRegex" or "BackgroundImagePath") text = "";
                if (text != null) write = RegistryWrite.Set(d.Name, text, RegistryValueKind.String);
                return true;

            case SettingKind.PipeList:
                write = value is List<string> { Count: > 0 } files
                    ? RegistryWrite.Set(d.Name, string.Join("|", files), RegistryValueKind.String)
                    : RegistryWrite.Remove(d.Name);
                return true;

            case SettingKind.IntList:
                if (value is int[] desktops) write = RegistryWrite.Set(d.Name, string.Join(",", desktops), RegistryValueKind.String);
                else if (!bulk) write = RegistryWrite.Set(d.Name, "", RegistryValueKind.String);
                return true;

            case SettingKind.MultiString:
                if (value is List<string> { Count: > 0 } lines) write = RegistryWrite.Set(d.Name, lines.ToArray(), RegistryValueKind.MultiString);
                else if (!bulk) write = RegistryWrite.Set(d.Name, Array.Empty<string>(), RegistryValueKind.MultiString);
                return true;

            case SettingKind.PairList:
                if (value is List<Tuple<string, string>> { Count: > 0 } pairs)
                    write = RegistryWrite.Set(d.Name, pairs.Select(t => $"{t.Item1},{t.Item2}").ToArray(), RegistryValueKind.MultiString);
                else if (!bulk) write = RegistryWrite.Set(d.Name, Array.Empty<string>(), RegistryValueKind.MultiString);
                return true;
        }
        return true;
    }

    /// <summary>
    /// A global default is plain REG_SZ text for every kind (what the "Default Style" instance has always written; older
    /// builds read the root with a text reader and would fail on a REG_DWORD). Lists are not global defaults and are not written.
    /// </summary>
    public static bool TryFormatGlobal(SettingDescriptor d, object? value, out string? text, out string? problem)
    {
        text = null;
        problem = null;
        switch (value)
        {
            case null: return false;
            case double number:
                if (!double.IsFinite(number)) { problem = "not a finite number: not stored"; return false; }
                text = number.ToString(CultureInfo.InvariantCulture);
                return true;
            case int whole: text = whole.ToString(CultureInfo.InvariantCulture); return true;
            case bool flag: text = flag ? "True" : "False"; return true;
            case string s: text = s; return true;
            case Enum e: text = e.ToString(); return true;
            default: return false;   // lists
        }
    }
}
