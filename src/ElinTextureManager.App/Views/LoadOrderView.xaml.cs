using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class LoadOrderView : UserControl
{
    private Point _dragStart;
    private LoadOrderRowViewModel? _dragItem;

    public LoadOrderView() => InitializeComponent();

    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoadOrderViewModel vm) vm.MarkDirty();
    }

    // ---- drag and drop reordering ----

    private void OrderList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = ItemUnder(e.OriginalSource as DependencyObject);
    }

    private void OrderList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;

        var moved = e.GetPosition(null) - _dragStart;

        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(OrderList, _dragItem, DragDropEffects.Move);
    }

    private void OrderList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LoadOrderRowViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OrderList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not LoadOrderViewModel vm) return;
        if (e.Data.GetData(typeof(LoadOrderRowViewModel)) is not LoadOrderRowViewModel dragged) return;

        var target = ItemUnder(e.OriginalSource as DependencyObject);
        if (target is null || ReferenceEquals(target, dragged)) return;

        var from = vm.Items.IndexOf(dragged);
        var to = vm.Items.IndexOf(target);

        vm.MoveRow(from, to);

        _dragItem = null;
        e.Handled = true;
    }

    /// <summary>Walks up the visual tree to the row under the pointer.</summary>
    private static LoadOrderRowViewModel? ItemUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);

        return (source as ListBoxItem)?.DataContext as LoadOrderRowViewModel;
    }
}
