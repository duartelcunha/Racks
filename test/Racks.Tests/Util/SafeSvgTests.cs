using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Racks.Tests.Guards;
using Racks.Util;
using Svg;

namespace Racks.Tests.Util;

// INV-IMPORT-4: an SVG thumbnail never resolves anything outside the file. No test here touches
// the network: remote resolution is checked through the library's own settings, and the render
// test uses a local file as the "external" image.
[Collection(nameof(SafeSvgTests))]
public class SafeSvgTests
{
    [Fact]
    public void The_resolver_settings_are_process_wide_statics_of_the_referenced_library()
    {
        // Pins the API this hardening relies on: if a Svg upgrade turned these into per-document
        // settings (or renamed them), SafeSvg.Apply would no longer cover every document.
        var t = typeof(SvgDocument);
        foreach (var name in new[] { "ResolveExternalImages", "ResolveExternalElements", "ResolveExternalXmlEntites" })
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(p);
            Assert.Equal(typeof(ExternalType), p!.PropertyType);
            Assert.True(p.CanWrite);
        }
        var dtd = t.GetProperty("DisableDtdProcessing", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(dtd);
        Assert.Equal(typeof(bool), dtd!.PropertyType);
        Assert.Equal(0, (int)ExternalType.None);
        Assert.True(typeof(ExternalType).IsDefined(typeof(FlagsAttribute), false));
    }

    [Fact]
    public void Apply_turns_off_every_external_resolution()
    {
        try
        {
            SvgDocument.ResolveExternalImages = ExternalType.Local | ExternalType.Remote;
            SvgDocument.ResolveExternalElements = ExternalType.Local | ExternalType.Remote;
            SvgDocument.ResolveExternalXmlEntites = ExternalType.Remote;
            SvgDocument.DisableDtdProcessing = false;
            Assert.False(SafeSvg.IsApplied);

            SafeSvg.Apply();

            Assert.Equal(ExternalType.None, SvgDocument.ResolveExternalImages);
            Assert.Equal(ExternalType.None, SvgDocument.ResolveExternalElements);
            Assert.Equal(ExternalType.None, SvgDocument.ResolveExternalXmlEntites);
            Assert.True(SvgDocument.DisableDtdProcessing);
            Assert.True(SafeSvg.IsApplied);
        }
        finally { SafeSvg.Apply(); }
    }

    [Fact]
    public void Open_reapplies_the_settings_and_does_not_load_an_external_image()
    {
        string dir = Path.Combine(Path.GetTempPath(), "racks-svg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string png = Path.Combine(dir, "red.png");
            using (var red = new Bitmap(4, 4))
            {
                using (var g = Graphics.FromImage(red)) g.Clear(Color.Red);
                red.Save(png, ImageFormat.Png);
            }
            string svg = Path.Combine(dir, "thumb.svg");
            File.WriteAllText(svg,
                "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='16' height='16'>" +
                $"<image x='0' y='0' width='16' height='16' xlink:href='{new Uri(png).AbsoluteUri}'/></svg>");

            // Control: with resolution on, this local "external" image IS drawn, so the check
            // below is not passing just because the image could never load.
            SvgDocument.ResolveExternalImages = ExternalType.Local;
            Assert.Equal(Color.Red.ToArgb(), CenterPixel(SvgDocument.Open<SvgDocument>(svg)));

            // SafeSvg.Open switches it off again before parsing, and nothing is drawn.
            var doc = SafeSvg.Open(svg);
            Assert.True(SafeSvg.IsApplied);
            Assert.NotEqual(Color.Red.ToArgb(), CenterPixel(doc));
        }
        finally
        {
            SafeSvg.Apply();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Svg_files_are_only_parsed_through_SafeSvg()
    {
        var call = new Regex(@"SvgDocument\s*\.\s*(Open|FromSvg)", RegexOptions.None, TimeSpan.FromSeconds(1));
        var hits = Directory.EnumerateFiles(RepoPaths.App(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) != "SafeSvg.cs")
            .Where(f => call.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void The_app_applies_the_svg_settings_at_startup()
    {
        string app = File.ReadAllText(RepoPaths.App("App.xaml.cs"));
        int startup = app.IndexOf("protected override void OnStartup", StringComparison.Ordinal);
        int apply = app.IndexOf("Racks.Util.SafeSvg.Apply();", StringComparison.Ordinal);
        int window = app.IndexOf("StartupUri = new Uri(\"MainWindow.xaml\"", StringComparison.Ordinal);
        Assert.True(startup > 0 && apply > startup && window > apply);
    }

    private static int CenterPixel(SvgDocument doc)
    {
        using var bmp = doc.Draw(16, 16);
        return bmp.GetPixel(8, 8).ToArgb();
    }
}
