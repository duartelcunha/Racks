using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Racks.Core;

namespace Racks.Desktop;

public sealed class RackWindow : Window
{
    private readonly App app;
    private readonly Session session;
    public RackDefinition Rack { get; }
    private readonly ListBox list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock title = Ui.Text("", 16);
    private readonly TextBlock count = Ui.Text("", 11, true);
    private readonly Border surface = new();
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer integrationTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private CatalogItem[] items = [];
    private bool appClosing, restoring, attached, dirty;
    private int reconnects, columns = 2;
    private Point? dragStart;
    private PointerPressedEventArgs? dragEvent;
    private bool dragging;

    public RackWindow(App app, RackDefinition rack)
    {
        this.app = app; session = app.Session; Rack = rack;
        Title = rack.Title; WindowDecorations = WindowDecorations.None; ShowInTaskbar = true;
        MinWidth = 280; MinHeight = 68; Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(18, 14, 12, 10) };
        header.Children.Add(Ui.Stack(title, count));
        var collapse = Ui.Button("−", () => { rack.Collapsed = !rack.Collapsed; ApplyAppearance(); SaveSoon(); });
        AutomationProperties.SetName(collapse, "Collapse or expand rack"); collapse.Classes.Add("quiet"); Grid.SetColumn(collapse, 1); header.Children.Add(collapse);
        var menuButton = Ui.Button("•••", () => { }); menuButton.Classes.Add("quiet"); AutomationProperties.SetName(menuButton, "Rack actions"); Grid.SetColumn(menuButton, 2); header.Children.Add(menuButton);
        var menu = new ContextMenu();
        menu.ItemsSource = new[] {
            Menu("Manage racks", () => { app.ShowHome(); return Task.CompletedTask; }),
            Menu("Add files…", AddFilesAsync),
            Menu("Create shortcuts…", () => AddFilesAsync(true)),
            Menu("Return selected to Desktop", ReturnSelectedAsync),
            Menu("Undo last operation", session.UndoAsync),
            Menu("Grid / list", () => { rack.ListView = !rack.ListView; RenderItems(); SaveSoon(); return Task.CompletedTask; }),
            Menu("Lock / unlock position", () => { rack.Locked = !rack.Locked; ApplyAppearance(); SaveSoon(); return Task.CompletedTask; }),
            Menu("Appearance and sorting…", () => RackEditor.Show(this, session, rack)),
            Menu("Reveal folder", () => { session.Platform.Open(rack.Folder); return Task.CompletedTask; }),
            Menu("Hide rack", () => { rack.Visible = false; session.Save(); app.SyncRacks(); return Task.CompletedTask; }) };
        menuButton.Click += (_, _) => menu.Open(menuButton);
        header.PointerPressed += (_, e) => { if (!rack.Locked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button && e.Source is not ContentPresenter) BeginMoveDrag(e); };
        var footer = Ui.Text("Drop files here · Alt-drop creates shortcuts", 10, true); footer.Margin = new Thickness(16, 8, 12, 12);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") }; grid.Children.Add(header); Grid.SetRow(list, 1); grid.Children.Add(list); Grid.SetRow(footer, 2); grid.Children.Add(footer);
        surface.Child = grid; surface.CornerRadius = new CornerRadius(16); surface.BorderThickness = new Thickness(1); surface.ClipToBounds = true; Content = surface;
        var resize = new Border { Width = 16, Height = 16, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = new Cursor(StandardCursorType.BottomRightCorner) };
        Grid.SetRow(resize, 2); grid.Children.Add(resize); resize.PointerPressed += (_, e) => { if (!rack.Locked && !rack.Collapsed) BeginResizeDrag(WindowEdge.SouthEast, e); };
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => { e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) && !session.Operations.IsBusy ? ((e.KeyModifiers & KeyModifiers.Alt) != 0 ? DragDropEffects.Link : DragDropEffects.Move) : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(this, async (_, e) =>
        {
            e.Handled = true;
            try { var paths = e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray() ?? []; if (paths.Length > 0) await DropAsync(paths, (e.KeyModifiers & KeyModifiers.Alt) != 0); }
            catch (Exception ex) { await Ui.Error(this, ex); }
        });

