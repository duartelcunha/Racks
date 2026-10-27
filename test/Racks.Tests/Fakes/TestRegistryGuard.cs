using System.Runtime.CompilerServices;
using Racks.Infrastructure.Registry;

namespace Racks.Tests.Fakes;

/// <summary>
/// No test may read or write the real HKCU\SOFTWARE\Racks: a rack that persists would otherwise write to it as soon as a
/// test changes a property. Runs once when the test assembly loads and wraps the default settings store: while
/// <c>InstanceController.appName</c> is still "Racks" (a test did not redirect it to a throwaway root) a write, delete or load
/// throws, and reading the global style defaults returns none. Tests that need the registry use
/// <c>RackRegistryFixture</c>, which redirects the root.
/// </summary>
internal static class TestRegistryGuard
{
    [ModuleInitializer]
    internal static void Init() => Instance.Store = new GuardedStore(Instance.Store);

    private sealed class GuardedStore(IRackSettingsStore inner) : IRackSettingsStore
    {
        private static bool IsRealRoot => string.Equals(InstanceController.appName, "Racks", StringComparison.Ordinal);

        private static void Refuse() => throw new InvalidOperationException(
            @"A test touched the REAL HKCU\SOFTWARE\Racks registry. Point InstanceController.appName at a throwaway root (RackRegistryFixture) or wrap the change in Instance.SuspendPersistence().");

        public RackLoadResult LoadAll() { if (IsRealRoot) Refuse(); return inner.LoadAll(); }
        public RackSettings ReadGlobalStyleDefaults() => IsRealRoot ? new RackSettings() : inner.ReadGlobalStyleDefaults();
        public bool Save(Instance rack) { if (IsRealRoot) Refuse(); return inner.Save(rack); }
        public bool SaveField(Instance rack, string propertyName) { if (IsRealRoot) Refuse(); return inner.SaveField(rack, propertyName); }
        public bool SaveGlobalDefault(Instance rack, string propertyName) { if (IsRealRoot) Refuse(); return inner.SaveGlobalDefault(rack, propertyName); }
        public bool Delete(string name) { if (IsRealRoot) Refuse(); return inner.Delete(name); }
        public int ResetGlobalStyleDefaults() { if (IsRealRoot) Refuse(); return inner.ResetGlobalStyleDefaults(); }
    }
}
