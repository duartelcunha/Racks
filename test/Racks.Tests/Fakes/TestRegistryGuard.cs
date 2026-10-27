using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Fakes;

/// <summary>
/// No test may read or write the real HKCU\SOFTWARE\Racks through the rack settings store: a rack that persists would
/// otherwise write to it as soon as a test changes a property. Runs once when the test assembly loads and wraps the default
/// settings store: while <c>InstanceController.appName</c> is still "Racks" (a test did not redirect it to a throwaway root) a
/// write, delete or load throws, and reading the global style defaults returns none. Tests that need the registry use
/// <c>RackRegistryFixture</c>, which redirects the root.
/// <para>Every refusal is also recorded in <see cref="Refusals"/>, because the production code swallows the exception in places
/// (<c>InitInstances</c>, the uninstall title map) and a test that forgot the fixture would otherwise just see zero racks. The
/// guard's own test asserts the list is empty at that point, and a message is written to stderr when the test process ends with
/// a refusal on record.</para>
/// <para>What it does NOT cover: the layout import executor (it takes an <c>IRackRegistry</c> and is built by the caller, P9), the old <c>RegistryHelper</c> root
/// writer used by P5 comparisons, <c>AppSettings</c>/<c>MigrationMarkers</c> (they take their root as a parameter or follow
/// <c>appName</c> themselves), <c>MigrateLegacyRegistry</c> and <c>DefaultStyleReset.Run</c> on a key a test opened.</para>
/// </summary>
internal static class TestRegistryGuard
{
    public static readonly ConcurrentQueue<string> Refusals = new();

    [ModuleInitializer]
    internal static void Init()
    {
        Instance.Store = new GuardedStore(Instance.Store);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!Refusals.IsEmpty)
                Console.Error.WriteLine("TestRegistryGuard: " + Refusals.Count + " test call(s) were refused because they used the real HKCU\\SOFTWARE\\Racks: " + string.Join("; ", Refusals.Distinct()));
        };
    }

    public static bool IsInstalled => Instance.Store is GuardedStore;

    /// <summary>Removes the refusals of the guard's own test from the record.</summary>
    public static void Forget(string operation)
    {
        var keep = Refusals.Where(r => r != operation).ToList();
        while (Refusals.TryDequeue(out _)) { }
        foreach (var r in keep) Refusals.Enqueue(r);
    }

    internal sealed class GuardedStore(IRackSettingsStore inner) : IRackSettingsStore
    {
        private static bool IsRealRoot => string.Equals(InstanceController.appName, "Racks", StringComparison.Ordinal);

        private static void Refuse(string operation)
        {
            Refusals.Enqueue(operation);
            throw new InvalidOperationException(
                @"A test touched the REAL HKCU\SOFTWARE\Racks registry (" + operation + @"). Point InstanceController.appName at a throwaway root (RackRegistryFixture) or wrap the change in Instance.SuspendPersistence().");
        }

        public RackLoadResult LoadAll() { if (IsRealRoot) Refuse("LoadAll"); return inner.LoadAll(); }
        public RackSettings ReadGlobalStyleDefaults() => IsRealRoot ? new RackSettings() : inner.ReadGlobalStyleDefaults();
        public bool Save(Instance rack) { if (IsRealRoot) Refuse("Save"); return inner.Save(rack); }
        public bool Normalize(Instance rack) { if (IsRealRoot) Refuse("Normalize"); return inner.Normalize(rack); }
        public bool SaveField(Instance rack, string propertyName) { if (IsRealRoot) Refuse("SaveField " + propertyName); return inner.SaveField(rack, propertyName); }
        public bool SaveGlobalDefault(Instance rack, string propertyName) { if (IsRealRoot) Refuse("SaveGlobalDefault " + propertyName); return inner.SaveGlobalDefault(rack, propertyName); }
        public bool Delete(string name) { if (IsRealRoot) Refuse("Delete " + name); return inner.Delete(name); }
        public int ResetGlobalStyleDefaults() { if (IsRealRoot) Refuse("ResetGlobalStyleDefaults"); return inner.ResetGlobalStyleDefaults(); }
    }
}
