using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Racks.Core;

namespace Racks.Desktop;

public sealed class HomeWindow : Window
{
    private readonly App app;
    private readonly Session session;
    private readonly ContentControl page = new();
    private readonly TextBlock status = Ui.Text("Ready", 12, true);
    private string currentPage = "Racks";
    private readonly Dictionary<string, Button> navigationButtons = new();
    public HomeWindow(App app)
    {
        this.app = app; session = app.Session;
        Title = "Racks"; Width = 1020; Height = 740; MinWidth = 820; MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var navigation = Ui.Stack(Ui.Text("RACKS", 27), Ui.Text("A place for everything.", 12, true)); navigation.Spacing = 12;
        foreach (var name in new[] { "Racks", "Routing", "Settings", "Recovery" })
        {
            var button = Ui.Button(name, () => Navigate(name)); button.Classes.Add("nav"); navigationButtons.Add(name, button); navigation.Children.Add(button);
        }
        navigation.Children.Add(new Border { Height = 20 });
        navigation.Children.Add(Ui.Button("Quick Finder  ⌘ / Ctrl K", () => new FinderWindow(session).Show(this)));
        navigation.Children.Add(Ui.AsyncButton("Undo last operation", this, session.UndoAsync));
        navigation.Children.Add(Ui.Button("Quit Racks", app.Exit));
        var sidebar = new Border { Padding = new Thickness(24, 30), BorderBrush = Brush.Parse("#307F8E93"), BorderThickness = new Thickness(0, 0, 1, 0), Child = navigation };
        sidebar.Classes.Add("sidebar");
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRowSpan(sidebar, 2); body.Children.Add(sidebar);
        page.Margin = new Thickness(30); Grid.SetColumn(page, 1); body.Children.Add(page);
        var cancel = Ui.Button("Cancel operation", () => session.CurrentOperation?.Cancel()); cancel.IsVisible = false;
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(30, 0, 30, 18) };
        footer.Children.Add(status); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel); Grid.SetRow(footer, 1); Grid.SetColumn(footer, 1); body.Children.Add(footer);
        Content = body;
        session.PropertyChanged += (_, e) => { status.Text = session.Status; cancel.IsVisible = session.CurrentOperation != null; if (e.PropertyName == nameof(Session.Items) && currentPage == "Racks") ShowRacks(); };
        session.RacksChanged += () => { if (currentPage == "Racks") ShowRacks(); };
        Closing += (_, e) => { if (session.CurrentOperation != null) { e.Cancel = true; session.Status = "Finish or cancel the file operation before quitting."; } else app.Exit(); };
        KeyDown += (_, e) => { if (e.Key == Key.K && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { new FinderWindow(session).Show(this); e.Handled = true; } };
        Navigate(session.ReadOnly ? "Recovery" : "Racks");
    }
    private void Navigate(string name)
    {
        currentPage = name;
        foreach (var (key, button) in navigationButtons) button.Classes.Set("active", key == name);
        page.Content = name switch { "Routing" => new RoutingView(this, session), "Settings" => new SettingsView(this, app), "Recovery" => new RecoveryView(this, session), _ => null };
        if (name == "Racks") ShowRacks();
    }
    private void ShowRacks()
    {
        var header = Ui.Stack(Ui.Text("Your desktop, in order.", 30), Ui.Text("Drop files into a rack. Keep the things you need within reach.", 14, true));
        if (session.SafeMode) header.Children.Add(Ui.Text("Safe mode: routing and desktop attachment are paused. Open Recovery for details.", 13));
        var actions = Ui.Row(Ui.AsyncButton("+ Create rack", this, async () => { var rack = await Ui.NewRack(this, session); if (rack != null) app.ShowRack(rack); }, true),
            Ui.AsyncButton("Add folder", this, async () => { var rack = await Ui.NewRack(this, session, true); if (rack != null) app.ShowRack(rack); }),
            Ui.Button("Organize…", () => new OrganizeWindow(session).Show(this)));
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 20 };
        grid.Children.Add(header); Grid.SetRow(actions, 1); grid.Children.Add(actions);
        if (session.Settings.Racks.Count == 0)
        {
            var empty = Ui.Card(Ui.Stack(Ui.Text("Make room for what matters.", 23), Ui.Text("Create your first rack, then drag in a few files. Files remain on your computer, outside the application folder."), Ui.Text("Folder racks display an existing folder without moving its contents. You can return items to Desktop or undo the last operation after a restart.", 13, true)));
            empty.VerticalAlignment = VerticalAlignment.Top; Grid.SetRow(empty, 2); grid.Children.Add(empty);
        }
        else
        {
            var list = new ListBox { ItemsSource = session.Settings.Racks.ToArray(), Background = Brushes.Transparent };
            list.Classes.Add("rack-list");
            list.ItemTemplate = new FuncDataTemplate<RackDefinition>((rack, _) =>
            {
                if (rack == null) return new Border();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8) };
                row.Children.Add(Ui.Stack(Ui.Text(rack.Title, 19), Ui.Text($"{session.Items.Count(x => x.RackId == rack.Id)} items · {(rack.Kind == RackKind.Folder ? "Folder rack" : "Rack")}", 12, true)));
                var buttons = Ui.Row(Ui.Button("Open", () => app.ShowRack(rack)), Ui.AsyncButton("Edit", this, () => RackEditor.Show(this, session, rack)), Ui.AsyncButton("Remove", this, async () =>
                {
                    if (!await Ui.Confirm(this, "Remove “" + rack.Title + "”?", rack.Kind == RackKind.Owned ? "Files will be returned to Desktop. The rack stays if any file cannot be returned." : "This removes the rack window. The folder and its contents remain where they are.", "Remove rack")) return;
                    var collision = rack.Kind == RackKind.Owned ? await Ui.Collision(this) : CollisionChoice.Skip;
                    if (collision != null) await session.RemoveRackAsync(rack, collision.Value);
                }));
                Grid.SetColumn(buttons, 1); row.Children.Add(buttons); return row;
            });
            Grid.SetRow(list, 2); grid.Children.Add(list);
        }
        var tools = Ui.Row(Ui.Button("Reset positions", app.ResetPositions), Ui.AsyncButton("Refresh folders", this, async () => { session.RebuildWatchers(); await session.RefreshAsync(); }));
        Grid.SetRow(tools, 3); grid.Children.Add(tools); page.Content = grid;
    }
}

