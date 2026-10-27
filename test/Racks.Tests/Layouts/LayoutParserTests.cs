using System.IO;
using System.Text;
using Racks.UseCases.Layout;

namespace Racks.Tests.Layouts;

// INV-IMPORT-5: the parser bounds size, depth and counts BEFORE anything is turned into objects. Nothing here touches the registry.
public sealed class LayoutParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RacksLayoutParser-" + Guid.NewGuid().ToString("N"));

    public LayoutParserTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(byte[] bytes, string name = "layout.json")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Padded(string json, int totalBytes) =>
        Encoding.UTF8.GetBytes(json + new string(' ', totalBytes - Encoding.UTF8.GetByteCount(json)));

    // ---- size ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_one_byte_over_the_limit_is_refused_without_being_parsed()
    {
        var result = LayoutParser.ReadFile(Write(Padded("{\"Racks\":[]}", LayoutLimits.MaxFileBytes + 1)));
        Assert.False(result.Ok);
        Assert.Contains("too large", result.Error);
        Assert.Contains("2 MB", result.Error);
    }

    [Fact]
    public void A_file_exactly_at_the_limit_is_read()
    {
        var result = LayoutParser.ReadFile(Write(Padded("{\"Racks\":[]}", LayoutLimits.MaxFileBytes)));
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void The_limit_can_be_lowered_for_a_caller_and_the_message_says_bytes()
    {
        var result = LayoutParser.ReadFile(Write(Encoding.UTF8.GetBytes("{\"Racks\":[]}")), maxBytes: 5);
        Assert.False(result.Ok);
        Assert.Contains("5 bytes", result.Error);
    }

    [Fact]
    public void Text_longer_than_the_limit_in_utf8_is_refused()
    {
        string json = "{\"ExportedBy\":\"" + new string('\u00e9', LayoutLimits.MaxFileBytes / 2 + 10) + "\"}";   // 2 bytes per char
        Assert.Contains("too large", LayoutParser.Parse(json).Error);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_a_result_not_an_exception()
    {
        var missing = LayoutParser.ReadFile(Path.Combine(_dir, "nope.json"));
        Assert.False(missing.Ok);
        Assert.Contains("could not be read", missing.Error);

        var directory = LayoutParser.ReadFile(_dir);   // a folder is not a file
        Assert.False(directory.Ok);
    }

    [Fact]
    public void A_file_in_use_for_writing_by_another_program_is_a_result()
    {
        string path = Write(Encoding.UTF8.GetBytes("{}"));
        using var lockedForWrite = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.False(LayoutParser.ReadFile(path).Ok);
    }

    // ---- depth and counts ---------------------------------------------------------------------------------------------

    private static string Nested(int levels) =>
        "{\"Racks\":[{\"Name\":\"a\",\"Values\":{\"PosX\":" + new string('[', levels) + "1" + new string(']', levels) + "}}]}";

    [Fact]
    public void Json_nested_deeper_than_the_limit_is_refused_and_a_real_layout_depth_is_not()
    {
        Assert.True(LayoutParser.Parse(Nested(1)).Ok);   // a list in a value: still within the limit
        var tooDeep = LayoutParser.Parse(Nested(LayoutLimits.MaxJsonDepth + 1));
        Assert.False(tooDeep.Ok);
        Assert.Contains("nested too deeply", tooDeep.Error);
    }

    [Fact]
    public void Absurd_nesting_is_refused_cheaply_and_does_not_overflow_the_stack()
    {
        string bomb = "{\"Racks\":" + new string('[', 200_000) + new string(']', 200_000) + "}";
        var result = LayoutParser.Parse(bomb);
        Assert.False(result.Ok);
        Assert.Contains("nested too deeply", result.Error);
    }

    private static string Racks(int count) =>
        "{\"Racks\":[" + string.Join(",", Enumerable.Range(0, count).Select(i => "{\"Name\":\"r" + i + "\"}")) + "]}";

    [Fact]
    public void More_racks_than_the_limit_are_refused()
    {
        Assert.True(LayoutParser.Parse(Racks(LayoutLimits.MaxRacks)).Ok);
        var result = LayoutParser.Parse(Racks(LayoutLimits.MaxRacks + 1));
        Assert.False(result.Ok);
        Assert.Contains("501 racks", result.Error);
    }

    [Fact]
    public void A_rack_with_more_entries_than_the_limit_is_refused()
    {
        static string Values(int n) => string.Join(",", Enumerable.Range(0, n).Select(i => $"\"Unknown{i}\":1"));
        Assert.True(LayoutParser.Parse("{\"Racks\":[{\"Name\":\"a\",\"Values\":{" + Values(LayoutLimits.MaxEntriesPerSection) + "}}]}").Ok);
        var result = LayoutParser.Parse("{\"Racks\":[{\"Name\":\"a\",\"Values\":{" + Values(LayoutLimits.MaxEntriesPerSection + 1) + "}}]}");
        Assert.False(result.Ok);
        Assert.Contains("more than 256 entries", result.Error);
    }

    // ---- encodings and garbage ---------------------------------------------------------------------------------------

    private const string Small = "{\"Version\":1,\"Racks\":[{\"Name\":\"\u00dcn\u00ef\",\"Values\":{\"TitleText\":\"h\u00e9llo\"}}]}";

    [Fact]
    public void A_utf8_file_with_or_without_a_byte_order_mark_is_read()
    {
        foreach (var encoding in new[] { new UTF8Encoding(false), new UTF8Encoding(true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Small)).ToArray();
            var result = LayoutParser.ReadFile(Write(bytes));
            Assert.True(result.Ok, result.Error);
            Assert.Equal("\u00dcn\u00ef", Assert.Single(result.Document!.Racks!)!.Name);
        }
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void A_file_with_a_utf16_or_utf32_byte_order_mark_is_read_like_before(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Small)).ToArray();
        var result = LayoutParser.ReadFile(Write(bytes));
        Assert.True(result.Ok, result.Error);
        Assert.Equal("\u00dcn\u00ef", Assert.Single(result.Document!.Racks!)!.Name);
    }

    [Fact]
    public void Garbage_and_empty_input_are_refused_with_a_message()
    {
        Assert.Contains("empty", LayoutParser.ReadFile(Write([])).Error);
        Assert.Equal(LayoutParser.NotJson, LayoutParser.ReadFile(Write([0xFF, 0x00, 0xFE, 0x13, 0x37, 0x80])).Error);
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("   \r\n ").Error);
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{ not json").Error);
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{} trailing").Error);
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{\"Racks\":[],}").Error);        // trailing comma
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{\"Racks\":[] /* c */}").Error); // comment
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{\"Version\":\"1\"}").Error);    // wrong type, as before
        Assert.Equal(LayoutParser.NotJson, LayoutParser.Parse("{\"ExportedAt\":\"yesterday\"}").Error);
    }

    [Fact]
    public void A_json_null_is_a_file_with_no_racks_as_it_always_was()
    {
        var result = LayoutParser.Parse("null");
        Assert.True(result.Ok);
        Assert.Null(result.Document!.Racks);
    }

    [Fact]
    public void Property_names_are_read_without_regard_to_case()
    {
        var rack = Assert.Single(LayoutParser.Parse("{\"racks\":[{\"NAME\":\"a\",\"values\":{\"Folder\":\"C:\\\\x\"}}]}").Document!.Racks!)!;
        Assert.Equal("a", rack.Name);
        Assert.Single(rack.Values!);
    }
}
