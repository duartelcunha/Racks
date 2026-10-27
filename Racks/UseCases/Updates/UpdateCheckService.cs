using System.Diagnostics;
using Racks.Infrastructure.Update;

namespace Racks.UseCases.Updates;

/// <summary>
/// The glue the hidden host window runs around the updater: the opt-in background check a little after startup, the manual
/// "Check for updates" menu item, and the delayed clean-up of the download folders earlier updates left behind. The
/// update itself (manifest, signature, hash, install: <see cref="UpdateService"/>) stays fail-closed and never throws.
///
/// The background check is silent: a toast appears only when a newer release exists (the updater stays quiet on a first
/// failure such as being offline). The manual check also says "up to date". Both swallow and log any exception.
/// </summary>
public sealed class UpdateCheckService
{
    internal static readonly TimeSpan BackgroundCheckDelay = TimeSpan.FromSeconds(20);
    // Download folders of earlier updates cannot be deleted at the time (the installer ran from there):
    // a minute in, so a just-finished installer has exited.
    internal static readonly TimeSpan CleanupDelay = TimeSpan.FromMinutes(1);

    private readonly Func<TimeSpan, Task> _delay;
    private readonly Func<bool, Task> _check;
    private readonly Action _cleanup;

    /// <param name="delay">Waits (default <c>Task.Delay</c>).</param>
    /// <param name="check">Asks GitHub for a newer release; the argument is "toast even when up to date".</param>
    /// <param name="cleanup">Removes old update download folders.</param>
    public UpdateCheckService(Func<TimeSpan, Task>? delay = null, Func<bool, Task>? check = null, Action? cleanup = null)
    {
        _delay = delay ?? (d => Task.Delay(d));
        _check = check ?? (showToastIfNoUpdate => UpdateComposition.Service.CheckAsync(ReleaseCatalogClient.ReleasesApi, showToastIfNoUpdate));
        _cleanup = cleanup ?? (() => UpdateCleanupService.CleanupOldUpdateDirs());
    }

    /// <summary>Waits 20 s, then checks silently. Never throws.</summary>
    public async Task CheckInBackgroundAsync()
    {
        try
        {
            await _delay(BackgroundCheckDelay);
            await _check(false);
        }
        catch (Exception ex) { Debug.WriteLine($"Background update check failed: {ex.Message}"); }
    }

    /// <summary>The tray menu item: checks now and always tells the user the result. Never throws.</summary>
    public async Task CheckNowAsync()
    {
        try { await _check(true); }
        catch (Exception ex) { Debug.WriteLine($"Manual update check failed: {ex.Message}"); }
    }

    /// <summary>Starts, on the thread pool, the delayed removal of old update folders. An exception there is not observed, as before.</summary>
    public Task ScheduleCleanup() => Task.Run(async () =>
    {
        await _delay(CleanupDelay);
        _cleanup();
    });
}
