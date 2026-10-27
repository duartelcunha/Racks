using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Racks.UseCases.Layout;

namespace Racks.Util
{
    // The old entry points of layout import, kept as a thin facade for the tests that pin what a layout file may carry.
    // The work is in Racks.UseCases.Layout (parser, validator, security policy, executor, exporter) and the dialogs in
    // Racks.Views.LayoutDialogs (P9). Nothing here touches the registry or the file system.
    internal static class RackLayoutIO
    {
        // Parse a layout and return its racks validated and normalized, or throw InvalidDataException with the reason
        // the whole file is refused (INV-IMPORT-1, -5). Pure: no registry access.
        internal static List<ValidatedRack> ParseAndValidate(string json)
        {
            var parsed = LayoutParser.Parse(json);
            if (!parsed.Ok) throw new InvalidDataException(parsed.Error);
            var validated = LayoutValidator.Validate(parsed.Document!);
            if (!validated.Ok) throw new InvalidDataException(validated.Error);
            return validated.Racks!.ToList();
        }

        internal static bool IsSafeImportedPath(string? value) => LayoutSecurityPolicy.IsSafeImportedPath(value);

        // For the tests that keep this list equal to the schema.
        internal static IEnumerable<string> AllowedValueNames => LayoutSecurityPolicy.AllowedValueNames;
    }
}
