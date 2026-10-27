using System.Globalization;
using Racks.Util;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// One value that was refused, changed or dropped on its way from the registry to the model, or one rack that was
/// skipped. Each becomes exactly one <c>SettingFallback</c> audit record. A value that is merely missing is not an issue.
/// </summary>
/// <param name="Rack">The rack name, or "(global defaults)".</param>
/// <param name="Field">The value name.</param>
/// <param name="Raw">The stored value as text (shortened when audited).</param>
/// <param name="Reason">Why it was not used as stored.</param>
/// <param name="Fallback">What applies instead.</param>
/// <param name="Status">Rejected (a fallback applies), or Skipped (the whole rack was not loaded).</param>
/// <param name="Normalizes">True for the two benign cases (a stored number clamped, a missing Name): the stored value is not wrong data, so the rack may be saved once in normal form.</param>
public sealed record SettingIssue(string Rack, string Field, string Raw, string Reason, string Fallback, string Status = "Rejected", bool Normalizes = false);

public enum ParseStatus
{
    /// <summary>Usable as stored.</summary>
    Accepted,
    /// <summary>Usable after a change (clamped, a list item dropped): <see cref="ParseResult.Reason"/> says what.</summary>
    Changed,
    /// <summary>Same as a missing value (an empty font or color): nothing to apply, nothing to report.</summary>
    Absent,
    /// <summary>Not usable. The default applies.</summary>
    Rejected,
}

public readonly record struct ParseResult(ParseStatus Status, object? Value = null, string Reason = "", string Fallback = "", bool Normalizes = false);

/// <summary>
/// Strict parsing of stored rack values (INV-REG-2). Every value passes through <see cref="Parse"/> before it can
/// reach the runtime model; whatever is not usable is reported and replaced by the default, never applied.
/// <list type="bullet">
/// <item><b>Numbers</b> are read from invariant text first (what this build writes), then from the current culture's
/// text (what older builds wrote on a comma-decimal machine), REG_DWORD or REG_QWORD (an import). NaN, Infinity and
/// overflow are refused; a limit from <see cref="RackSettingsSchema"/> refuses or clamps as documented there.</item>
/// <item><b>Switches</b> are <c>bool.TryParse</c> text, as before ("1" is not a switch).</item>
/// <item><b>Enums</b> must name a defined member.</item>
/// <item><b>Text</b> is taken as stored; fonts and colors must pass <see cref="SafeWpfValues"/>; paths and the rack name
/// must be sane (no control characters; one safe segment).</item>
/// <item><b>Lists</b> keep their good items: a bad item is dropped and reported once for the value. A list with no good
/// item at all is refused.</item>
/// </list>
/// </summary>
public static class RackSettingsValidator
{
    public const string GlobalDefaultsLabel = "(global defaults)";

    /// <summary>
    /// Validates every value of one rack key. Returns the typed settings, or null when the rack is skipped (its name is
    /// not a safe single segment). A missing or empty Name value falls back to the key name (reported).
    /// </summary>
    public static RackSettings? ValidateRack(string keyName, IReadOnlyDictionary<string, RegistryValue> values, List<SettingIssue> issues)
    {
        string? name = ResolveName(keyName, values, issues);
        if (name == null) return null;

        var settings = new RackSettings { Name = name };
        foreach (var (valueName, raw) in values)
        {
            if (!RackSettingsSchema.TryGet(valueName, out var descriptor)) continue;       // unknown names: ignored, as ever
            if (!descriptor.IsPersisted || descriptor.Name == RackSettingsSchema.NameField) continue;   // legacy: read and discarded
            Apply(settings, descriptor, raw, name, issues);
        }
        return settings;
    }

    /// <summary>Parses one value into <paramref name="settings"/>, reporting a refusal or a change.</summary>
    public static void Apply(RackSettings settings, SettingDescriptor descriptor, RegistryValue raw, string rack, List<SettingIssue> issues)
    {
        var result = Parse(descriptor, raw);
        switch (result.Status)
        {
            case ParseStatus.Accepted:
                settings.Set(descriptor.Name, result.Value);
                break;
            case ParseStatus.Changed:
                settings.Set(descriptor.Name, result.Value);
                issues.Add(new SettingIssue(rack, descriptor.Name, Describe(raw), result.Reason, result.Fallback, Normalizes: result.Normalizes));
                break;
            case ParseStatus.Rejected:
                issues.Add(new SettingIssue(rack, descriptor.Name, Describe(raw), result.Reason, DefaultText(descriptor)));
                break;
        }
    }

    // ---- identity ------------------------------------------------------------------------------------------------------

