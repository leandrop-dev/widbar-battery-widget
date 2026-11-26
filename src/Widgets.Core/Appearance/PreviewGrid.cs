namespace Widgets.Core.Appearance;

/// <summary>
/// Where each device goes in a layout with rows (Fill capsules and Icon matrix). Devices fill a column from top to
/// bottom before starting the next one, so with four devices the first column holds the first two, as in System Metrics.
/// </summary>
public static class PreviewGrid
{
    public static (int Column, int Row) Cell(int index, int count, int rows = 2)
    {
        rows = Math.Max(1, rows);
        return (index / rows, index % rows);
    }

    public static int Columns(int count, int rows = 2)
    {
        rows = Math.Max(1, rows);
        return count <= 0 ? 0 : (count + rows - 1) / rows;
    }
}