        list.DoubleTapped += async (_, _) => { if (rack.ListView && list.SelectedItem is CatalogItem item) await OpenAsync(item); };
        KeyDown += OnKeyDown;
        PositionChanged += (_, _) => { if (!restoring) SaveSoon(); };
        SizeChanged += (_, _) => { if (!restoring) { var next = ColumnCount(); if (next != columns && !rack.ListView) { columns = next; RenderItems(); } SaveSoon(); } };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); FlushPosition(); };
        session.PropertyChanged += SessionChanged; session.SettingsChanged += ApplyAppearance;
        Opened += (_, _) => { RestorePosition(); ApplyAppearance(); RefreshItems(); if (!session.SafeMode && session.Settings.DesktopIntegration && !session.Paths.IsIsolated) ConnectDesktop(); integrationTimer.Start(); };
        integrationTimer.Tick += (_, _) =>
        {
            if (attached && !session.Platform.DesktopConnectionAlive(this))
            {
                attached = false;
                if (reconnects++ < 2) ConnectDesktop();
                else { session.Platform.Detach(this); session.Status = "Desktop connection unavailable. Racks are accessible as ordinary windows."; }
            }
            if (!Screens.All.Any(s => s.WorkingArea.Contains(session.Platform.ScreenPosition(this)))) RestorePosition();
        };
        Closing += (_, e) => { if (!appClosing) { e.Cancel = true; rack.Visible = false; FlushPosition(); session.Save(); app.SyncRacks(); } };
        Closed += (_, _) => { saveTimer.Stop(); integrationTimer.Stop(); session.PropertyChanged -= SessionChanged; session.SettingsChanged -= ApplyAppearance; };
        ApplyAppearance();
    }
    private MenuItem Menu(string label, Func<Task> action)
    {
        var menu = new MenuItem { Header = label }; menu.Click += async (_, _) => { try { await action(); } catch (Exception ex) { await Ui.Error(this, ex); } }; return menu;
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Session.Items)) RefreshItems(); }
    private void ConnectDesktop() { try { attached = session.Platform.AttachToDesktop(this); if (!attached) session.Status = "Desktop attachment unavailable. Your rack is an ordinary window."; } catch { attached = false; session.Platform.Detach(this); } }
    private void SaveSoon() { dirty = true; saveTimer.Stop(); saveTimer.Start(); }
    public void FlushPosition()
    {
        if (!dirty || restoring || session.ReadOnly) return;
        dirty = false;
        var position = session.Platform.ScreenPosition(this);
        Rack.X = position.X; Rack.Y = position.Y;
        if (!Rack.Collapsed) { Rack.Width = Math.Max(280, Bounds.Width); Rack.Height = Math.Max(120, Bounds.Height); }
        if (Rack.Snap && !Rack.Locked)
        {
            var screen = Screens.ScreenFromWindow(this)?.WorkingArea;
            if (screen is { } area)
            {
                var width = (int)(Bounds.Width * RenderScaling); var height = (int)(Bounds.Height * RenderScaling);
                if (Math.Abs(Rack.X - area.X) < 20) Rack.X = area.X;
                if (Math.Abs(Rack.X + width - area.Right) < 20) Rack.X = area.Right - width;
                if (Math.Abs(Rack.Y - area.Y) < 20) Rack.Y = area.Y;
                if (Math.Abs(Rack.Y + height - area.Bottom) < 20) Rack.Y = area.Bottom - height;
                restoring = true; session.Platform.SetScreenPosition(this, new((int)Rack.X, (int)Rack.Y)); restoring = false;
            }
        }
        try { session.Save(); } catch (Exception ex) { session.Status = "Position could not be saved: " + ex.Message; }
    }
    public void RestorePosition()
    {
        restoring = true;
        var desired = new PixelPoint((int)Rack.X, (int)Rack.Y);
        var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(desired)) ?? Screens.Primary;
        if (screen != null)
        {
            var a = screen.WorkingArea;
            Width = Math.Clamp(Rack.Width, 280, Math.Max(280, a.Width / screen.Scaling));
            Height = Rack.Collapsed ? 68 : Math.Clamp(Rack.Height, 120, Math.Max(120, a.Height / screen.Scaling));
            desired = new PixelPoint(Math.Clamp(desired.X, a.X, Math.Max(a.X, a.Right - (int)(Width * screen.Scaling))), Math.Clamp(desired.Y, a.Y, Math.Max(a.Y, a.Bottom - (int)(Height * screen.Scaling))));
        }
        session.Platform.SetScreenPosition(this, desired);
        Rack.X = desired.X; Rack.Y = desired.Y; Rack.Width = Width;
        if (!Rack.Collapsed) Rack.Height = Height;
        var nextColumns = ColumnCount();
        if (nextColumns != columns && !Rack.ListView) { columns = nextColumns; RenderItems(); }
        restoring = false; SaveSoon();
    }
    private void ApplyAppearance()
    {
        title.Text = (Rack.Locked ? "▣  " : "") + Rack.Title; Title = Rack.Title;
        FontSize = Rack.FontSize; FontFamily = new FontFamily(Rack.FontFamily);
        Foreground = ParseBrush(Rack.Foreground, "#F1F5F5");
        var color = Color.TryParse(Rack.Background, out var c) ? c : Color.Parse("#18252B");
        surface.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(Rack.Opacity, .2, 1) * 255), color.R, color.G, color.B));
        surface.BorderBrush = ParseBrush(Rack.Accent, "#58C4AD");
        CanResize = !Rack.Locked && !Rack.Collapsed; list.IsVisible = !Rack.Collapsed;
        restoring = true; Height = Rack.Collapsed ? 68 : Rack.Height; restoring = false;
        if ((!session.Settings.DesktopIntegration || session.SafeMode) && attached) { session.Platform.Detach(this); attached = false; RestorePosition(); }
        RefreshItems();
    }
    internal static IBrush ParseBrush(string input, string fallback) => new SolidColorBrush(Color.TryParse(input, out var c) ? c : Color.Parse(fallback));
    private void RefreshItems()
    {
        IEnumerable<CatalogItem> query = session.Items.Where(x => x.RackId == Rack.Id);
        query = Rack.Sort switch { "Modified" => query.OrderBy(x => x.Modified), "Type" => query.OrderBy(x => x.Kind).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase), "Size" => query.OrderBy(x => x.Size), _ => query.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase) };
        if (Rack.Descending) query = query.Reverse();
        var next = query.ToArray(); count.Text = $"{next.Length} items";
        if (items.SequenceEqual(next) && Equals(list.Tag, PresentationKey())) return;
        items = next; selected.IntersectWith(items.Select(x => x.Path)); RenderItems();
    }
    private void RenderItems()
    {
        list.Tag = PresentationKey();
        if (Rack.ListView)
        {
            list.ItemTemplate = new FuncDataTemplate<CatalogItem>((item, _) => item == null ? null : Tile(item, true));
            list.ItemsSource = items;
        }
        else
        {
            list.ItemTemplate = new FuncDataTemplate<CatalogRow>((row, _) =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns))), ColumnSpacing = 4 };
                if (row != null) for (var i = 0; i < row.Items.Length; i++) { var tile = Tile(row.Items[i], false); Grid.SetColumn(tile, i); grid.Children.Add(tile); }
                return grid;
            });
            list.ItemsSource = items.Chunk(columns).Select(row => new CatalogRow(row)).ToArray();
        }
    }
    private string PresentationKey() => $"{Rack.ListView}|{Rack.FontSize}|{Rack.Accent}|{Rack.Foreground}";
    private int ColumnCount() => Math.Clamp((int)((double.IsFinite(Width) ? Width : Bounds.Width) / Math.Max(148, Rack.FontSize * 10 + 18)), 2, 10);
    private Control Tile(CatalogItem item, bool compact)
    {
        var label = Ui.Text(item.Name, Rack.FontSize); label.MaxLines = compact ? 1 : 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.WrapWithOverflow;
        label.Foreground = ParseBrush(Rack.Foreground, "#F1F5F5");
        var symbol = Ui.Text(item.IsDirectory ? "▰" : (item.Kind.Length == 0 ? "FILE" : item.Kind[..Math.Min(5, item.Kind.Length)]), compact ? 12 : 13);
        symbol.Foreground = ParseBrush(Rack.Accent, "#58C4AD"); symbol.FontWeight = FontWeight.SemiBold;
        var content = compact ? Ui.Row(symbol, label) : Ui.Stack(symbol, label); content.Spacing = 8;
        var button = new ToggleButton { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Height = compact ? Math.Max(42, Rack.FontSize * 1.8 + 16) : Math.Max(104, Rack.FontSize * 2.8 + 40), Padding = new Thickness(10), CornerRadius = new CornerRadius(9), Background = Brushes.Transparent, IsChecked = selected.Contains(item.Path) };
        AutomationProperties.SetName(button, item.Name + ", " + item.Kind); ToolTip.SetTip(button, item.Name);
        button.Click += (_, _) => { if (button.IsChecked == true) selected.Add(item.Path); else selected.Remove(item.Path); };
        button.DoubleTapped += async (_, e) => { e.Handled = true; await OpenAsync(item); };
        button.AddHandler(KeyDownEvent, async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await OpenAsync(item); } }, RoutingStrategies.Tunnel);
        button.ContextMenu = new ContextMenu { ItemsSource = new[] { Menu("Open", () => OpenAsync(item)), Menu("Reveal", () => { session.Platform.Reveal(item.Path); return Task.CompletedTask; }), Menu("Rename…", async () => { var name = await Ui.Prompt(this, "Rename file", "Filename", item.Name); if (name != null) await session.RenameAsync(item, name); }), Menu("Return to Desktop", async () => { var choice = await Ui.Collision(this); if (choice != null) await session.ReturnAsync([item], choice.Value); }) } };
        // Buttons handle the bubbling press themselves. Observe the tunnelling
        // event so native dragging is not silently swallowed by tile selection.
        button.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { dragStart = e.GetPosition(this); dragEvent = e; } }, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerReleasedEvent, (_, _) => { dragStart = null; dragEvent = null; }, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (dragging || dragStart is not { } start || dragEvent == null || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed || Math.Sqrt(Math.Pow(e.GetPosition(this).X - start.X, 2) + Math.Pow(e.GetPosition(this).Y - start.Y, 2)) < 8) return;
            dragging = true; var press = dragEvent; dragStart = null;
            try
            {
                using var data = new DataTransfer();
                var paths = selected.Contains(item.Path) ? Selected().Select(x => x.Path).ToArray() : [item.Path];
                foreach (var path in paths)
                {
                    IStorageItem? storage = Directory.Exists(path) ? await StorageProvider.TryGetFolderFromPathAsync(new Uri(path)) : await StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                    if (storage != null) data.Add(DataTransferItem.Create(DataFormat.File, storage));
                }
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move);
                // The target owns the transfer. A drag effect is never proof that it is safe to delete.
                if (Rack.IncludedNames != null && Directory.Exists(Rack.Folder))
                {
                    foreach (var path in paths.Where(path => !SafeFiles.Exists(path))) Rack.IncludedNames.Remove(Path.GetFileName(path));
                    session.Save();
                }
                await session.RefreshAsync();
            }
            catch (Exception ex) { await Ui.Error(this, ex); }
            finally { dragging = false; dragEvent = null; }
        }, RoutingStrategies.Tunnel, true);
        return button;
    }
    private IEnumerable<CatalogItem> Selected() => items.Where(x => selected.Contains(x.Path));
    public async Task DropAsync(string[] paths, bool shortcut, CollisionChoice? choice = null)
    {
        choice ??= await Ui.Collision(this); if (choice != null) await session.MoveIntoAsync(Rack, paths, shortcut, choice.Value);
    }
    private async Task AddFilesAsync() => await AddFilesAsync(false);
    private async Task AddFilesAsync(bool shortcut)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = shortcut ? "Create shortcuts" : "Move files into this rack", AllowMultiple = true });
        var paths = files.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray(); if (paths.Length > 0) await DropAsync(paths, shortcut);
    }
    private async Task ReturnSelectedAsync() { var selection = Selected().ToArray(); if (selection.Length == 0) return; var choice = await Ui.Collision(this); if (choice != null) await session.ReturnAsync(selection, choice.Value); }
    private async Task OpenAsync(CatalogItem item) { try { session.Platform.Open(item.Path); } catch (Exception ex) { await Ui.Error(this, ex); } }
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (command && e.Key == Key.A) { foreach (var item in items) selected.Add(item.Path); RenderItems(); e.Handled = true; }
            else if (command && e.Key == Key.Z) { await session.UndoAsync(); e.Handled = true; }
            else if (command && e.Key == Key.K) { new FinderWindow(session).Show(this); e.Handled = true; }
            else if (e.Key == Key.F2 && Selected().FirstOrDefault() is { } item) { var name = await Ui.Prompt(this, "Rename file", "Filename", item.Name); if (name != null) await session.RenameAsync(item, name); e.Handled = true; }
            else if (e.Key == Key.Escape) { selected.Clear(); RenderItems(); }
        }
        catch (Exception ex) { await Ui.Error(this, ex); }
    }
    public void CloseForApp() { appClosing = true; Close(); }
    private sealed record CatalogRow(CatalogItem[] Items)
    {
        public override string ToString() => string.Join(", ", Items.Select(item => item.Name));
    }
}

