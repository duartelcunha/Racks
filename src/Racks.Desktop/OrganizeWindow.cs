using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Racks.Core;

namespace Racks.Desktop;

public sealed class OrganizeWindow : Window
{
    private sealed class Entry { public string Path { get; init; } = ""; public bool Included { get; set; } = true; public override string ToString() => System.IO.Path.GetFileName(Path); }
    private sealed class Group : INotifyPropertyChanged
    {
        private string name = "";
        public string Name { get => name; set { if (name != value) { name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } } }
        public Entry[] Entries { get; init; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => Name;
    }
    public OrganizeWindow(Session session)
    {
        Title = "Organize"; Width = 760; Height = 680; MinWidth = 620; MinHeight = 450; Padding = new Thickness(28); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Group[] groups = []; Group? current = null;
        var folder = Ui.Text("Choose a folder to preview. Nothing moves until you apply.", 13, true);
        var groupList = new ListBox { Width = 205 }; var files = new ListBox(); var name = Ui.Input("Group name"); var progress = Ui.Text("", 12, true);
        groupList.ItemTemplate = new FuncDataTemplate<Group>((g, _) =>
        {
            if (g == null) return null;
            var label = Ui.Text(g.Name, 15); label.Bind(TextBlock.TextProperty, new Binding(nameof(Group.Name)) { Source = g });
            return Ui.Stack(label, Ui.Text($"{g.Entries.Length} items", 12, true));
        });
        files.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) =>
        {
            if (entry == null) return null; var check = new CheckBox { Content = Path.GetFileName(entry.Path), IsChecked = entry.Included }; check.IsCheckedChanged += (_, _) => entry.Included = check.IsChecked == true; return check;
        });
        groupList.SelectionChanged += (_, _) => { current = groupList.SelectedItem as Group; name.Text = current?.Name; files.ItemsSource = current?.Entries; };
        name.TextChanged += (_, _) => { if (current != null) current.Name = name.Text ?? ""; };
        var pick = Ui.AsyncButton("Choose folder…", this, async () =>
        {
            var path = await Ui.Folder(this, "Preview organization"); if (path == null) return;
            if (session.Settings.Racks.Any(x => SafeFiles.IsWithin(path, x.Folder) || SafeFiles.IsWithin(x.Folder, path))) throw new IOException("Choose a source folder outside your existing racks.");
            folder.Text = path;
            var preview = await Task.Run(() => FileCatalog.Organize(path));
            groups = preview.Select(g => new Group { Name = g.Name, Entries = g.Paths.Select(p => new Entry { Path = p }).ToArray() }).ToArray();
            groupList.ItemsSource = groups; groupList.SelectedIndex = groups.Length > 0 ? 0 : -1; progress.Text = $"{groups.Sum(g => g.Entries.Length)} items · uncheck anything you want to leave in place";
        });
        var apply = Ui.AsyncButton("Apply organization", this, async () =>
        {
            var selection = groups.Select(g => new OrganizeGroup { Name = g.Name, Paths = g.Entries.Where(x => x.Included).Select(x => x.Path).ToList() }).Where(g => g.Paths.Count > 0).ToArray();
            if (selection.Length == 0) { progress.Text = "Choose a folder and include at least one file."; return; }
            var collision = await Ui.Collision(this); if (collision == null) return;
            await session.OrganizeAsync(selection, collision.Value);
            groups = []; groupList.ItemsSource = groups; files.ItemsSource = null; name.Text = ""; progress.Text = session.Status;
        }, true);
        var previewGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("205,*"), ColumnSpacing = 20 };
        previewGrid.Children.Add(groupList); var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 }; right.Children.Add(name); Grid.SetRow(files, 1); right.Children.Add(files); Grid.SetColumn(right, 1); previewGrid.Children.Add(right);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), RowSpacing = 16 };
        var cancel = Ui.Button("Cancel operation", () => session.CurrentOperation?.Cancel());
        cancel.IsVisible = session.CurrentOperation != null;
        Control[] controls = [Ui.Text("A little order. A clearer desktop.", 27), Ui.Text("Files are grouped by type. Rename groups and exclude items, then apply one undoable operation.", 14, true), Ui.Stack(pick, folder), previewGrid, progress, Ui.Row(cancel, apply)];
        for (var i = 0; i < controls.Length; i++) { Grid.SetRow(controls[i], i); grid.Children.Add(controls[i]); } Content = grid;
        PropertyChangedEventHandler changed = (_, _) => { cancel.IsVisible = session.CurrentOperation != null; if (session.CurrentOperation != null) progress.Text = session.Status; }; session.PropertyChanged += changed;
        Closed += (_, _) => session.PropertyChanged -= changed;
        Closing += (_, e) => { if (session.CurrentOperation != null) { session.CurrentOperation.Cancel(); e.Cancel = true; progress.Text = "Cancelling. Wait for the current item to finish."; } };
    }
}
