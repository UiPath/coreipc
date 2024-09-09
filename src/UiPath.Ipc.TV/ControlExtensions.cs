using BrightIdeasSoftware;

namespace UiPath.Ipc.TV;

internal static class ControlExtensions
{
    public static void Stylize(this TreeListView treeListView)
    {
        treeListView.BorderStyle = BorderStyle.None;
        treeListView.FullRowSelect = true;
        treeListView.MultiSelect = true;
        treeListView.RowHeight = 16;
        treeListView.CellPadding = new Rectangle(0, 0, 0, 0);

        treeListView.VirtualListSize = 10;

        treeListView.HeaderUsesThemes = true;
        treeListView.TreeColumnRenderer.UseGdiTextRendering = true;
        treeListView.TreeColumnRenderer.IsShowLines = false;
        treeListView.TreeColumnRenderer.UseTriangles = true;
        treeListView.TreeColumnRenderer.IsShowGlyphs = true;

        treeListView.UseCustomSelectionColors = true;
    }

    public static void AutoSizeColumns(this ListView listView)
    {
        foreach(var column in listView.Columns.OfType<ColumnHeader>())
        {
            column.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
        }
    }
}
