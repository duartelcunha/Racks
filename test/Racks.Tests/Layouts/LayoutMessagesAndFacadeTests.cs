using System.IO;
using System.Text.RegularExpressions;
using Racks.UseCases.Layout;
using Racks.Util;

namespace Racks.Tests.Layouts;

// The dialog layer cannot run without a UI; what it shows is built here (LayoutMessages) and tested as text. The RackLayoutIO
// facade only forwards, and layout code reaches the registry only through IRackRegistry.
public sealed class LayoutMessagesAndFacadeTests
{
    [Fact]
    public void A_successful_import_with_racks_and_a_successful_export_need_no_message()
    {
        Assert.Null(LayoutMessages.ForImport(ImportResult.Imported(3)));
        Assert.Null(LayoutMessages.ForExport(ExportResult.Done(3)));
    }

    [Fact]
    public void A_file_with_no_racks_says_nothing_was_changed()
    {
        var message = LayoutMessages.ForImport(ImportResult.Imported(0))!;
        Assert.Equal("Import racks", message.Title);
        Assert.Contains("no racks", message.Text);
        Assert.Contains("Nothing was changed", message.Text);
    }

    [Fact]
    public void A_rejected_file_gives_the_reason_and_says_the_racks_are_unchanged()
    {
        var message = LayoutMessages.ForImport(ImportResult.Rejected("The layout file is too large (the limit is 2 MB)."))!;
        Assert.StartsWith("Import cancelled: The layout file is too large (the limit is 2 MB).", message.Text);
        Assert.EndsWith("Your existing racks are unchanged.", message.Text);
    }

    [Fact]
    public void A_failed_import_says_whether_the_previous_racks_were_restored()
    {
        var restored = LayoutMessages.ForImport(new ImportResult(ImportStatus.Failed, 0, "write denied", StateChanged: true, RolledBack: true))!;
        Assert.Contains("write denied", restored.Text);
        Assert.Contains("previous racks were restored", restored.Text);

        var untouched = LayoutMessages.ForImport(new ImportResult(ImportStatus.Failed, 0, "cannot read"))!;
        Assert.Contains("unchanged", untouched.Text);
        Assert.DoesNotContain("restored", untouched.Text);
    }

    [Fact]
    public void A_restore_that_failed_is_told_to_the_user_plainly_with_where_the_details_are()
    {
        var result = new ImportResult(ImportStatus.Failed, 0, "write denied", StateChanged: true, RollbackFailed: true, RollbackDetails: "could not restore 'Alpha'");
        var message = LayoutMessages.ForImport(result)!;
        Assert.Contains("write denied", message.Text);
        Assert.Contains("could not be fully restored", message.Text);
        Assert.Contains("some racks may be missing or changed", message.Text);
        Assert.Contains("audit.log", message.Text);
        Assert.DoesNotContain("were restored", message.Text);
    }

    [Fact]
    public void A_failed_export_gives_the_reason_and_says_an_existing_file_was_not_changed()
    {
        var message = LayoutMessages.ForExport(ExportResult.Fail("The folder does not exist."))!;
        Assert.Equal("Export racks", message.Title);
        Assert.Contains("The folder does not exist.", message.Text);
        Assert.Contains("existing file at that location was not changed", message.Text);
    }

    [Fact]
    public void An_unexpected_error_still_gets_a_message_for_both_directions()
    {
        var ex = new InvalidOperationException("boom");
        Assert.Contains("boom", LayoutMessages.Unexpected(import: true, ex).Text);
        Assert.Contains("boom", LayoutMessages.Unexpected(import: false, ex).Text);
        Assert.Equal("Import racks", LayoutMessages.Unexpected(import: true, ex).Title);
        Assert.Equal("Export racks", LayoutMessages.Unexpected(import: false, ex).Title);
    }

    // ---- the facade ---------------------------------------------------------------------------------------------------

    [Fact]
    public void The_facade_throws_invalid_data_with_the_reason_for_each_stage()
    {
        Assert.Equal(LayoutParser.NotJson, Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate("{ nope")).Message);
        Assert.Contains("newer version", Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate("{\"Version\":3}")).Message);
        Assert.Contains("unsafe rack name", Assert.Throws<InvalidDataException>(() => RackLayoutIO.ParseAndValidate("{\"Racks\":[{\"Name\":\"..\"}]}")).Message);
        Assert.Empty(RackLayoutIO.ParseAndValidate("{\"Racks\":[]}"));
    }

    // ---- registry access --------------------------------------------------------------------------------------------

    [Fact]
    public void Layout_code_reaches_the_registry_only_through_IRackRegistry()
    {
        // INV-REG-2: the 5 direct Registry.CurrentUser uses of the old RackLayoutIO are gone and must not come back.
        var files = Directory.EnumerateFiles(Path.Combine(Racks.Tests.Guards.RepoPaths.Root, "Racks", "UseCases", "Layout"), "*.cs")
            .Append(Path.Combine(Racks.Tests.Guards.RepoPaths.Root, "Racks", "Util", "RackLayoutIO.cs"))
            .Append(Path.Combine(Racks.Tests.Guards.RepoPaths.Root, "Racks", "Views", "LayoutDialogs.cs"))
            .Append(Path.Combine(Racks.Tests.Guards.RepoPaths.Root, "Racks", "Infrastructure", "Layout", "LayoutFileWriter.cs"));
        var offenders = files
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bRegistry\s*\.\s*(CurrentUser|LocalMachine|ClassesRoot|Users|CurrentConfig|OpenBaseKey)\b|\bRegistryKey\b"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(offenders.Count == 0, "Direct registry access in: " + string.Join(", ", offenders));
    }
}
