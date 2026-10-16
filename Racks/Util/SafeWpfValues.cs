using System.IO;
using System.Text.RegularExpressions;

namespace Racks.Util
{
    // INV-IMPORT-3: rack settings (registry values, imported layouts) that WPF turns into
    // resources. Several WPF parsers dereference a location hidden in the string:
    //   - new FontFamily(@"\\host\share\#Font") opens the share to look for font files,
    //   - ColorConverter / BrushConverter accept "ContextColor <uri> ..." and open that ICC
    //     profile URI (file://host/share/x.icc),
    //   - a BitmapImage UriSource (and File.Exists before it) opens a UNC path.
    // Touching an attacker's SMB share leaks the user's NTLM hash. Every such value is checked
    // here first: a font family is only names, a color only "#hex", a named color or "sc#"
    // numbers, and a background image only a local drive path. Anything else falls back.
    public static class SafeWpfValues
    {
        public const string DefaultFontFamily = "Segoe UI";

        // Up to 8 comma-separated family names (the WPF fallback list), each 1-64 letters,
        // digits, spaces and . _ ' & - and not starting with a space. No \ / : # %, so no
        // location or URI can be expressed.
        private static readonly Regex FontFamilyPattern = new(
            @"\A[\p{L}\p{N}._'&-][\p{L}\p{N} ._'&-]{0,63}(, ?[\p{L}\p{N}._'&-][\p{L}\p{N} ._'&-]{0,63}){0,7}\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        // "#RGB", "#ARGB", "#RRGGBB", "#AARRGGBB", a named color ("Red"), or "sc#a,r,g,b".
        // Not "ContextColor <profile uri> ...", which makes WPF open the URI.
        private static readonly Regex ColorPattern = new(
            @"\A(#([0-9A-Fa-f]{3,4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})|[A-Za-z]{1,32}|sc#[0-9eE.,+ -]{1,128})\z",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        public static bool IsSafeFontFamily(string? name) => SafeMatch(FontFamilyPattern, name);

        public static string SafeFontFamilyName(string? name) => IsSafeFontFamily(name) ? name! : DefaultFontFamily;

        // The only way rack settings become a FontFamily.
        public static System.Windows.Media.FontFamily CreateFontFamily(string? name)
            => new(SafeFontFamilyName(name));

        public static bool IsSafeColor(string? value) => SafeMatch(ColorPattern, value);

        public static string SafeColorOr(string? value, string fallback) => IsSafeColor(value) ? value! : fallback;

        // A fully-qualified LOCAL drive path ("C:\..." or "C:/..."), no "..", no ADS, no
        // wildcard or control characters. Anything starting with two separators (UNC
        // "\\host\share", "//host", "\\host@SSL", and the device forms "\\?\", "\\.\") is refused.
        public static bool IsLocalDrivePath(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3) return false;
            if (IsSeparator(value[0])) return false;                  // UNC, device paths, "\rooted"
            if (!char.IsAsciiLetter(value[0]) || value[1] != ':' || !IsSeparator(value[2]))
                return false;                                         // "C:x" (drive-relative), "relative"
            if (value.IndexOf(':', 2) >= 0) return false;             // alternate data streams
            foreach (char ch in value)
                if (ch < 0x20 || ch is '"' or '<' or '>' or '|' or '*' or '?') return false;
            foreach (var seg in value.Split('\\', '/'))
                if (seg == "..") return false;
            try { return Path.IsPathFullyQualified(value); }
            catch { return false; }
        }

        private static bool IsSeparator(char c) => c == '\\' || c == '/';

        private static bool SafeMatch(Regex pattern, string? value)
        {
            if (value == null) return false;
            try { return pattern.IsMatch(value); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
