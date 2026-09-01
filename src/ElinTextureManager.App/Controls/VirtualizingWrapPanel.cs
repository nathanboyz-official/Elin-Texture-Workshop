using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ElinTextureManager.App.Controls;

/// <summary>
/// A wrap panel that only realises the tiles currently on screen.
///
/// WPF's stock WrapPanel does not virtualise, so a library of several thousand textures
/// would build several thousand visual trees up front. Every texture card is the same
/// size here, which makes the layout maths simple: the panel computes how many columns
/// fit, derives the visible row range from the scroll offset, and generates containers
/// only for that range.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(210.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private Size _extent;
    private Size _viewport;
    private Point _offset;
    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemCount = GetItemCount();

        var itemWidth = Math.Max(1, ItemWidth);
        var itemHeight = Math.Max(1, ItemHeight);

        var width = double.IsInfinity(availableSize.Width) ? itemWidth : availableSize.Width;
        _columns = Math.Max(1, (int)Math.Floor(width / itemWidth));

        var rows = itemCount == 0 ? 0 : (int)Math.Ceiling(itemCount / (double)_columns);

        var extent = new Size(_columns * itemWidth, rows * itemHeight);
        var viewport = new Size(width,
            double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);

        UpdateScrollInfo(extent, viewport);

        var (first, last) = VisibleRange(itemCount, itemHeight);
        RealizeRange(first, last, new Size(itemWidth, itemHeight));

        return new Size(
            double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemWidth = Math.Max(1, ItemWidth);
        var itemHeight = Math.Max(1, ItemHeight);

        var generator = ItemContainerGenerator;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0) continue;

            var row = itemIndex / _columns;
            var column = itemIndex % _columns;

            child.Arrange(new Rect(
                column * itemWidth - _offset.X,
                row * itemHeight - _offset.Y,
                itemWidth,
                itemHeight));
        }

        return finalSize;
    }

    /// <summary>Rows visible at the current offset, padded by one row either side.</summary>
    private (int first, int last) VisibleRange(int itemCount, double itemHeight)
    {
        if (itemCount == 0) return (0, -1);

        var firstRow = Math.Max(0, (int)Math.Floor(_offset.Y / itemHeight) - 1);
        var visibleRows = (int)Math.Ceiling(_viewport.Height / itemHeight) + 2;

        var first = firstRow * _columns;
        var last = Math.Min(itemCount - 1, (firstRow + visibleRows) * _columns - 1);

        return (Math.Min(first, itemCount - 1), last);
    }

    private void RealizeRange(int first, int last, Size itemSize)
    {
        var generator = ItemContainerGenerator;
        if (generator is null) return;

        CleanUpOutsideRange(first, last);

        if (last < first) return;

        var startPos = generator.GeneratorPositionFromIndex(first);
        var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (var i = first; i <= last; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var isNew);
                if (child is null) continue;

                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);

                    generator.PrepareItemContainer(child);
                }

                child.Measure(itemSize);
            }
        }
    }

    /// <summary>Recycles containers that have scrolled out of view.</summary>
    private void CleanUpOutsideRange(int first, int last)
    {
        var generator = ItemContainerGenerator;

        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);

            if (itemIndex >= 0 && (itemIndex < first || itemIndex > last))
            {
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private int GetItemCount()
    {
        var owner = ItemsControl.GetItemsOwner(this);
        return owner?.Items.Count ?? 0;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                // Drop the realised containers but leave the offset alone. Whether a
                // reset should return to the top depends on why it happened - a filter
                // change should, a background refresh should not - so that decision
                // belongs to the page, not here. MeasureOverride clamps it to the new
                // extent, so an offset past the end of a shorter list is still safe.
                RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }

        InvalidateMeasure();
    }

    // ---------------- IScrollInfo ----------------

    private const double LineSize = 48;
    private const double WheelSize = 3 * LineSize;

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        var changed = false;

        if (extent != _extent) { _extent = extent; changed = true; }
        if (viewport != _viewport) { _viewport = viewport; changed = true; }

        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxOffset) { _offset.Y = maxOffset; changed = true; }

        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (Math.Abs(clamped - _offset.Y) < 0.01) return;

        _offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset) { /* columns always fit the viewport */ }

    public void LineUp() => SetVerticalOffset(VerticalOffset - LineSize);
    public void LineDown() => SetVerticalOffset(VerticalOffset + LineSize);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - WheelSize);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + WheelSize);
    public void PageUp() => SetVerticalOffset(VerticalOffset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(VerticalOffset + _viewport.Height);

    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        // Bring the requested child fully into view.
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            if (!ReferenceEquals(InternalChildren[i], visual)) continue;

            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0) break;

            var row = itemIndex / _columns;
            var top = row * ItemHeight;
            var bottom = top + ItemHeight;

            if (top < _offset.Y) SetVerticalOffset(top);
            else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);

            break;
        }

        return rectangle;
    }
}
