using Racks.Infrastructure.FileSystem;

namespace Racks.Tests.FileSystem;

// INV-FS-2: a name the user types is a leaf name. These are the shapes that used to move an item elsewhere
// (or create folders elsewhere) through Path.Combine.
public class PathPolicyTests
{
    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub\\x.txt")]
    [InlineData("\\x")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:\\x")]
    [InlineData("C:x")]
    [InlineData("a:b")]                       // alternate data stream
    [InlineData("file.txt:stream")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("con.txt")]
    [InlineData("Nul.tar.gz")]
    [InlineData("NUL")]
    [InlineData("PRN.log")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("com9.txt")]
    [InlineData("LPT9.log")]
    [InlineData("CON .txt")]                  // spaces before the dot do not make it a different device
    [InlineData("CONIN$")]
    [InlineData("name.")]                     // trailing dot
    [InlineData("name ")]                     // trailing space
    [InlineData("name...")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("a|b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a<b")]
    [InlineData("a\"b")]
    [InlineData("a\u0001b")]                  // control characters
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    public void Names_that_are_not_plain_leaf_names_are_rejected(string name)
    {
        var (ok, reason) = PathPolicy.ValidateLeafName(name);
        Assert.False(ok, $"\"{name}\" should be rejected");
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("LPT0.txt")]
    [InlineData("COM\u00B9")]
    [InlineData("com\u00B2.log")]
    [InlineData("COM\u00B3")]
    [InlineData("LPT\u00B9")]
    [InlineData("lpt\u00B2")]
    [InlineData("LPT\u00B3.txt")]
    public void Zero_and_superscript_device_names_are_reserved_too(string name)
        => Assert.False(PathPolicy.ValidateLeafName(name).Ok, name);

    // U+007F-U+009F are legal NTFS name characters (mojibake file names from zip extracts exist on disk);
    // only 0x00-0x1F are refused. char.IsControl would have rejected these.
    [Fact]
    public void C1_and_DEL_characters_are_allowed_in_names_and_paths_but_C0_is_not()
    {
        for (char c = '\u007F'; c <= '\u009F'; c++)
        {
            string name = "a" + c + "b";
            Assert.True(PathPolicy.ValidateLeafName(name).Ok, $"name with U+{(int)c:X4}");
            Assert.True(PathPolicy.ValidateFullPath("C:\\dir\\" + name).Ok, $"path with U+{(int)c:X4}");
        }
        Assert.True(PathPolicy.ValidateLeafName("\u00E2\u0080\u0093 mojibake.txt").Ok);   // what a bad unzip writes for an en dash
        Assert.False(PathPolicy.ValidateLeafName("a\u0001b").Ok);
        Assert.False(PathPolicy.ValidateFullPath("C:\\dir\\a\u0001b").Ok);
        Assert.False(PathPolicy.ValidateLeafName("a\u001Fb").Ok);
    }

    [Theory]
    [InlineData("\\\\server\\C$\\Users\\me\\Desktop")]
    [InlineData("\\\\localhost\\c$\\x")]
    [InlineData("\\\\localhost\\ADMIN$\\x")]
    [InlineData("\\\\?\\UNC\\server\\D$\\x")]
    [InlineData("\\\\server\\z$")]
    public void Administrative_shares_are_refused_because_the_protected_folder_check_cannot_see_through_them(string path)
        => Assert.False(PathPolicy.ValidateFullPath(path).Ok, path);

    [Theory]
    [InlineData("\\\\server\\data$\\x")]       // a hidden share that is not an admin share
    [InlineData("\\\\server\\Cdata\\x")]
    [InlineData("\\\\server\\share\\C$")]      // a folder called C$ inside a share is fine
    public void Ordinary_and_hidden_non_admin_shares_are_still_allowed(string path)
        => Assert.True(PathPolicy.ValidateFullPath(path).Ok, path);

    [Fact]
    public void Null_is_rejected()
        => Assert.False(PathPolicy.ValidateLeafName(null).Ok);

    [Fact]
    public void A_name_longer_than_255_characters_is_rejected_and_255_is_fine()
    {
        Assert.False(PathPolicy.ValidateLeafName(new string('a', 256)).Ok);
        Assert.True(PathPolicy.ValidateLeafName(new string('a', 255)).Ok);
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("My Folder")]
    [InlineData("a.b.c")]
    [InlineData(".gitignore")]
    [InlineData("name (1).txt")]
    [InlineData("COM10")]                     // only COM1-9 are devices
    [InlineData("CONSOLE.txt")]
    [InlineData("CONTENT")]
    [InlineData("LPT")]
    [InlineData("résumé final.docx")]
    [InlineData("日本語のファイル.txt")]
    [InlineData("📁 Projects 🚀")]
    [InlineData("  leading spaces ok")]
    [InlineData("100%")]
    [InlineData("a&b #1 [x] {y} (z) ~$ @ ! , ; = + ' ^ `")]
    public void Ordinary_names_unicode_and_emoji_are_allowed(string name)
    {
        var (ok, reason) = PathPolicy.ValidateLeafName(name);
        Assert.True(ok, $"\"{name}\" should be allowed but: {reason}");
    }

    [Theory]
    [InlineData("relative\\x.txt")]
    [InlineData("x.txt")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:x")]                       // drive-relative
    [InlineData("\\rooted")]                  // current-drive relative
    [InlineData("C:\\a\\..\\b")]
    [InlineData("C:\\a\\b:stream")]           // alternate data stream
    [InlineData("C:\\a\\*.txt")]
    [InlineData("C:\\a\\b\u0001")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    [InlineData("\\\\.\\C:")]
    [InlineData("\\\\?\\GLOBALROOT\\Device\\HarddiskVolume1\\x")]
    [InlineData("\\\\?\\Volume{01234567-89ab-cdef-0123-456789abcdef}\\x")]
    [InlineData("\\\\?\\C:x")]
    [InlineData("\\\\server")]                // host without a share
    [InlineData("\\\\server\\share:8080\\x")]
    public void Paths_that_are_not_ordinary_full_paths_are_rejected(string path)
    {
        var (ok, reason) = PathPolicy.ValidateFullPath(path);
        Assert.False(ok, $"\"{path}\" should be rejected");
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void Null_path_is_rejected()
        => Assert.False(PathPolicy.ValidateFullPath(null).Ok);

    [Theory]
    [InlineData("C:\\Users\\me\\Desktop\\file.txt")]
    [InlineData("C:\\")]
    [InlineData("D:\\a b\\c.d")]
    [InlineData("C:/Users/me/file.txt")]
    [InlineData("\\\\server\\share\\folder\\file.txt")]   // UNC is allowed for general paths: a rack may point at a share
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\C:\\Users\\me\\file.txt")]        // extended-length spelling of an ordinary path
    [InlineData("\\\\?\\UNC\\server\\share\\file.txt")]
    public void Ordinary_local_and_network_full_paths_are_allowed(string path)
    {
        var (ok, reason) = PathPolicy.ValidateFullPath(path);
        Assert.True(ok, $"\"{path}\" should be allowed but: {reason}");
    }

    // Magic Organize names folders after file groups; a cluster name must become a plain folder name.
    [Theory]
    [InlineData("..", "fallback")]
    [InlineData(".", "fallback")]
    [InlineData("a\\b", "a_b")]
    [InlineData("../../Windows", ".._.._Windows")]
    [InlineData("C:\\x", "C__x")]
    [InlineData("", "fallback")]
    [InlineData(null, "fallback")]
    [InlineData("   ", "fallback")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("Images.", "Images")]
    [InlineData("Images ", "Images")]
    [InlineData("Images", "Images")]
    [InlineData("Fotos 📷", "Fotos 📷")]
    [InlineData("a|b?c*d", "a_b_c_d")]
    public void SanitizeLeafName_turns_any_text_into_a_plain_folder_name(string? raw, string expected)
        => Assert.Equal(expected, PathPolicy.SanitizeLeafName(raw, "fallback"));

    [Theory]
    [InlineData("..\\..\\Windows\\System32")]
    [InlineData("a\\..\\b")]
    [InlineData("\\\\server\\share")]
    [InlineData("com1")]
    [InlineData("....")]
    [InlineData("x\u0000y")]
    [InlineData("name:stream")]
    public void SanitizeLeafName_always_returns_something_the_policy_accepts(string raw)
    {
        string name = PathPolicy.SanitizeLeafName(raw, "Cluster_ab12");
        Assert.True(PathPolicy.ValidateLeafName(name).Ok, $"\"{raw}\" -> \"{name}\"");
    }

    [Fact]
    public void SanitizeLeafName_truncates_a_very_long_name_to_a_valid_one()
    {
        string name = PathPolicy.SanitizeLeafName(new string('x', 400), "fallback");
        Assert.Equal(PathPolicy.MaxLeafLength, name.Length);
        Assert.True(PathPolicy.ValidateLeafName(name).Ok);
    }
}