    private static string? ResolveName(string keyName, IReadOnlyDictionary<string, RegistryValue> values, List<SettingIssue> issues)
    {
        RegistryValue? stored = null;
        foreach (var (valueName, raw) in values)
            if (string.Equals(valueName, RackSettingsSchema.NameField, StringComparison.OrdinalIgnoreCase)) { stored = raw; break; }

        string? fallbackReason = null;
        if (stored == null) fallbackReason = "no Name value";
        else if (!TryText(stored.Value, out string text)) fallbackReason = $"wrong value type ({stored.Value.Kind})";
        else if (string.IsNullOrWhiteSpace(text)) fallbackReason = "the Name value is empty";
        else if (!RackSettingsSchema.IsValidRackName(text))
        {
            issues.Add(new SettingIssue(keyName, RackSettingsSchema.NameField, Describe(stored.Value),
                "not a safe rack name (one segment, no '\\' or '/', not '.' or '..', at most 255 characters)",
                "rack not loaded; its registry data is left untouched", "Skipped"));
            return null;
        }
        else
        {
            // The registry compares key names without regard to case: a case-only rename keeps the old key spelling.
            if (!string.Equals(text, keyName, StringComparison.OrdinalIgnoreCase))
                issues.Add(new SettingIssue(text, RackSettingsSchema.NameField, keyName,
                    "the Name value differs from the registry key it is stored in", "the Name value is used"));
            return text;
        }

        if (!RackSettingsSchema.IsValidRackName(keyName))
        {
            issues.Add(new SettingIssue(keyName, RackSettingsSchema.NameField, stored == null ? "(missing)" : Describe(stored.Value),
                fallbackReason + ", and the key name is not a safe rack name", "rack not loaded; its registry data is left untouched", "Skipped"));
            return null;
        }
        bool benign = stored == null || fallbackReason == "the Name value is empty";
        issues.Add(new SettingIssue(keyName, RackSettingsSchema.NameField, stored == null ? "(missing)" : Describe(stored.Value),
            fallbackReason, "the registry key name is used", Normalizes: benign));
        return keyName;
    }

    // ---- one value -------------------------------------------------------------------------------------------------------

    public static ParseResult Parse(SettingDescriptor d, RegistryValue raw) => d.Kind switch
    {
        SettingKind.Double => ParseDouble(d, raw),
        SettingKind.Int => ParseInt(d, raw),
        SettingKind.Bool => ParseBool(raw),
        SettingKind.Enum => ParseEnum(d, raw),
        SettingKind.String => ParseText(raw, rejectControl: false),
        SettingKind.Path => ParseText(raw, rejectControl: true),
        SettingKind.RackName => ParseRackName(raw),
        SettingKind.FontFamily => ParseSafe(raw, SafeWpfValues.IsSafeFontFamily, "not a plain font family name"),
        SettingKind.Color => ParseSafe(raw, SafeWpfValues.IsSafeColor, "not a plain color"),
        SettingKind.MultiString => ParseLines(raw),
        SettingKind.PairList => ParsePairs(raw),
        SettingKind.PipeList => ParsePipeList(raw),
        SettingKind.IntList => ParseIntList(raw),
        _ => new ParseResult(ParseStatus.Absent),
    };

    private static ParseResult ParseDouble(SettingDescriptor d, RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        if (!TryParseDouble(text, out double v)) return Rejected("not a number");
        if (!double.IsFinite(v)) return Rejected("not a finite number");
        if (d.Range == RangePolicy.Reject && (v < d.Min || v > d.Max))
            return Rejected($"out of range ({Num(d.Min)} to {Num(d.Max)})");
        return new ParseResult(ParseStatus.Accepted, v);
    }

    private static ParseResult ParseInt(SettingDescriptor d, RegistryValue raw)
    {
        long v;
        if (raw.Data is int i) v = i;
        else if (raw.Data is long l) v = l;
        else if (!TryText(raw, out string text)) return WrongType(raw);
        else if (!TryParseInt(text, out v)) return Rejected("not a whole number");

        if (v < int.MinValue || v > int.MaxValue) return Rejected("not a 32-bit whole number");
        if (d.Range == RangePolicy.None) return new ParseResult(ParseStatus.Accepted, (int)v);
        if (v >= d.Min && v <= d.Max) return new ParseResult(ParseStatus.Accepted, (int)v);
        if (d.Range == RangePolicy.Reject) return Rejected($"out of range ({Num(d.Min)} to {Num(d.Max)})");
        int clamped = (int)Math.Clamp(v, d.Min, d.Max);
        return new ParseResult(ParseStatus.Changed, clamped, $"out of range ({Num(d.Min)} to {Num(d.Max)})", $"clamped to {clamped}", Normalizes: true);
    }

    private static ParseResult ParseBool(RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        return bool.TryParse(text, out bool b)
            ? new ParseResult(ParseStatus.Accepted, b)
            : Rejected("not True or False");
    }

    private static ParseResult ParseEnum(SettingDescriptor d, RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        // Enum.TryParse also reads "Left, Right" as a combination of members; a stored value is one member.
        if (d.EnumType != null && !text.Contains(',') && Enum.TryParse(d.EnumType, text, ignoreCase: false, out object? value)
            && value != null && Enum.IsDefined(d.EnumType, value))
            return new ParseResult(ParseStatus.Accepted, value);
        return Rejected("not a defined value");
    }

