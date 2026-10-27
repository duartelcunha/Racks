using Racks.Infrastructure.Update;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Racks.Tests.Util;

public class UpdaterTempDirTests
{
    [Fact]
    public void Update_download_folder_is_fresh_and_owner_only()
    {
        string dir = InstallerExecutor.CreatePrivateTempDir();
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

            string other = InstallerExecutor.CreatePrivateTempDir();
            UpdateCleanupService.TryDeleteDir(other);
            Assert.NotEqual(dir, other);

            File.WriteAllBytes(Path.Combine(dir, "Racks-Setup-9.9.9.exe"), new byte[] { 1 });
        }
        finally
        {
            UpdateCleanupService.TryDeleteDir(dir);
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

            int removed = UpdateCleanupService.CleanupOldUpdateDirs(root, minAge: TimeSpan.Zero);

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
        => Assert.Equal(0, UpdateCleanupService.CleanupOldUpdateDirs(Path.Combine(Path.GetTempPath(), "racks-missing-" + Guid.NewGuid().ToString("N"))));

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "racks-cleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void A_download_folder_that_was_just_created_is_not_cleaned_up_from_under_the_download()
    {
        string root = NewRoot();
        try
        {
            // The window the ledger found: the folder exists (empty) before the installer file is created in it.
            string dir = InstallerExecutor.CreatePrivateTempDir(root);

            Assert.Equal(0, UpdateCleanupService.CleanupOldUpdateDirs(root));   // default minimum age

            Assert.True(Directory.Exists(dir));
            using var file = new FileStream(Path.Combine(dir, "Racks-Setup-9.9.9.exe"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.WriteByte(1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_folder_is_cleaned_up_once_it_is_older_than_the_minimum_age()
    {
        string root = NewRoot();
        try
        {
            string young = InstallerExecutor.CreatePrivateTempDir(root);
            File.WriteAllBytes(Path.Combine(young, "Racks-Setup-9.9.9.exe"), new byte[] { 1 });

            Assert.Equal(0, UpdateCleanupService.CleanupOldUpdateDirs(root, nowUtc: DateTime.UtcNow.AddMinutes(9)));
            Assert.True(Directory.Exists(young));

            Assert.Equal(1, UpdateCleanupService.CleanupOldUpdateDirs(root, nowUtc: DateTime.UtcNow.AddMinutes(11)));
            Assert.False(Directory.Exists(young));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_folder_whose_installer_is_still_open_is_left_alone_however_old()
    {
        string root = NewRoot();
        try
        {
            string dir = InstallerExecutor.CreatePrivateTempDir(root);
            string exe = Path.Combine(dir, "Racks-Setup-9.9.9.exe");
            using (var running = new FileStream(exe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                running.WriteByte(1);
                Assert.Equal(0, UpdateCleanupService.CleanupOldUpdateDirs(root, TimeSpan.Zero));
                Assert.True(File.Exists(exe));
            }
            Assert.Equal(1, UpdateCleanupService.CleanupOldUpdateDirs(root, TimeSpan.Zero));   // once it is closed it goes
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

