using Microsoft.Win32;
using Racks.Util;
using Xunit;

namespace Racks.Windows.Tests;

public sealed class NativeProfileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "racks-tests", Guid.NewGuid().ToString("N"));
    private readonly string? prior = Environment.GetEnvironmentVariable("RACKS_TEST_PROFILE");
    public NativeProfileTests() => NativeProfile.Initialize(["--profile", root]);

    [Fact]
    public void RegistryCompatibilityRetainsTypesAndOrderingAcrossReload()
    {
        using (var key = ProfileRegistry.CurrentUser.CreateSubKey(@"SOFTWARE\Racks\Instances\example"))
        {
            key.SetValue("Order", new[] { "z.txt", "a.txt", "long name.txt" });
            key.SetValue("Binary", new byte[] { 0, 255, 16 });
            key.SetValue("Position", 123);
            key.SetValue("Large", long.MaxValue);
            key.SetValue("Font", "Segoe UI");
        }
        NativeProfile.Initialize(["--profile", Path.Combine(root, "other")]);
        using (var other = ProfileRegistry.CurrentUser.CreateSubKey(@"SOFTWARE\Racks")) other.SetValue("Other", "profile");
        NativeProfile.Initialize(["--profile", root]);
        using var restored = ProfileRegistry.CurrentUser.OpenSubKey(@"software\racks\instances\EXAMPLE");
        Assert.NotNull(restored);
        Assert.Equal(new[] { "z.txt", "a.txt", "long name.txt" }, Assert.IsType<string[]>(restored.GetValue("order")));
        Assert.Equal(new byte[] { 0, 255, 16 }, Assert.IsType<byte[]>(restored.GetValue("binary")));
        Assert.Equal(123, restored.GetValue("position"));
        Assert.Equal(long.MaxValue, restored.GetValue("large"));
        Assert.Equal(RegistryValueKind.MultiString, restored.GetValueKind("order"));
        Assert.Equal("Segoe UI", restored.GetValue("font"));
        Assert.True(File.Exists(Path.Combine(root, "native-settings.json.bak")));
    }

    [Fact]
    public void IsolatedProfileCannotWriteWindowsAutorunOrReadNormalRacks()
    {
        Assert.Null(ProfileRegistry.CurrentUser.OpenSubKey(@"SOFTWARE\Racks"));
        Assert.Throws<InvalidOperationException>(() => ProfileRegistry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"));
        Assert.StartsWith(root, NativeProfile.GetFolderPath(Environment.SpecialFolder.Desktop));
        Assert.StartsWith(root, NativeProfile.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Assert.NotEqual(@"Local\Racks-SingleInstance-2C9D", NativeProfile.MutexName);
    }

    [Fact]
    public void UnknownProfileVersionFailsWithoutRewritingIt()
    {
        var file = Path.Combine(root, "native-settings.json");
        const string unsupported = "{\"Version\":999,\"Keys\":{}}";
        File.WriteAllText(file, unsupported);
        Assert.Throws<InvalidDataException>(() => ProfileRegistry.CurrentUser.OpenSubKey(@"SOFTWARE\Racks"));
        Assert.Equal(unsupported, File.ReadAllText(file));
    }

    public void Dispose()
    {
        NativeProfile.Initialize([]);
        Environment.SetEnvironmentVariable("RACKS_TEST_PROFILE", prior);
        if (root.StartsWith(Path.Combine(Path.GetTempPath(), "racks-tests") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
    }
}
