using Svg;

namespace Racks.Util
{
    // INV-IMPORT-4: an .svg in a rack or on the Desktop is untrusted input, and the thumbnailer
    // renders it without asking. Svg 3.4.7 resolves external images and external elements by
    // default (ExternalType.Local | Remote), so <image xlink:href="http://host/x.png"> or
    // href="\\host\share\x.png" or <use xlink:href="file://host/share/a.svg#x"> would make Racks
    // fetch from the network the moment the file is shown: a tracking beacon, and for UNC/file
    // URIs the user's NTLM hash. Nothing outside the file is ever resolved: no external images,
    // no external elements, no external XML entities, and the DTD is not processed.
    // These are process-wide statics of the library. Apply() runs at startup and again before
    // every parse, so nothing can leave them switched back on.
    public static class SafeSvg
    {
        public static void Apply()
        {
            SvgDocument.ResolveExternalImages = ExternalType.None;
            SvgDocument.ResolveExternalElements = ExternalType.None;
            SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
            SvgDocument.DisableDtdProcessing = true;
        }

        public static bool IsApplied =>
            SvgDocument.ResolveExternalImages == ExternalType.None
            && SvgDocument.ResolveExternalElements == ExternalType.None
            && SvgDocument.ResolveExternalXmlEntites == ExternalType.None
            && SvgDocument.DisableDtdProcessing;

        // The only way Racks parses an SVG file.
        public static SvgDocument Open(string path)
        {
            Apply();
            return SvgDocument.Open<SvgDocument>(path);
        }
    }
}
