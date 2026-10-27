using Racks.Tests.Fakes;

namespace Racks.Tests.Persistence;

/// <summary>The guard that keeps tests away from the real HKCU\SOFTWARE\Racks works, and records what it refuses.</summary>
[Collection("Registry")]
public sealed class TestRegistryGuardTests
{
    [Fact]
    public void The_guard_refuses_the_real_root_and_records_the_refusal()
    {
        Assert.True(TestRegistryGuard.IsInstalled);
        Assert.Equal("Racks", InstanceController.appName);

        var error = Assert.Throws<InvalidOperationException>(() => Instance.Store.Delete("guard-self-test"));

        Assert.Contains("REAL", error.Message);
        Assert.Contains("Delete guard-self-test", TestRegistryGuard.Refusals);
        TestRegistryGuard.Forget("Delete guard-self-test");
    }

    [Fact]
    public void Reading_the_global_defaults_on_the_real_root_returns_none_instead_of_reading_them()
    {
        Assert.True(TestRegistryGuard.IsInstalled);
        Assert.Equal("Racks", InstanceController.appName);
        var defaults = Instance.Store.ReadGlobalStyleDefaults();
        Assert.All(Racks.Infrastructure.Registry.RackSettingsSchema.GlobalDefaults, d => Assert.Null(defaults.Get(d.Name)));
    }

    [Fact]
    public void No_test_so_far_has_been_refused_by_the_guard()
    {
        // The production code swallows the exception in places (InitInstances, the uninstall title map), so a test that forgot
        // RackRegistryFixture would silently see no racks. Only tests that ran before this one are visible here, so this is a
        // tripwire, not a proof; the same list is printed to stderr when the test process exits.
        Assert.Empty(TestRegistryGuard.Refusals);
    }
}
