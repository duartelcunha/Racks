using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Racks.Core.Abstractions;
using Racks.Infrastructure.FileSystem;
using Racks.Infrastructure.Logging;
using Racks.Infrastructure.Shell;
using Racks.Rack;
using Racks.Util;

namespace Racks.UseCases.AutoRoute;

/// <summary>What the service needs from the app, so it has no WPF or registry types of its own.</summary>
/// <param name="GetRacks">A snapshot of the racks, in order. The first match wins, so the order is the user's.</param>
/// <param name="Persist">Saves one rack after its claims changed.</param>
/// <param name="GetWorkspacePath">The Racks workspace folder, where desktop racks keep their files.</param>
public sealed record AutoRouteHost(
    Func<IReadOnlyList<Instance>> GetRacks,
    Action<Instance> Persist,
    Func<string> GetWorkspacePath);

/// <summary>
/// The use case "a file landed on the Desktop": pick the rack whose rule matches (first match wins), ask
/// <see cref="RoutingPolicy"/> whether the entry may be moved, move it through <see cref="FileOperationBroker"/>,
/// tell the shell, and let a desktop rack claim it (INV-ROUTE-1).
///
/// Auditing: a move that reaches the broker is recorded by the broker (kind Move, reason "auto-route"), so
/// there is exactly one record for it. Anything decided before the broker (a skip by policy, a file still in
/// use after the last try, a rule that cannot run) is recorded here as kind <c>AutoRoute</c>. A file that no
/// rack wants is not recorded: the Desktop sees many of them and nothing happened to them.
///
/// Threading: called on the UI thread (the debounced flush and the retries both are). Retries use the given
/// scheduler; nothing blocks or sleeps.
/// </summary>
public sealed class AutoRouteService : IDisposable
{
    /// <summary>Waits between the looks at a file that is still in use: the first try plus these retries (about 30 s in all).</summary>
    internal static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    internal const string AuditKind = "AutoRoute";
    internal const string MoveReason = "auto-route";

    // What the service made of one rack's stored rule text. The text itself is never touched: a rule that
    // can't run (not valid, too slow) just makes the rack inactive until the user edits it.
    private sealed class CompiledRule
    {
        public string? Pattern;
        public Regex? Regex;
    }

    private readonly AutoRouteHost _host;
    private readonly IDelayScheduler _scheduler;
    private readonly RoutingPolicy _policy;
    private readonly FileOperationBroker _broker;
    private readonly ShellOperationGateway _shell;
    private readonly IAuditSink _audit;

    private readonly object _gate = new();
    private readonly Dictionary<string, IDisposable> _retries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<Instance, CompiledRule> _rules = new();
    private readonly HashSet<string> _reportedRules = new(StringComparer.Ordinal);
    private Dictionary<string, bool>? _folderCache;   // only while a batch runs (UI thread)
    private bool _disposed;

    public AutoRouteService(
        AutoRouteHost host,
        IDelayScheduler scheduler,
        RoutingPolicy? policy = null,
        FileOperationBroker? broker = null,
        ShellOperationGateway? shell = null,
        IAuditSink? audit = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _policy = policy ?? RoutingPolicy.Default;
        _broker = broker ?? FileOperationBroker.Default;
        _shell = shell ?? ShellOperationGateway.Default;
        _audit = audit ?? AuditLog.Forwarder;
    }

    /// <summary>Where a matching file goes: a desktop rack's folder is the Desktop itself, so its files go to the workspace.</summary>
    internal static string DestinationFor(Instance rack, string workspace)
        => rack.IsDesktopFilterRack ? workspace : rack.Folder;

    /// <summary>
    /// Judges one Desktop entry now. Never throws. A file that is still in use comes back
    /// <see cref="RoutingOutcome.Deferred"/> and is looked at again later; the final outcome is then recorded
    /// but not returned (it is in the audit log).
    /// </summary>
    public RoutingDecision Process(string path) => Process(path, attempt: 0);

