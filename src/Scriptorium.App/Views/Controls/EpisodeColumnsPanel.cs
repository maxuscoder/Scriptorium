using System.Windows;
using System.Windows.Controls;

namespace Scriptorium.App.Views.Controls;

/// <summary>Balances episodes down two columns, returning to one column in narrow windows.</summary>
public sealed class EpisodeColumnsPanel : Panel
{
    private const double ColumnGap = 12;
    private const double RowGap = 8;
    private double _rowHeight;

    private static int ColumnsFor(double width) => double.IsFinite(width) && width >= 680 ? 2 : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnsFor(availableSize.Width);
        var width = (availableSize.Width - (columns - 1) * ColumnGap) / columns;
        _rowHeight = 0;
        var desiredWidth = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
        }

        var rows = (InternalChildren.Count + columns - 1) / columns;
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth,
            rows * _rowHeight + Math.Max(0, rows - 1) * RowGap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var rows = (InternalChildren.Count + columns - 1) / columns;
        var width = Math.Max(0, (finalSize.Width - (columns - 1) * ColumnGap) / columns);
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var column = index / rows;
            var row = index % rows;
            InternalChildren[index].Arrange(new Rect(column * (width + ColumnGap), row * (_rowHeight + RowGap), width, _rowHeight));
        }
        return finalSize;
    }
}
