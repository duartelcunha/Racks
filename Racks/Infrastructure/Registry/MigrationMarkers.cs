using Microsoft.Win32;
using Racks.Infrastructure.Logging;

namespace Racks.Infrastructure.Registry;

/// <summary>
/// The one-shot markers under <c>HKCU\SOFTWARE\Racks</c> that stop a first-run or migration step from running
/// again. A marker is "set" when a value of that name exists, whatever its type (what <c>KeyExistsRoot</c> always
/// checked). <see cref="Set"/> writes the same text the app always wrote: REG_SZ <c>True</c>. The set of names is closed: an unknown name is a
/// programming error (<see cref="ArgumentException"/>). Otherwise nothing throws: an unreadable registry reads as
/// "not set" (the step runs again, as before; audited once) and a failed write is audited and returned as false.
/// </summary>
public sealed class MigrationMarkers
{
    public const string InstallAnimationShownV2 = "InstallAnimationShownV2";
    public const string QuickAccessMirrorPinned = "QuickAccessMirrorPinned";
    public const string FirstRunWelcomeShownV2 = "FirstRunWelcomeShownV2";
    public const string VirtualRackLinkOnDropRevertedToMove = "VirtualRackLinkOnDropRevertedToMove";
    public const string FolderOpenInsideFrameDefaultedToExplorer = "FolderOpenInsideFrameDefaultedToExplorer";

    private static readonly Dictionary<string, string> WrittenText = new(StringComparer.OrdinalIgnoreCase)
    {
        [InstallAnimationShownV2] = "True",
        [QuickAccessMirrorPinned] = "True",
        [FirstRunWelcomeShownV2] = "True",
        [VirtualRackLinkOnDropRevertedToMove] = "True",
        [FolderOpenInsideFrameDefaultedToExplorer] = "True",
    };

    /// <summary>The markers of the real registry; follows <c>InstanceController.appName</c>.</summary>
    public static MigrationMarkers Default { get; } = new(HkcuRegistryRoot.ForApp(), AuditLog.Forwarder);

    private readonly IRegistryRoot _root;
    private readonly RegistryAudit _audit;

    public MigrationMarkers(IRegistryRoot root, IAuditSink? audit = null)
    {
        _root = root;
        _audit = new RegistryAudit(audit ?? AuditLog.Forwarder);
    }

    public bool IsSet(string name)
    {
        Require(name);
        try
        {
            return _root.Read(name) != null;
        }
        catch (Exception ex)
        {
            _audit.Fallback(_root, name, "(unreadable)", $"read failed: {ex.GetType().Name}: {ex.Message}", "not set");
            return false;
        }
    }

    /// <summary>Sets the marker. False when the write failed (audited).</summary>
    public bool Set(string name)
    {
        string text = Require(name);
        try
        {
            _root.Write(name, text, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            _audit.WriteFailed(_root, name, text, ex);
            return false;
        }
    }

    private static string Require(string name) =>
        WrittenText.TryGetValue(name, out var text) ? text : throw new ArgumentException($"'{name}' is not a known migration marker", nameof(name));
}
