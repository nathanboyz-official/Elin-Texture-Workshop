using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using ElinTextureManager.App.Controls;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class TextureBrowserView : UserControl
{
    private ScrollViewer? _scroller;

    public TextureBrowserView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TextureBrowserViewModel vm) return;

        _scroller = ScrollMemory.Bind(TextureGrid,
            read: () => vm.ScrollOffset,
            write: offset => vm.ScrollOffset = offset);

        // Refreshing the page rebuilds the collection underneath a live view. The page
        // has already decided whether that should return to the top (a filter change) or
        // stay put (a rescan) by setting ScrollOffset, so just honour it.
        vm.Items.CollectionChanged -= OnItemsChanged;
        vm.Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Reset) return;
        if (DataContext is not TextureBrowserViewModel vm) return;

        // Captured now: the relayout that follows reports its own offset first.
        var target = vm.ScrollOffset;
        ScrollMemory.ReapplyAfterRebuild(_scroller, target, offset => vm.ScrollOffset = offset);
    }
}
