using System.IO;
using System.Security.Cryptography;

namespace Racks.Util;

public static class NativeProfile
{
    public static string? Root { get; private set; }
    public static bool IsIsolated => Root != null;
    public static bool DesignPreview { get; private set; }
    public static string MutexName => IsIsolated ? @"Local\Racks-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Root!)))[..16] : @"Local\Racks-SingleInstance-2C9D";

    public static void Initialize(string[] args)
    {
        Root = null;
        var index = Array.IndexOf(args, "--profile");
        if (index >= 0)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--")) throw new ArgumentException("--profile needs a directory.");
            Root = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Root);
            foreach (var folder in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData }) Directory.CreateDirectory(GetFolderPath(folder));
            Environment.SetEnvironmentVariable("RACKS_TEST_PROFILE", Root);
        }
        DesignPreview = args.Contains("--design-preview");
        if (DesignPreview && !IsIsolated) throw new ArgumentException("The design preview requires an isolated --profile.");
    }

    public static string GetFolderPath(Environment.SpecialFolder folder)
    {
        if (Root == null) return Environment.GetFolderPath(folder);
        return folder switch
        {
            Environment.SpecialFolder.Desktop or Environment.SpecialFolder.DesktopDirectory => Path.Combine(Root, "Desktop"),
            Environment.SpecialFolder.ApplicationData => Path.Combine(Root, "Roaming"),
            Environment.SpecialFolder.LocalApplicationData => Path.Combine(Root, "Local"),
            Environment.SpecialFolder.UserProfile => Root,
            _ => Environment.GetFolderPath(folder)
        };
    }
}
