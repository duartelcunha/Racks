using System.Reflection;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// Moves values between <see cref="RackSettings"/> and an <c>Instance</c>. Values are set through the public members of
/// <c>Instance</c>, so its safe setters (<c>SafeWpfValues</c> for fonts and colors, the <c>IconSize</c> and <c>GridSize</c>
/// clamps) still run; <see cref="Apply"/> and <see cref="ToInstance"/> suspend persistence, so putting loaded values on
/// a rack NEVER writes to the registry. The members are found by name from <see cref="RackSettingsSchema"/> once
/// (properties, and the two public fields <c>IsDesktopFilterRack</c> and <c>AssignedFiles</c>); the schema test fails if a
/// descriptor has no member of the right type.
/// </summary>
public static class RackSettingsMapper
{
    private sealed class Member
    {
        public required Type Type { get; init; }
        public required Func<object, object?> Get { get; init; }
        public required Action<object, object?> Set { get; init; }
    }

    private static readonly Dictionary<string, Member> s_members = BuildMembers();

    private static Dictionary<string, Member> BuildMembers()
    {
        var map = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        foreach (var d in RackSettingsSchema.Persisted)
        {
            var property = typeof(Instance).GetProperty(d.Name, Flags);
            if (property != null && property.CanRead && property.CanWrite)
            {
                map[d.Name] = new Member { Type = property.PropertyType, Get = property.GetValue, Set = property.SetValue };
                continue;
            }
            var field = typeof(Instance).GetField(d.Name, Flags);
            if (field != null) map[d.Name] = new Member { Type = field.FieldType, Get = field.GetValue, Set = field.SetValue };
        }
        return map;
    }

    /// <summary>True when <c>Instance</c> has a member for the descriptor (the schema test asserts this for every descriptor).</summary>
    public static bool HasMember(string name) => s_members.ContainsKey(name);

    /// <summary>The CLR type of the <c>Instance</c> member for a descriptor, or null.</summary>
    public static Type? MemberType(string name) => s_members.TryGetValue(name, out var m) ? m.Type : null;

    /// <summary>The current value of one persisted member of <paramref name="rack"/>, boxed.</summary>
    public static object? Read(Instance rack, string name) => s_members[name].Get(rack);

    /// <summary>Puts every value that is set on <paramref name="settings"/> onto <paramref name="rack"/>, without persisting anything.</summary>
    public static void Apply(Instance rack, RackSettings settings)
    {
        using var noWrites = rack.SuspendPersistence();
        foreach (var d in RackSettingsSchema.Persisted)
        {
            object? value = settings.Get(d.Name);
            if (value == null || !s_members.TryGetValue(d.Name, out var member)) continue;
            if (d.Name == RackSettingsSchema.NameField && Equals(member.Get(rack), value)) continue;
            member.Set(rack, Copy(value));
        }
    }

    /// <summary>A new rack named like <paramref name="settings"/> with its values applied (nothing is written).</summary>
    public static Instance ToInstance(RackSettings settings)
    {
        var rack = new Instance(settings.Name ?? "", false);
        Apply(rack, settings);
        return rack;
    }

    /// <summary>The persisted values of <paramref name="rack"/> as typed settings (lists are copied).</summary>
    public static RackSettings ToSettings(Instance rack)
    {
        var settings = new RackSettings();
        foreach (var d in RackSettingsSchema.Persisted)
        {
            if (!s_members.TryGetValue(d.Name, out var member)) continue;
            object? value = member.Get(rack);
            if (value != null) settings.Set(d.Name, Copy(value));
        }
        return settings;
    }

    internal static object Copy(object value) => value switch
    {
        List<string> list => new List<string>(list),
        List<Tuple<string, string>> pairs => new List<Tuple<string, string>>(pairs),
        int[] numbers => (int[])numbers.Clone(),
        _ => value,
    };
}
