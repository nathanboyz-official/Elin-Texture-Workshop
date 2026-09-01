using System.Windows.Controls;
using ElinTextureManager.App.Controls;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is not ModsViewModel vm) return;

            ScrollMemory.Bind(ModScroller,
                read: () => vm.ScrollOffset,
                write: offset => vm.ScrollOffset = offset);
        };
    }
}
