using System.IO;
using Racks.Util;

namespace Racks.Tests.Util;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "racks-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Log => Path.Combine(_dir, CrashLog.FileName);

    [Fact]
    public void Creates_the_folder_and_appends()
    {
        CrashLog.Append(_dir, "one\n");
        CrashLog.Append(_dir, "two\n");
        Assert.Equal("one\ntwo\n", File.ReadAllText(Log));
    }

    [Fact]
    public void Rotates_to_dot_1_instead_of_growing_past_the_cap()
    {
        CrashLog.Append(_dir, new string('a', 60), maxBytes: 100);
        CrashLog.Append(_dir, new string('b', 60), maxBytes: 100);   // would reach 120: rotate first

        Assert.Equal(new string('b', 60), File.ReadAllText(Log));
        Assert.Equal(new string('a', 60), File.ReadAllText(Log + ".1"));
    }

    [Fact]
    public void A_second_rotation_replaces_the_old_backup_so_only_two_files_exist()
    {
        CrashLog.Append(_dir, new string('a', 60), maxBytes: 100);
        CrashLog.Append(_dir, new string('b', 60), maxBytes: 100);
        CrashLog.Append(_dir, new string('c', 60), maxBytes: 100);

        Assert.Equal(new string('c', 60), File.ReadAllText(Log));
        Assert.Equal(new string('b', 60), File.ReadAllText(Log + ".1"));
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void Never_throws_when_the_location_is_unusable()
    {
        string file = Path.Combine(Path.GetTempPath(), "racks-not-a-dir-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try { CrashLog.Append(Path.Combine(file, "sub"), "entry"); }   // a file where the folder should be
        finally { File.Delete(file); }
    }

    [Fact]
    public void Default_cap_is_one_megabyte()
        => Assert.Equal(1_000_000, CrashLog.MaxBytes);
}
