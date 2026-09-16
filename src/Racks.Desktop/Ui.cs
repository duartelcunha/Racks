using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Racks.Core;

namespace Racks.Desktop;

internal static class Ui
{
    public static readonly IBrush Mint = Brush.Parse("#58C4AD");
    public static TextBlock Text(string text, double size = 14, bool muted = false) => new() { Text = text, FontSize = size, Opacity = muted ? .68 : 1, TextWrapping = TextWrapping.Wrap };
    public static StackPanel Stack(params Control[] children) { var panel = new StackPanel { Spacing = 12 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params Control[] children) { var panel = Stack(children); panel.Orientation = Orientation.Horizontal; panel.Spacing = 8; return panel; }
    public static Border Card(Control content) { var border = new Border { Child = content }; border.Classes.Add("card"); return border; }
    public static TextBox Input(string name, string value = "") { var box = new TextBox { Text = value, PlaceholderText = name }; AutomationProperties.SetName(box, name); return box; }
    public static Control Field(string label, Control input) => Stack(Text(label, 12, true), input);
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label }; AutomationProperties.SetName(button, label);
        if (primary) button.Classes.Add("primary");
        button.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(button) as Window;
            try { action(); }
            catch (Exception ex)
            {
                if (owner == null) throw;
                await Error(owner, ex);
            }
        };
        return button;
    }
    public static Button AsyncButton(string label, Window owner, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label }; AutomationProperties.SetName(button, label);
        if (primary) button.Classes.Add("primary");
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { await Error(owner, ex); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static Window Dialog(string title, double width = 460) => new() { Title = title, Width = width, MinWidth = 340, MaxHeight = 760, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Padding = new Thickness(24) };
    public static async Task Message(Window owner, string title, string message)
    {
        var window = Dialog(title); window.Content = Stack(Text(title, 23), Text(message), Button("OK", () => window.Close(), true)); await window.ShowDialog(owner);
    }
    public static Task Error(Window owner, Exception ex) => Message(owner, "Couldn’t finish", ex.Message);
    public static async Task<string?> Prompt(Window owner, string title, string label, string value = "")
    {
        var window = Dialog(title); var input = Input(label, value);
        var save = Button("Save", () => { if (!string.IsNullOrWhiteSpace(input.Text)) window.Close(input.Text.Trim()); }, true); save.IsDefault = true;
        var cancel = Button("Cancel", () => window.Close()); cancel.IsCancel = true;
        window.Content = Stack(Text(title, 23), Field(label, input), Row(cancel, save)); window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await window.ShowDialog<string?>(owner);
    }
    public static async Task<bool> Confirm(Window owner, string title, string description, string accept)
    {
        var window = Dialog(title); var cancel = Button("Cancel", () => window.Close(false)); cancel.IsCancel = true;
        window.Content = Stack(Text(title, 23), Text(description), Row(cancel, Button(accept, () => window.Close(true), true)));
        return await window.ShowDialog<bool>(owner);
    }
    public static async Task<CollisionChoice?> Collision(Window owner)
    {
        var window = Dialog("Files with the same name");
        window.Content = Stack(Text("If a name already exists…", 23), Text("Keep both adds a number to the new filename. Skip leaves the original where it is."), Row(Button("Cancel", () => window.Close()), Button("Skip", () => window.Close((CollisionChoice?)CollisionChoice.Skip)), Button("Keep both", () => window.Close((CollisionChoice?)CollisionChoice.KeepBoth), true)));
        return await window.ShowDialog<CollisionChoice?>(owner);
    }
    public static async Task<string?> Folder(Window owner, string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    public static async Task<RackDefinition?> NewRack(Window owner, Session session, bool folder = false)
    {
        var path = folder ? await Folder(owner, "Choose a folder to display") : null;
        if (folder && path == null) return null;
        var title = await Prompt(owner, folder ? "Add folder rack" : "Create a rack", "Rack name", folder ? Path.GetFileName(path) ?? "" : "");
        return title == null ? null : session.CreateRack(title, folder ? RackKind.Folder : RackKind.Owned, path);
    }
}
