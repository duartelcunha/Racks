using System.IO;
using Racks.Rack;

namespace Racks.Tests.Rack;

public sealed class DesktopRackClaimsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "racks-claims-" + Guid.NewGuid().ToString("N"));

    public DesktopRackClaimsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_file_Explorer_moved_away_is_gone()
    {
        string file = Path.Combine(_root, "Holiday.jpg");
        File.WriteAllText(file, "x");
        Assert.False(DesktopRackClaims.IsGone(file));

        File.Move(file, Path.Combine(Path.GetTempPath(), "moved-" + Guid.NewGuid().ToString("N")));
        Assert.True(DesktopRackClaims.IsGone(file));
    }

    [Fact]
    public void A_folder_that_is_still_there_is_not_gone()
    {
        string dir = Path.Combine(_root, "Projects");
        Directory.CreateDirectory(dir);
        Assert.False(DesktopRackClaims.IsGone(dir));
    }

    [Fact]
    public void A_copy_leaves_the_original_so_it_is_not_gone()
    {
        string file = Path.Combine(_root, "Beach.jpg");
        File.WriteAllText(file, "x");
        File.Copy(file, Path.Combine(_root, "Beach-copy.jpg"));
        Assert.False(DesktopRackClaims.IsGone(file));
    }

    [Fact]
    public void An_empty_path_is_never_reported_as_gone()
    {
        Assert.False(DesktopRackClaims.IsGone(""));
    }
}
