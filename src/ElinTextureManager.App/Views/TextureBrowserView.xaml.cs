using System.Windows;
using System.Windows.Controls;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class TextureBrowserView : UserControl
{
    public TextureBrowserView() => InitializeComponent();

    /// <summary>
    /// Thumbnails load when a card is realised rather than up front, so a library of
    /// thousands of textures only ever decodes what is on screen.
    /// </summary>
    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TextureCardViewModel card }
            && DataContext is TextureBrowserViewModel vm)
        {
            card.EnsureThumbnail(vm.ThumbnailSize);
        }
    }

    private void PrefixCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is TextureBrowserViewModel vm && sender is ComboBox combo)
            vm.SetPrefixFromDisplay(combo.SelectedItem as string);
    }
}
