namespace Racks.UseCases.Updates;

// What the update use case needs from Windows. The implementations live in Racks/Infrastructure/Update; tests
// use fakes, so the whole check / download / verify / launch path runs without a network, a toast, a process
// or WPF.

/// <summary>The toasts of the updater. Implementations may throw; <see cref="UpdateService"/> never lets that out.</summary>
internal interface IUpdateNotifier
{
    void Show(string header, string body);

    /// <summary>The "update available" toast: header, body and the Install / Close buttons.</summary>
    void ShowOffer(string header, string body);

    IUpdateProgress StartProgress(string header);
}

internal interface IUpdateProgress
{
    void Report(int percent);
    void Hide();
}

internal interface IInstallerLauncher
{
    /// <summary>Starts the (verified) installer. Throws if it cannot be started.</summary>
    void Start(string installerPath);
}

/// <summary>Orderly application shutdown (so the single-instance mutex is released). May be called from any thread.</summary>
internal interface IAppShutdown
{
    void Shutdown();
}
