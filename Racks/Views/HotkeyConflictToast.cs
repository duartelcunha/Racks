using Microsoft.Toolkit.Uwp.Notifications;

namespace Racks.Views
{
    /// <summary>The toast that tells the user another app owns the quick finder and/or new-rack hotkey.</summary>
    public static class HotkeyConflictToast
    {
        public static void Show(bool quickFinderOk, bool newRackOk)
        {
            var toast = new ToastContentBuilder()
                .AddText(Racks.Properties.Lang.Hotkey_Conflict_Title);
            if (!quickFinderOk) toast.AddText(Racks.Properties.Lang.Hotkey_Conflict_QuickFinder);
            if (!newRackOk) toast.AddText(Racks.Properties.Lang.Hotkey_Conflict_NewRack);
            toast.Show();
        }
    }
}
