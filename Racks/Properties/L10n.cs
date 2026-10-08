using System.Globalization;

namespace Racks.Properties;

/// <summary>Small helpers for text that has numbers in it, so translators can reorder the words.</summary>
public static class L10n
{
    /// <summary>string.Format with the current culture. Use {0}, {1} in the resource so a translation can reorder them.</summary>
    public static string F(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// Picks the singular or plural resource for a count (one for 1, other for everything else). That is
    /// right for English, Spanish, Italian and Portuguese, and for Chinese and Korean resources that use the
    /// same text for both. Languages with more plural forms (Polish, Czech) should phrase the text without a count word.
    /// </summary>
    public static string Plural(long count, string one, string other) => count == 1 ? one : other;
}
