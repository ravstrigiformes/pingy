using System;
using System.Windows;
using System.Windows.Controls;

namespace Pingy.Widget.Controls;

/// <summary>
/// Wrap panel that picks a column count from available width and stretches each child
/// to fill its column. Behaves like a CSS Grid with <c>grid-template-columns: repeat(auto-fit, minmax(MinItemWidth, 1fr))</c>.
/// </summary>
public sealed class ResponsiveWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(
            nameof(MinItemWidth),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(
                280.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public static readonly DependencyProperty HorizontalGapProperty =
        DependencyProperty.Register(
            nameof(HorizontalGap),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double HorizontalGap
    {
        get => (double)GetValue(HorizontalGapProperty);
        set => SetValue(HorizontalGapProperty, value);
    }

    public static readonly DependencyProperty VerticalGapProperty =
        DependencyProperty.Register(
            nameof(VerticalGap),
            typeof(double),
            typeof(ResponsiveWrapPanel),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double VerticalGap
    {
        get => (double)GetValue(VerticalGapProperty);
        set => SetValue(VerticalGapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size(0, 0);

        // Fall back to a single column if width is unbounded (e.g. inside a horizontally-scrolling parent).
        var widthBudget = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var (columns, itemWidth) = ComputeColumns(widthBudget);

        var childConstraint = new Size(itemWidth, double.PositiveInfinity);
        double rowHeight = 0;
        double totalHeight = 0;
        int col = 0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(childConstraint);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            col++;
            if (col >= columns)
            {
                totalHeight += rowHeight + VerticalGap;
                rowHeight = 0;
                col = 0;
            }
        }
        if (col > 0) totalHeight += rowHeight; // last partial row, no trailing gap

        return new Size(double.IsInfinity(availableSize.Width) ? itemWidth * columns : availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;

        var (columns, itemWidth) = ComputeColumns(finalSize.Width);

        double x = 0;
        double y = 0;
        double rowHeight = 0;
        int col = 0;

        foreach (UIElement child in InternalChildren)
        {
            // Re-measure with the chosen itemWidth so DesiredSize reflects the arranged width.
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            var h = child.DesiredSize.Height;
            child.Arrange(new Rect(x, y, itemWidth, h));
            rowHeight = Math.Max(rowHeight, h);

            col++;
            if (col >= columns)
            {
                y += rowHeight + VerticalGap;
                x = 0;
                rowHeight = 0;
                col = 0;
            }
            else
            {
                x += itemWidth + HorizontalGap;
            }
        }

        return finalSize;
    }

    private (int columns, double itemWidth) ComputeColumns(double availableWidth)
    {
        var min = Math.Max(1.0, MinItemWidth);
        var gap = Math.Max(0.0, HorizontalGap);

        // Solve for largest n where n*min + (n-1)*gap <= availableWidth
        //   n <= (availableWidth + gap) / (min + gap)
        var raw = (availableWidth + gap) / (min + gap);
        var columns = Math.Max(1, (int)Math.Floor(raw));

        var itemWidth = (availableWidth - gap * (columns - 1)) / columns;
        if (itemWidth < min) itemWidth = min; // degenerate case (very narrow); will overflow horizontally but stay legible
        return (columns, itemWidth);
    }
}