public sealed class FinderWindow : Window
{
    public FinderWindow(Session session)
    {
        Title = "Quick Finder"; Width = 670; Height = 490; MinWidth = 480; MinHeight = 320; Padding = new Thickness(24); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var input = Ui.Input("Search filenames across your racks"); var count = Ui.Text("", 12, true);
        var list = new ListBox { Background = Brushes.Transparent };
        list.ItemTemplate = new FuncDataTemplate<CatalogItem>((item, _) => item == null ? null : Ui.Stack(Ui.Text(item.Name, 16), Ui.Text(item.Detail, 12, true)));
        void Search() { var items = FileCatalog.Search(session.Items, input.Text ?? "").Take(200).ToArray(); list.ItemsSource = items; count.Text = $"{items.Length} matches shown · filenames in registered racks only"; }
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); Search(); };
        input.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        async Task Open() { if (list.SelectedItem is CatalogItem item) { try { session.Platform.Open(item.Path); } catch (Exception ex) { await Ui.Error(this, ex); } } }
        list.DoubleTapped += async (_, _) => await Open();
        var actions = Ui.Row(Ui.AsyncButton("Open", this, Open, true), Ui.AsyncButton("Reveal", this, () => { if (list.SelectedItem is CatalogItem item) session.Platform.Reveal(item.Path); return Task.CompletedTask; }));
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 14 };
        var controls = new Control[] { Ui.Text("Find your files.", 26), input, list, count, actions };
        for (var i = 0; i < controls.Length; i++) { Grid.SetRow(controls[i], i); grid.Children.Add(controls[i]); }
        Content = grid; Opened += (_, _) => { input.Focus(); Search(); };
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(Session.Items)) Search(); }; session.PropertyChanged += changed;
        Closed += (_, _) => { timer.Stop(); session.PropertyChanged -= changed; };
        KeyDown += async (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) { await Open(); e.Handled = true; } else if (e.Key == Key.Down && input.IsFocused) { list.SelectedIndex = 0; list.Focus(); e.Handled = true; } };
    }
}
