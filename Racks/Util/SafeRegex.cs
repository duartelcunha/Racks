using System;
using System.Text.RegularExpressions;

namespace Racks.Util
{
    // ReDoS guard-rail for patterns that come from the user, the registry, or an imported
    // layout file (FileFilterRegex, FileFilterHideRegex, AutoRouteRegex). A catastrophic-
    // backtracking pattern like "(a+)+$" does not throw at compile time, it just spins - and
    // these matches run on the UI thread, so one bad pattern would pin the app forever and
    // re-trigger on every launch. TryCompile bounds every match to MatchTimeout; IsMatch turns
    // a timeout into a caller-chosen fallback instead of an uncaught RegexMatchTimeoutException
    // (some call sites are not wrapped in a broad catch, so an unguarded throw would crash).
    public static class SafeRegex
    {
        public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

        public static Regex? TryCompile(string? pattern, RegexOptions options = RegexOptions.None)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            try { return new Regex(pattern, options, MatchTimeout); }
            catch (ArgumentException) { return null; } // invalid pattern -> treat as no filter
        }

        // onTimeout is what a pathological pattern should resolve to. For a "show only matches"
        // whitelist pass true (keep the file visible); for a "hide matches" pass false (don't
        // hide it). Either way the user never loses sight of their files to a bad regex.
        public static bool IsMatch(Regex? re, string input, bool onTimeout)
        {
            if (re == null) return false;
            try { return re.IsMatch(input); }
            catch (RegexMatchTimeoutException) { return onTimeout; }
        }
    }
}