    /// <summary>
    /// Judges the paths of one debounce window. Same as calling <see cref="Process(string)"/> for each, except that
    /// whether a rack's folder exists is asked once per rack for the whole batch, not once per file.
    /// </summary>
    public void ProcessBatch(IEnumerable<string> paths)
    {
        _folderCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in paths) Process(path, attempt: 0);
        }
        finally
        {
            _folderCache = null;
        }
    }

    private RoutingDecision Process(string path, int attempt)
    {
        try
        {
            return Decide(path ?? "", attempt);
        }
        catch (Exception ex)
        {
            return Record(new RoutingDecision(path ?? "", null, null, RoutingOutcome.Failed, "Unexpected error: " + ex.Message));
        }
    }

    private RoutingDecision Decide(string path, int attempt)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrEmpty(name)) return new RoutingDecision(path, null, null, RoutingOutcome.NoMatch, "");

        if (attempt == 0)
        {
            lock (_gate)
                if (_disposed || _retries.ContainsKey(path))
                    return new RoutingDecision(path, null, null, RoutingOutcome.Deferred, "Already waiting for this file.");
        }

        var rack = FindRack(name);
        if (rack == null) return new RoutingDecision(path, null, null, RoutingOutcome.NoMatch, "");

        var verdict = _policy.Evaluate(path);
        switch (verdict.Kind)
        {
            case EligibilityKind.Gone:
                // A temporary name that was renamed or deleted before we looked: nothing happened, nothing to record.
                return new RoutingDecision(path, rack.Name, null, RoutingOutcome.Skipped, "Not routed: " + verdict.Reason + ".");

            case EligibilityKind.Skip:
                return Record(new RoutingDecision(path, rack.Name, null, RoutingOutcome.Skipped, "Not routed: " + verdict.Reason + "."));

            case EligibilityKind.InUse:
                if (attempt >= RetryDelays.Length)
                {
                    var total = RetryDelays.Aggregate(TimeSpan.Zero, (a, b) => a + b);
                    return Record(new RoutingDecision(path, rack.Name, null, RoutingOutcome.Skipped,
                        $"Not routed: {verdict.Reason}, still after {RetryDelays.Length + 1} tries over {total.TotalSeconds:0} s. It stays on the Desktop."));
                }
                ScheduleRetry(path, attempt);
                return new RoutingDecision(path, rack.Name, null, RoutingOutcome.Deferred, verdict.Reason);
        }

        return MoveInto(rack, path, name);
    }

    private RoutingDecision MoveInto(Instance rack, string path, string name)
    {
        string dest = DestinationFor(rack, SafeWorkspace());
        if (string.IsNullOrWhiteSpace(dest))
            return Record(new RoutingDecision(path, rack.Name, null, RoutingOutcome.Skipped, "Not routed: the rack has no destination folder."));

        string target = Path.Combine(dest, name);   // name passed the leaf-name check in the policy
        bool isDir = Directory.Exists(path);
        var result = _broker.Move(path, target, MoveReason);   // the broker writes the one audit record for this move

        if (!result.Completed)
        {
            var outcome = result.Status == OperationStatus.Failed ? RoutingOutcome.Failed : RoutingOutcome.Skipped;
            return new RoutingDecision(path, rack.Name, target, outcome, result.Reason);
        }

        _shell.NotifyItemMoved(path, target, isDir);
        _shell.NotifyDirectoryChanged(Path.GetDirectoryName(path));

        string note = result.Reason;
        if (rack.IsDesktopFilterRack)
        {
            // A desktop rack lists the files it claims by name (the rack refreshes itself: it watches the workspace).
            try
            {
                foreach (var lost in DesktopRackClaims.Claim(_host.GetRacks(), rack, name))
                    _host.Persist(lost);
                _host.Persist(rack);
            }
            catch (Exception ex)
            {
                string failure = "The file was moved but saving the rack failed: " + ex.Message;
                note = (note + " " + failure).Trim();
                Write(path, target, "Failed", failure);
            }
        }
        return new RoutingDecision(path, rack.Name, target, RoutingOutcome.Routed, note);
    }

    // First rack with a usable rule, a matching name and an existing folder. The (cheap) name test comes first;
    // the folder is only looked at for a rack that already matched.
    private Instance? FindRack(string name)
    {
        foreach (var rack in _host.GetRacks())
        {
            string pattern = rack.AutoRouteRegex;
            if (string.IsNullOrWhiteSpace(pattern)) continue;

            var rule = RuleFor(rack, pattern);
            if (rule.Regex == null) continue;
            bool matched;
            try
            {
                matched = rule.Regex.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                // A rule that is too slow on a real name: this rack stays inactive until its text changes.
                rule.Regex = null;
                ReportRuleOnce(rack, pattern, "slow", $"The rule /{pattern}/ of rack \"{rack.Name}\" took too long on a file name; the rack is inactive for auto-route until the rule is edited.");
                continue;
            }
            if (matched && FolderExists(rack.Folder)) return rack;
        }
        return null;
    }

    private bool FolderExists(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        var cache = _folderCache;
        if (cache == null) return Directory.Exists(folder);
        if (!cache.TryGetValue(folder, out bool exists)) cache[folder] = exists = Directory.Exists(folder);
        return exists;
    }

    private CompiledRule RuleFor(Instance rack, string pattern)
    {
        var rule = _rules.GetOrCreateValue(rack);
        if (!string.Equals(rule.Pattern, pattern, StringComparison.Ordinal))
        {
            rule.Pattern = pattern;
            rule.Regex = SafeRegex.TryCompile(pattern, RegexOptions.IgnoreCase);
            if (rule.Regex == null)
                ReportRuleOnce(rack, pattern, "invalid", $"The rule /{pattern}/ of rack \"{rack.Name}\" is not a valid pattern; the rack is inactive for auto-route until the rule is edited.");
            else if (!SafeRegex.IsAcceptable(pattern, RegexOptions.IgnoreCase))
            {
                rule.Regex = null;
                ReportRuleOnce(rack, pattern, "slow", $"The rule /{pattern}/ of rack \"{rack.Name}\" is too slow; the rack is inactive for auto-route until the rule is edited.");
            }
        }
        return rule;
    }

    // One record per rack and pattern, not one per file: a bad rule would otherwise flood the log.
    private void ReportRuleOnce(Instance rack, string pattern, string kind, string detail)
    {
        string key = rack.Name + "\u0001" + pattern + "\u0001" + kind;
        lock (_gate)
            if (!_reportedRules.Add(key)) return;
        Write("rack:" + rack.Name, null, "Skipped", detail);
    }

    private void ScheduleRetry(string path, int attempt)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _retries[path] = _scheduler.Schedule(RetryDelays[attempt], () => Retry(path, attempt + 1));
        }
    }

    private void Retry(string path, int attempt)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _retries.Remove(path);
        }
        Process(path, attempt);
    }

    private string SafeWorkspace()
    {
        try { return _host.GetWorkspacePath() ?? ""; }
        catch { return ""; }
    }

    // A decision made before the broker was reached: write its record here.
    private RoutingDecision Record(RoutingDecision d)
    {
        Write(d.Path, d.Destination, d.Outcome.ToString(), d.Reason);
        return d;
    }

    private void Write(string source, string? destination, string status, string detail)
    {
        try
        {
            _audit.Write(new AuditEvent(Guid.NewGuid().ToString("N")[..12], DateTime.UtcNow, AuditKind, source, destination, MoveReason, status, detail));
        }
        catch
        {
            // A broken audit sink must not change what auto-route does.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var pending in _retries.Values) pending.Dispose();
            _retries.Clear();
        }
    }
}