    private static ParseResult ParseText(RegistryValue raw, bool rejectControl)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        if (text.Length > RackSettingsSchema.MaxTextLength) return Rejected($"longer than {RackSettingsSchema.MaxTextLength} characters");
        if (rejectControl && text.Any(c => c < 0x20)) return Rejected("contains a control character");
        return new ParseResult(ParseStatus.Accepted, text);
    }

    private static ParseResult ParseRackName(RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        return RackSettingsSchema.IsValidRackName(text)
            ? new ParseResult(ParseStatus.Accepted, text)
            : Rejected("not a safe rack name");
    }

    private static ParseResult ParseSafe(RegistryValue raw, Func<string?, bool> isSafe, string reason)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        // An empty font or color is what a cleared settings box leaves: the default stays, silently (as the layout import does).
        if (text.Length == 0) return new ParseResult(ParseStatus.Absent);
        return isSafe(text) ? new ParseResult(ParseStatus.Accepted, text) : Rejected(reason);
    }

    private static ParseResult ParseLines(RegistryValue raw)
    {
        if (raw.Data is not string[] lines) return WrongType(raw);
        return new ParseResult(ParseStatus.Accepted, lines.Where(l => l != null).ToList());
    }

    private static ParseResult ParsePairs(RegistryValue raw)
    {
        if (raw.Data is not string[] lines) return WrongType(raw);
        var pairs = new List<Tuple<string, string>>();
        int dropped = 0;
        foreach (var line in lines)
        {
            var parts = (line ?? "").Split(',');
            if (parts.Length < 2) { dropped++; continue; }
            pairs.Add(Tuple.Create(parts[0], parts[1]));
        }
        return dropped == 0
            ? new ParseResult(ParseStatus.Accepted, pairs)
            : new ParseResult(ParseStatus.Changed, pairs, $"{dropped} of {lines.Length} item(s) have no ',' and were dropped", $"kept {pairs.Count} item(s)");
    }

    private static ParseResult ParsePipeList(RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        return new ParseResult(ParseStatus.Accepted, text.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    private static ParseResult ParseIntList(RegistryValue raw)
    {
        if (!TryText(raw, out string text)) return WrongType(raw);
        var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>();
        int dropped = 0;
        foreach (var part in parts)
        {
            if (TryParseInt(part, out long n) && n >= int.MinValue && n <= int.MaxValue) numbers.Add((int)n);
            else dropped++;
        }
        if (dropped == 0) return new ParseResult(ParseStatus.Accepted, numbers.ToArray());
        if (numbers.Count == 0) return Rejected("no item is a whole number");
        return new ParseResult(ParseStatus.Changed, numbers.ToArray(), $"{dropped} of {parts.Length} item(s) are not whole numbers and were dropped", $"kept {numbers.Count} item(s)");
    }

    // ---- primitives ------------------------------------------------------------------------------------------------------

    /// <summary>The value as text when it is text or a number; false for binary and REG_MULTI_SZ data.</summary>
    internal static bool TryText(RegistryValue value, out string text)
    {
        switch (value.Data)
        {
            case string s: text = s; return true;
            case int i: text = i.ToString(CultureInfo.InvariantCulture); return true;
            case long l: text = l.ToString(CultureInfo.InvariantCulture); return true;
            default: text = ""; return false;
        }
    }

    /// <summary>Invariant text first (what this build writes), then the current culture (what an older build wrote there).</summary>
    internal static bool TryParseDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);

    internal static bool TryParseInt(string text, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
           || long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private static ParseResult Rejected(string reason) => new(ParseStatus.Rejected, null, reason);

    private static ParseResult WrongType(RegistryValue raw) => Rejected($"wrong value type ({raw.Kind})");

    private static string Num(double d) => d.ToString(CultureInfo.InvariantCulture);

    internal static string DefaultText(SettingDescriptor d) => d.Default switch
    {
        null => d.Kind is SettingKind.PipeList or SettingKind.MultiString or SettingKind.PairList ? "empty list" : "the built-in default",
        string s => $"default \"{s}\"",
        bool b => $"default {(b ? "True" : "False")}",
        double v => "default " + v.ToString(CultureInfo.InvariantCulture),
        _ => "default " + Convert.ToString(d.Default, CultureInfo.InvariantCulture),
    };

    /// <summary>The stored value as text for the audit log (kind and content; the caller shortens it).</summary>
    internal static string Describe(RegistryValue value) => value.Data switch
    {
        string s => s,
        string[] lines => "[" + string.Join(" | ", lines) + "]",
        byte[] bytes => $"({bytes.Length} bytes)",
        _ => Convert.ToString(value.Data, CultureInfo.InvariantCulture) ?? "",
    };
}
