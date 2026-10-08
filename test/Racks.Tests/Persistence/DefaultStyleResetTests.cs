using Microsoft.Win32;
using Racks.Core;

namespace Racks.Tests.Persistence;

[Collection("Registry")]
public sealed class DefaultStyleResetTests : IDisposable
{
    private readonly string _previousAppName = InstanceController.appName;
    private readonly string _root = "RacksTests-" + Guid.NewGuid().ToString("N");
    private readonly InstanceController _controller;

    public DefaultStyleResetTests()
    {
        InstanceController.appName = _root;
        _controller = new InstanceController();
        Racks.MainWindow._controller = _controller;
    }

    public void Dispose()
    {
        InstanceController.appName = _previousAppName;
        Racks.MainWindow._controller = null!;
        try { Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\{_root}", throwOnMissingSubKey: false); } catch { }
    }

    private RegistryKey Root(bool writable = false) =>
        Registry.CurrentUser.CreateSubKey($@"SOFTWARE\{_root}", writable);

    [Fact]
    public void Reset_removes_style_defaults_and_keeps_app_switches_and_markers()
    {
        using (var k = Root(true))
        {
            k.SetValue("TitleBarColor", "#112233");
            k.SetValue("IconSize", 64);
            k.SetValue("BorderEnabled", "True");
            k.SetValue("IcePhysics", "False");
            k.SetValue("HideDesktopIcons", "True");
            k.SetValue("startOnLogin", "True");
            k.SetValue("DoubleClickToHide", "True");
            k.SetValue("InstallAnimationShownV2", "True");
            k.SetValue("FirstRunWelcomeShownV2", "True");
            k.SetValue("QuickAccessMirrorPinned", "True");
            k.SetValue("VirtualRackLinkOnDropRevertedToMove", "True");
            k.SetValue("FolderOpenInsideFrameDefaultedToExplorer", "True");
        }

        int removed;
        using (var k = Root(true)) removed = DefaultStyleReset.Run(k);

        Assert.Equal(3, removed);
        using var after = Root();
        var left = after.GetValueNames().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "DoubleClickToHide", "FirstRunWelcomeShownV2", "FolderOpenInsideFrameDefaultedToExplorer",
            "HideDesktopIcons", "IcePhysics", "InstallAnimationShownV2", "QuickAccessMirrorPinned",
            "VirtualRackLinkOnDropRevertedToMove", "startOnLogin",
        }.Order(StringComparer.Ordinal), left);
    }

    [Theory]
    [InlineData("TitleBarColor", true)]
    [InlineData("IconSize", true)]
    [InlineData("ItemFontFamily", true)]
    [InlineData("Name", false)]
    [InlineData("Folder", false)]
    [InlineData("PosX", false)]
    [InlineData("IcePhysics", false)]
    [InlineData("InstallAnimationShownV2", false)]
    public void IsStyleValue_classifies_names(string name, bool expected)
        => Assert.Equal(expected, DefaultStyleReset.IsStyleValue(name));

    [Fact]
    public void Suspended_property_changes_are_not_saved_and_do_not_leak_after_the_scope()
    {
        var rack = new Instance("hover", false) { Folder = System.IO.Path.GetTempPath() };
        rack.BorderColor = "#AAAAAA";
        string KeyValue() => (string?)Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{_root}\Instances\hover")?.GetValue("BorderColor") ?? "";
        Assert.Equal("#AAAAAA", KeyValue());

        int notifications = 0;
        rack.PropertyChanged += (_, _) => notifications++;
        using (rack.SuspendPersistence())
        {
            rack.BorderColor = "#7CFF00";
            Assert.Equal("#7CFF00", rack.BorderColor);   // the UI sees the preview
            Assert.Equal("#AAAAAA", KeyValue());          // the registry does not
        }
        Assert.True(notifications >= 1);

        rack.BorderColor = "#BBBBBB";                     // persistence is back on
        Assert.Equal("#BBBBBB", KeyValue());
    }

    [Fact]
    public void Nested_scopes_only_resume_when_all_are_disposed()
    {
        var rack = new Instance("nested", false) { Folder = System.IO.Path.GetTempPath() };
        string KeyValue() => (string?)Registry.CurrentUser.OpenSubKey($@"SOFTWARE\{_root}\Instances\nested")?.GetValue("TitleBarColor") ?? "";
        rack.TitleBarColor = "#111111";

        var outer = rack.SuspendPersistence();
        var inner = rack.SuspendPersistence();
        inner.Dispose();
        rack.TitleBarColor = "#222222";
        Assert.Equal("#111111", KeyValue());
        outer.Dispose();
        outer.Dispose();                                  // double dispose is harmless
        rack.TitleBarColor = "#333333";
        Assert.Equal("#333333", KeyValue());
    }
}
