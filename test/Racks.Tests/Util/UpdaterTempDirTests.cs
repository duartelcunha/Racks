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
}
