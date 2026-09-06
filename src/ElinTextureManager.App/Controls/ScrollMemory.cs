using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ElinTextureManager.App.Controls;

/// <summary>
/// Remembers where a page was scrolled to.
///
/// Navigating swaps the ContentControl's content, which rebuilds the view from its
/// DataTemplate, so any scroll position living in the visual tree is lost. Keeping the
/// offset on the view model instead means opening a texture and coming back lands you
/// where you were rather than at the top of the list.
/// </summary>
public static class ScrollMemory
{
    /// <summary>
    /// Restores the offset <paramref name="read"/> returns, then keeps it up to date
    /// through <paramref name="write"/> as the user scrolls. Returns the scroll viewer
    /// it attached to, so the caller can re-apply an offset after a list rebuild.
    /// </summary>
    /// <param name="host">
    /// The scrolling element itself, or the control that contains it. Pass the specific
    /// list rather than the whole page: templated controls such as TextBox carry their
    /// own internal ScrollViewer, and the first one found may not be the one you mean.
    /// </param>
    public static ScrollViewer? Bind(DependencyObject host, Func<double> read, Action<double> write)
    {
        var scroller = host as ScrollViewer ?? FindDescendant<ScrollViewer>(host);
        if (scroller is null) return null;

        // Read the target before anything is hooked up: the first layout pass reports an
        // offset of zero, and letting that be written back would erase what we are
        // restoring.
        var target = read();

        host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // By this point layout has run, so the panel knows its full extent and the
            // offset will not be clamped away.
            if (target > 0) scroller.ScrollToVerticalOffset(target);

            scroller.ScrollChanged += (_, _) => write(scroller.VerticalOffset);
        });

        return scroller;
    }

    /// <summary>
    /// Re-applies an offset after the item list has been rebuilt in place. The value is
    /// captured by the caller before the rebuild, because the intermediate layout writes
    /// its own offset back through <c>write</c> first.
    /// </summary>
    public static void ReapplyAfterRebuild(ScrollViewer? scroller, double offset, Action<double> write)
    {
        if (scroller is null) return;

        scroller.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            scroller.ScrollToVerticalOffset(offset);
            write(offset);
        });
    }

    /// <summary>Depth-first search of the visual tree for the first child of type T.</summary>
    public static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;

            var deeper = FindDescendant<T>(child);
            if (deeper is not null) return deeper;
        }

        return null;
    }
}
