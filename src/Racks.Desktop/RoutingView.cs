using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Racks.Core;

namespace Racks.Desktop;

internal sealed class RoutingView : ScrollViewer
{
    private readonly Window owner;
    private readonly Session session;
    public RoutingView(Window owner, Session session) { this.owner = owner; this.session = session; Render(); }
    private void Render()
    {
        var stack = Ui.Stack(Ui.Text("A place to land.", 30), Ui.Text("Rules run in order. The first match wins. Preview files before enabling a rule; only new files are routed automatically.", 14, true));
        stack.Children.Add(Ui.Row(Ui.AsyncButton("+ Add rule", owner, () => Edit(new RoutingRule(), true), true), Ui.AsyncButton(session.Settings.RoutingPaused ? "Resume routing" : "Pause routing", owner, () => { session.Settings.RoutingPaused = !session.Settings.RoutingPaused; session.Save(); session.Router.Rebuild(); Render(); return Task.CompletedTask; })));
        if (session.SafeMode) stack.Children.Add(Ui.Text("Routing is paused in safe mode. Restart normally to enable it.", 13, true));
        if (session.Settings.Rules.Count == 0) stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text("Start with one useful rule.", 20), Ui.Text("For example: new PDFs in Downloads → your Documents rack.", 13, true))));
        foreach (var rule in session.Settings.Rules)
        {
            var destination = session.Settings.Racks.FirstOrDefault(x => x.Id == rule.DestinationRackId)?.Title ?? "Missing rack";
            stack.Children.Add(Ui.Card(Ui.Stack(Ui.Text(rule.Name, 20), Ui.Text($"{(rule.Enabled ? "Enabled" : "Paused")} · {rule.Source} → {destination}", 12, true), Ui.Text(rule.LastError, 12, true), Ui.Row(
                Ui.AsyncButton("Edit & preview", owner, () => Edit(rule, false)),
                Ui.AsyncButton("Move up", owner, () => { var index = session.Settings.Rules.IndexOf(rule); if (index > 0) { session.Settings.Rules.RemoveAt(index); session.Settings.Rules.Insert(index - 1, rule); session.Save(); session.Router.Rebuild(); Render(); } return Task.CompletedTask; }),
                Ui.AsyncButton("Remove", owner, () => { session.Settings.Rules.Remove(rule); session.Save(); session.Router.Rebuild(); Render(); return Task.CompletedTask; })))));
        }
        Content = stack;
    }
    private async Task Edit(RoutingRule original, bool isNew)
    {
        var window = Ui.Dialog("Routing rule", 560);
        var name = Ui.Input("Rule name", original.Name); var source = Ui.Input("Source folder", original.Source); var extensions = Ui.Input("Extensions, e.g. pdf, docx", original.Extensions); var contains = Ui.Input("Filename contains", original.NameContains); var regex = Ui.Input("Advanced regular expression", original.Regex);
        var destination = new ComboBox { ItemsSource = session.Settings.Racks.ToArray(), SelectedItem = session.Settings.Racks.FirstOrDefault(x => x.Id == original.DestinationRackId), HorizontalAlignment = HorizontalAlignment.Stretch, ItemTemplate = new FuncDataTemplate<RackDefinition>((rack, _) => Ui.Text(rack?.Title ?? "")) };
        var enabled = new CheckBox { Content = "Enable after saving", IsChecked = original.Enabled };
        var preview = Ui.Text("Preview the rule before enabling it.", 12, true); string? previewed = null;
        RoutingRule Read() => new() { Id = original.Id, Name = name.Text ?? "", Source = Path.GetFullPath(source.Text ?? ""), DestinationRackId = (destination.SelectedItem as RackDefinition)?.Id ?? Guid.Empty, Extensions = extensions.Text ?? "", NameContains = contains.Text ?? "", Regex = regex.Text ?? "", Enabled = enabled.IsChecked == true };
        string Fingerprint(RoutingRule rule) => $"{rule.Source}\n{rule.DestinationRackId}\n{rule.Extensions}\n{rule.NameContains}\n{rule.Regex}";
        var scan = Ui.AsyncButton("Preview matches", window, async () =>
        {
            var rule = Read(); RuleMatcher.Validate(rule, session.Settings);
            var earlier = session.Settings.Rules.TakeWhile(x => x.Id != original.Id).Where(x => x.Enabled).ToArray();
            var result = await Task.Run(() => Directory.EnumerateFiles(rule.Source).Where(path => RuleMatcher.Matches(rule, path) && !earlier.Any(r => r.Source.Equals(rule.Source, SafeFiles.PathComparison) && RuleMatcher.Matches(r, path))).ToArray());
            previewed = Fingerprint(rule); preview.Text = $"{result.Length} current files match after earlier rules. Existing files will stay in place.\n" + string.Join("\n", result.Take(6).Select(Path.GetFileName));
        });
        var save = Ui.AsyncButton("Save rule", window, () =>
        {
            var rule = Read(); RuleMatcher.Validate(rule, session.Settings);
            if (rule.Enabled && previewed != Fingerprint(rule)) throw new IOException("Preview these conditions before enabling the rule.");
            if (string.IsNullOrWhiteSpace(rule.Name)) throw new IOException("Enter a rule name.");
            if (isNew) session.Settings.Rules.Add(rule); else session.Settings.Rules[session.Settings.Rules.IndexOf(original)] = rule;
            session.Save(); session.Router.Rebuild(); Render(); window.Close(); return Task.CompletedTask;
        }, true);
        window.Content = new ScrollViewer { Content = Ui.Stack(Ui.Text("Simple rules. Predictable results.", 23), Ui.Field("Name", name), Ui.Field("Source folder", source), Ui.AsyncButton("Browse…", window, async () => { var path = await Ui.Folder(window, "Routing source"); if (path != null) source.Text = path; }), Ui.Field("Destination rack", destination), extensions, contains, new Expander { Header = "Advanced regex", Content = regex }, enabled, scan, preview, Ui.Row(Ui.Button("Cancel", () => window.Close()), save)) };
        await window.ShowDialog(owner);
    }
}
