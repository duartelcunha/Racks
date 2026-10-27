using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Racks.Properties;
using Racks.Util;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace Racks.Views
{
    // The editor for a rack's auto-route rule. Only an entry made here is held to SafeRegex.IsAcceptable
    // (INV-ROUTE-1): a pattern that is invalid or too slow is not saved, the user is told, and the dialog comes back
    // with what was typed. What is already stored (registry, import) is never rewritten by this check.
    internal static class AutoRouteRuleDialog
    {
        // Returns the rule to store ("" clears it), or null when the user cancelled.
        public static async Task<string?> EditAsync(string current)
        {
            string typed = current;
            while (true)
            {
                var input = new System.Windows.Controls.TextBox { Text = typed, MinWidth = 280, Margin = new Thickness(0, 8, 0, 0) };
                var help = new TextBlock
                {
                    Text = "Regex matched against file names on the Desktop. " +
                           "Examples:  \\.png$   ^report-.*\\.pdf$   .*screenshot.*",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var panel = new StackPanel();
                panel.Children.Add(help);
                panel.Children.Add(input);
                var dlg = new MessageBox
                {
                    Title = "Auto-route rule",
                    Content = panel,
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Clear rule",
                    CloseButtonText = "Cancel",
                };
                var res = await dlg.ShowDialogAsync();
                if (res == Wpf.Ui.Controls.MessageBoxResult.Secondary) return "";
                if (res != Wpf.Ui.Controls.MessageBoxResult.Primary) return null;

                typed = input.Text ?? "";
                if (IsSavable(typed)) return typed;
                RacksMessageBox.Show(Lang.AutoRoute_Invalid_Message, Lang.AutoRoute_Invalid_Title);
            }
        }

        // Empty clears the rule; anything else must compile and survive SafeRegex's canary inputs.
        internal static bool IsSavable(string text)
            => text.Length == 0 || SafeRegex.IsAcceptable(text, RegexOptions.IgnoreCase);
    }
}
