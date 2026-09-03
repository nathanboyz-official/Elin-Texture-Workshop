using System.Windows;
using System.Windows.Controls;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class IdentifyView : UserControl
{
    public IdentifyView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not IdentifyViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;

        vm.AcceptFile(files[0]);
        e.Handled = true;
    }
}
