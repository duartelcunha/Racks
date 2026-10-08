namespace Racks.Core;

/// <summary>Where Magic Organize puts the racks it creates. All values are WPF units (DIPs), the unit of Instance.PosX/PosY/Width/Height.</summary>
public static class MagicOrganizeLayout
{
    public const double RackWidth = 300;
    public const double RackHeight = 380;
    public const double Gap = 30;

    /// <summary>
    /// A centred grid of <paramref name="count"/> racks inside the working area. The working area
    /// comes from the screen in physical pixels, so it is converted with <paramref name="dpiScale"/>
    /// (for example 1.5 at 150%) first. Using the pixel numbers directly as positions put the racks
    /// in the wrong place on any scaled display.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Grid(
        int count, double workLeftPx, double workTopPx, double workWidthPx, double workHeightPx, double dpiScale)
    {
        if (count <= 0) return Array.Empty<(double, double)>();
        if (dpiScale <= 0) dpiScale = 1;

        double left = workLeftPx / dpiScale, top = workTopPx / dpiScale;
        double width = workWidthPx / dpiScale, height = workHeightPx / dpiScale;

        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        int rows = (int)Math.Ceiling((double)count / cols);

        double totalWidth = cols * RackWidth + (cols - 1) * Gap;
        double totalHeight = rows * RackHeight + (rows - 1) * Gap;

        double startX = left + (width - totalWidth) / 2;
        double startY = top + (height - totalHeight) / 2;

        // Too big for the screen: start near the top-left corner instead of off-screen.
        if (startX < left) startX = left + 50;
        if (startY < top) startY = top + 50;

        var slots = new List<(double, double)>(count);
        for (int i = 0; i < count; i++)
            slots.Add((startX + (i % cols) * (RackWidth + Gap), startY + (i / cols) * (RackHeight + Gap)));
        return slots;
    }
}