internal static class RackEditor
{
    public static async Task Show(Window owner, Session session, RackDefinition rack)
    {
        var window = Ui.Dialog("Edit rack", 500);
        var title = Ui.Input("Rack name", rack.Title); var accent = Ui.Input("Accent color", rack.Accent); var background = Ui.Input("Background color", rack.Background); var foreground = Ui.Input("Text color", rack.Foreground);
        var size = new NumericUpDown { Minimum = 10, Maximum = 30, Value = (decimal)rack.FontSize, Increment = 1 }; var opacity = new Slider { Minimum = .65, Maximum = 1, Value = rack.Opacity };
        var sort = new ComboBox { ItemsSource = new[] { "Name", "Type", "Modified", "Size" }, SelectedItem = rack.Sort, HorizontalAlignment = HorizontalAlignment.Stretch };
        var descending = new CheckBox { Content = "Descending order", IsChecked = rack.Descending }; var snap = new CheckBox { Content = "Snap to screen edges", IsChecked = rack.Snap };
        var presets = Ui.Row(Ui.Button("Forest", () => { accent.Text = "#58C4AD"; background.Text = "#18252B"; foreground.Text = "#F1F5F5"; }), Ui.Button("Ink", () => { accent.Text = "#B8ADFF"; background.Text = "#1D1D28"; foreground.Text = "#F5F2FF"; }), Ui.Button("Paper", () => { accent.Text = "#316D64"; background.Text = "#F0F1EB"; foreground.Text = "#23312B"; }));
        var save = Ui.AsyncButton("Save changes", window, () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || title.Text.Trim().Length > 200) throw new IOException("Enter a rack name between 1 and 200 characters.");
            foreach (var input in new[] { accent, background, foreground }) if (!Color.TryParse(input.Text, out _)) throw new IOException("Use a color such as #58C4AD.");
            rack.Title = title.Text.Trim(); rack.Accent = accent.Text!; rack.Background = background.Text!; rack.Foreground = foreground.Text!; rack.FontSize = (double)(size.Value ?? 13); rack.Opacity = opacity.Value; rack.Sort = (string?)sort.SelectedItem ?? "Name"; rack.Descending = descending.IsChecked == true; rack.Snap = snap.IsChecked == true;
            session.SaveRackChanges(); window.Close(); return Task.CompletedTask;
        }, true);
        window.Content = new ScrollViewer { Content = Ui.Stack(Ui.Text("Make it yours.", 25), Ui.Field("Name", title), presets, Ui.Field("Accent", accent), Ui.Field("Background", background), Ui.Field("Text", foreground), Ui.Row(Ui.Field("Text size", size), Ui.Field("Sort by", sort)), Ui.Field("Background opacity", opacity), descending, snap, Ui.Row(Ui.Button("Cancel", () => window.Close()), save)) };
        await window.ShowDialog(owner);
    }
}
