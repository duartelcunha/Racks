using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Toolkit.Uwp.Notifications;
using Racks.Infrastructure.Shell;
using Racks.Infrastructure.Update;
using Racks.UseCases.Startup;
using Application = System.Windows.Application;

namespace Racks.Infrastructure.Startup;

/// <summary>
/// The real WPF and Windows side of the startup (<see cref="IStartupHost"/>). Each method is the code that used to sit inline
/// in <c>App.OnStartup</c>, unchanged; the order is decided by <see cref="AppBootstrapService"/>.
/// </summary>
internal sealed class AppStartupHost : IStartupHost
{
    private readonly Application _app;
    private readonly Action _raiseBaseStartup;

    public AppStartupHost(Application app, Action raiseBaseStartup)
    {
        _app = app;
        _raiseBaseStartup = raiseBaseStartup;
    }

    public void ConfigureCulturePreview() =>
        ProcessConfiguration.ApplyCulturePreview(Environment.GetEnvironmentVariable("RACKS_LANG"), culture =>
        {
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Properties.Lang.Culture = culture;
        });

    // Tell WPF which language the text is in (the UI language, not the regional format), so CJK text picks the right
    // glyph forms and fonts. Must run before any element is created.
    public void OverrideWpfLanguage() =>
        ProcessConfiguration.TryOverrideLanguage(() =>
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag))));

    // Belt for ReDoS: cap the runtime of ANY regex without an explicit timeout, so a catastrophic-backtracking pattern (from
    // the registry or an imported layout) can never pin a thread. Explicit-timeout call sites (Util.SafeRegex) still win.
    public void SetRegexDefaultTimeout() =>
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", Util.SafeRegex.MatchTimeout);

    public void ApplySafeSvg() => Util.SafeSvg.Apply();

    public void EnableDarkMode() => Util.DarkModeHelper.EnableForApp();

    public void SetBindingTraceLevel() => PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;

    public void InitializeWorkspace() => ShellOperationGateway.Default.InitializeWorkspace();

    // Set here rather than in App.xaml so the headless modes (uninstall, second instance) never load MainWindow.
    public void SetStartupUri() => _app.StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);

    public void RaiseBaseStartup() => _raiseBaseStartup();

    public void RegisterToastActivation() => ToastNotificationManagerCompat.OnActivated += OnToastActivated;

    public void RunUninstallAnimation() => Util.LifecycleAnimations.RunUninstallAnimation(() => _app.Shutdown());

    public void Shutdown(int exitCode) => _app.Shutdown(exitCode);

    public void RestoreDesktopIcons() => ShellOperationGateway.Default.RestoreDesktopIconsIfHiddenByRacks();

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat toastArgs)
    {
        var args = ToastArguments.Parse(toastArgs.Argument);
        _app.Dispatcher.InvokeAsync(async () =>
        {
            if (args.Contains("action") && args["action"] == "install_update")
            {
                await UpdateComposition.Service.InstallAsync();
            }
        });
    }
}
