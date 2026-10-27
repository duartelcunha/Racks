using System;
using Racks.Properties;
using Racks.UseCases.Layout;

namespace Racks.Views
{
    /// <summary>
    /// The save and open dialogs of layout export and import, and the messages after them (P9). It only asks the user for a
    /// file, calls <see cref="LayoutService"/> and shows what the result says; every failure is shown (the texts are built by
    /// <see cref="LayoutMessages"/>) and audited by the service. Nothing here decides what is allowed in a layout.
    /// </summary>
    public static class LayoutDialogs
    {
        private const string FileFilter = "Racks layout (*.json)|*.json|All files (*.*)|*.*";

        /// <summary>Asks for a file and exports the racks to it atomically. False when the user cancelled or it failed.</summary>
        public static bool Export(LayoutService? service = null)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"racks-layout-{DateTime.Now:yyyyMMdd-HHmm}.json",
                    Filter = FileFilter,
                };
                if (dialog.ShowDialog() != true) return false;
                var result = (service ?? LayoutService.Default).ExportToFile(dialog.FileName);
                Show(LayoutMessages.ForExport(result));
                return result.Ok;
            }
            catch (Exception ex)
            {
                Show(LayoutMessages.Unexpected(import: false, ex));
                return false;
            }
        }

        /// <summary>Confirms, asks for a file and imports it. Returns how many racks were imported (0 when cancelled or failed).</summary>
        public static int Import(bool replaceExisting, LayoutService? service = null)
        {
            try
            {
                if (!RacksMessageBox.Confirm(Lang.Layout_Import_Confirm, Lang.Layout_Import_Title,
                        Lang.Layout_Import_Replace, Lang.Layout_Import_Cancel)) return 0;
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = FileFilter };
                if (dialog.ShowDialog() != true) return 0;
                var result = (service ?? LayoutService.Default).ImportFile(dialog.FileName, replaceExisting);
                Show(LayoutMessages.ForImport(result));
                return result.Status == ImportStatus.Imported ? result.ImportedCount : 0;
            }
            catch (Exception ex)
            {
                Show(LayoutMessages.Unexpected(import: true, ex));
                return 0;
            }
        }

        private static void Show(LayoutMessage? message)
        {
            if (message != null) RacksMessageBox.Show(message.Text, message.Title);
        }
    }
}
