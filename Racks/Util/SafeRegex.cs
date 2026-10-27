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

        // Inputs that make the classic catastrophic patterns ("(a+)+$", "(a|aa)+$", "(\w+\s?)*$") time out. A
        // pattern that cannot digest a 64-character name within MatchTimeout would also stall on a real file
        // name, so it is not stored. This is a heuristic probe, not a proof: TryCompile + MatchTimeout stay the
        // real guard at match time.
        private static readonly string[] Canaries =
        [
            new string('a', 64) + "!",
            string.Concat(Enumerable.Repeat("ab", 32)) + "!",
            string.Join(" ", Enumerable.Repeat("word", 13)) + "!",
        ];

        // True when the pattern is non-empty, compiles, and survives the canary inputs inside MatchTimeout.
        // Used by setters that must keep only patterns they can run (an invalid or too-slow pattern is rejected).
        public static bool IsAcceptable(string? pattern, RegexOptions options = RegexOptions.None)
        {
            var re = TryCompile(pattern, options);
            if (re == null) return false;
            try
            {
                foreach (var canary in Canaries) re.IsMatch(canary);
                return true;
            }
            catch (RegexMatchTimeoutException) { return false; }
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
