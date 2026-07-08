using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace Racks.Util
{
    // One place for the "windows fade in when they open and fade out when they close"
    // behavior so every Racks-owned window (settings, about, help, quick finder, message
    // box, previews) feels the same. Opacity-only, GPU-composited, so it stays smooth at
    // 60fps and never touches layout.
    //
    // Usage: call WindowFade.Attach(this) at the end of a window's constructor.
    //
    // Works for both modeless (.Show()) and modal (.ShowDialog()) windows. The close fade
    // cancels the first Close, plays the fade, then closes for real. WPF keeps DialogResult
    // across the cancelled Closing, so ShowDialog() still returns the caller's value.
    public static class WindowFade
    {
        private const double FadeInSeconds = 0.16;
        private const double FadeOutSeconds = 0.13;

        public static void Attach(Window window)
        {
            if (window == null) return;

            bool fading = false;    // fade-out running: swallow further close attempts
            bool readyToClose = false; // fade finished: let the next close go through
            window.Opacity = 0;

            window.Loaded += (_, _) =>
            {
                window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(FadeInSeconds),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            };

            window.Closing += (_, e) =>
            {
                if (readyToClose) return;   // fade done: allow the real close
                if (fading) { e.Cancel = true; return; } // swallow closes fired mid-fade
                fading = true;
                e.Cancel = true;

                // Modal dialogs: a ShowDialog() window that set DialogResult (e.g. "OK" does
                // `DialogResult = true; Close();`) triggers this Closing. Cancelling it RESETS
                // DialogResult to false (verified), so we must capture the intended value now
                // and re-apply it on the real close. Re-assigning a DIFFERENT value (false ->
                // true) is not a no-op, so it also re-triggers the close - which readyToClose
                // lets through. For modeless windows DialogResult is null/unsettable, so we
                // just Close().
                bool? dialogResult = null;
                try { dialogResult = window.DialogResult; } catch { }

                var fade = new DoubleAnimation
                {
                    From = window.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(FadeOutSeconds),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                fade.Completed += (_, _) =>
                {
                    readyToClose = true;
                    try
                    {
                        if (dialogResult.HasValue)
                        {
                            // Cancelling Closing reset DialogResult to false; restore the
                            // captured value. If it was already false this is a no-op that
                            // won't close, so fall back to Close() in that case.
                            if (window.DialogResult != dialogResult)
                                window.DialogResult = dialogResult;
                            else
                                window.Close();
                        }
                        else
                        {
                            window.Close();
                        }
                    }
                    catch { try { window.Close(); } catch { } }
                };
                window.BeginAnimation(UIElement.OpacityProperty, fade);
            };
        }
    }
}
