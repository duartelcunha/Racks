using System.Windows.Forms;
using Racks.UseCases.HotCorner;

namespace Racks.Infrastructure.Shell;

/// <summary>The real pointer and primary monitor for <see cref="HotCornerService"/>. Throws (inside the service's try) when there is no primary screen, as before.</summary>
public sealed class ScreenHotCornerProbe : IHotCornerProbe
{
    public (int X, int Y) CursorPosition
    {
        get { var pt = Cursor.Position; return (pt.X, pt.Y); }
    }

    public (int Right, int Top) PrimaryTopRight
    {
        get { var primary = Screen.PrimaryScreen!.Bounds; return (primary.Right, primary.Top); }
    }
}
