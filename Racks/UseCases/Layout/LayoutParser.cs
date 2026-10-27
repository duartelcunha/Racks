using System.IO;
using System.Text;
using System.Text.Json;

namespace Racks.UseCases.Layout;

/// <summary>
/// Layout bytes or text to an untrusted <see cref="LayoutDocument"/>, within the limits of <see cref="LayoutLimits"/>
/// (INV-IMPORT-5). Pure and strict about shape only: it refuses a file that is too big, too deeply nested, not JSON or
/// holds too many racks or entries, and says nothing about what the values mean (that is <see cref="LayoutValidator"/>).
/// Nothing here throws for bad input; the reason comes back in <see cref="LayoutParseResult.Error"/>.
/// </summary>
public static class LayoutParser
{
    internal const string NotJson = "The layout file is not valid JSON.";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reads a file with a bounded read: the length is checked first, and the read itself stops one byte past the cap, so a
    /// file that grows, lies about its size or is not a regular file can never be pulled into memory whole.
    /// </summary>
    public static LayoutParseResult ReadFile(string path, int maxBytes = LayoutLimits.MaxFileBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (stream.CanSeek && stream.Length > maxBytes) return LayoutParseResult.Fail(TooLarge(maxBytes));

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > maxBytes) return LayoutParseResult.Fail(TooLarge(maxBytes));
                buffer.Write(chunk, 0, read);
            }
            return Parse(buffer.ToArray(), maxBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException or ArgumentException)
        {
            return LayoutParseResult.Fail($"The layout file could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses file content. A UTF-8, UTF-16 or UTF-32 byte order mark is honored (like <c>File.ReadAllText</c>); with none the
    /// text is UTF-8.
    /// </summary>
    public static LayoutParseResult Parse(byte[] bytes, int maxBytes = LayoutLimits.MaxFileBytes)
    {
        if (bytes.Length > maxBytes) return LayoutParseResult.Fail(TooLarge(maxBytes));
        if (bytes.Length == 0) return LayoutParseResult.Fail("The layout file is empty.");
        string text;
        using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();
        return ParseText(text);
    }

    /// <summary>Parses text that is already in memory (the size cap applies to its UTF-8 form).</summary>
    public static LayoutParseResult Parse(string json, int maxBytes = LayoutLimits.MaxFileBytes)
    {
        if (json == null) return LayoutParseResult.Fail(NotJson);
        // A string cannot be longer in bytes than three times its length: skip counting for the common small case.
        if (json.Length > maxBytes / 3 && Encoding.UTF8.GetByteCount(json) > maxBytes) return LayoutParseResult.Fail(TooLarge(maxBytes));
        return ParseText(json);
    }

    private static LayoutParseResult ParseText(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);

        // Pass 1: syntax and depth, token by token, without building anything. Only a file that passes is deserialized.
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 64 });
            while (reader.Read())
                if (reader.CurrentDepth > LayoutLimits.MaxJsonDepth)
                    return LayoutParseResult.Fail($"The layout file is nested too deeply (more than {LayoutLimits.MaxJsonDepth} levels).");
        }
        catch (JsonException)
        {
            return LayoutParseResult.Fail(NotJson);
        }

        LayoutDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<LayoutDocument>(utf8, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return LayoutParseResult.Fail(NotJson);
        }

        // A JSON "null" is a file with no racks, as it always was.
        document ??= new LayoutDocument { Racks = null };

        if (document.Racks != null && document.Racks.Count > LayoutLimits.MaxRacks)
            return LayoutParseResult.Fail($"The layout file holds {document.Racks.Count} racks; the most Racks reads is {LayoutLimits.MaxRacks}.");

        foreach (var rack in document.Racks ?? [])
        {
            if (rack == null) continue;   // reported by the validator, with the rest of the rack rules
            if ((rack.Values?.Count ?? 0) > LayoutLimits.MaxEntriesPerSection || (rack.MultiStringValues?.Count ?? 0) > LayoutLimits.MaxEntriesPerSection)
                return LayoutParseResult.Fail($"A rack in the layout file has more than {LayoutLimits.MaxEntriesPerSection} entries.");
        }
        return new LayoutParseResult(document, null);
    }

    private static string TooLarge(int maxBytes) =>
        maxBytes >= 1024 * 1024
            ? $"The layout file is too large (the limit is {maxBytes / (1024 * 1024)} MB)."
            : $"The layout file is too large (the limit is {maxBytes} bytes).";
}
