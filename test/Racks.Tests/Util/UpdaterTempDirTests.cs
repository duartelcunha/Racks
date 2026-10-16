using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Racks.Tests.Util;

public class UpdaterTempDirTests
{
    [Fact]
    public void Update_download_folder_is_fresh_and_owner_only()
    {
        string dir = Updater.CreatePrivateTempDir();
        try
        {
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), "Racks-update-"), dir);
            Assert.Empty(Directory.EnumerateFileSystemEntries(dir));

            var acl = new DirectoryInfo(dir).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected, "the folder must not inherit %TEMP%'s ACL");
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            var me = WindowsIdentity.GetCurrent().User;
            Assert.NotEmpty(rules);
            Assert.All(rules, r => Assert.Equal(me, r.IdentityReference));

            string other = Updater.CreatePrivateTempDir();
            Updater.TryDeleteDir(other);
            Assert.NotEqual(dir, other);

            File.WriteAllBytes(Path.Combine(dir, "Racks-Setup-9.9.9.exe"), new byte[] { 1 });
        }
        finally
        {
            Updater.TryDeleteDir(dir);
        }
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Old_update_folders_are_cleaned_up_but_nothing_else()
    {
        string root = Path.Combine(Path.GetTempPath(), "racks-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string target = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "x");
        try
        {
            string Make(string name, bool withFile = true)
            {
                string d = Path.Combine(root, name);
                Directory.CreateDirectory(d);
                if (withFile) File.WriteAllBytes(Path.Combine(d, "Racks-Setup-9.9.9.exe"), new byte[] { 1 });
                return d;
            }
            string old1 = Make("Racks-update-" + Guid.NewGuid().ToString("N"));
            string old2 = Make("Racks-update-" + Guid.NewGuid().ToString("N"), withFile: false);
            string otherName = Make("Racks-update-mine");
            string upper = Make("Racks-update-" + Guid.NewGuid().ToString("N").ToUpperInvariant());
            string nested = Make("Racks-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(nested, "sub"));
            string junction = Path.Combine(root, "Racks-update-" + Guid.NewGuid().ToString("N"));
            Assert.True(Racks.Util.JunctionHelper.TryCreate(target, junction), "test setup: junction");

            int removed = Updater.CleanupOldUpdateDirs(root);

            Assert.Equal(2, removed);
            Assert.False(Directory.Exists(old1));
            Assert.False(Directory.Exists(old2));
            Assert.True(Directory.Exists(otherName));
            Assert.True(Directory.Exists(upper));
            Assert.True(Directory.Exists(nested));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
        }
        finally
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var info = new DirectoryInfo(d);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) info.Delete();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Cleanup_of_a_missing_temp_folder_does_nothing()
        => Assert.Equal(0, Updater.CleanupOldUpdateDirs(Path.Combine(Path.GetTempPath(), "racks-missing-" + Guid.NewGuid().ToString("N"))));
}
