using System.Diagnostics;
using System.Globalization;

namespace Racks.UseCases.Startup;

/// <summary>The process-level settings that must be in place before any window or regex exists.</summary>
public static class ProcessConfiguration
{
    /// <summary>
    /// Translators: RACKS_LANG=zh-CN (any culture name) previews a language without changing the Windows display language.
    /// Unknown names are ignored.
    /// </summary>
    public static void ApplyCulturePreview(string? name, Action<CultureInfo> apply)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            apply(CultureInfo.GetCultureInfo(name.Trim()));
        }
        catch (CultureNotFoundException) { }
    }

    /// <summary>Runs <paramref name="override"/> and logs (never throws) when WPF refuses the language override.</summary>
    public static void TryOverrideLanguage(Action @override)
    {
        try { @override(); }
        catch (Exception ex) { Debug.WriteLine($"Language override failed: {ex.Message}"); }
    }
}
