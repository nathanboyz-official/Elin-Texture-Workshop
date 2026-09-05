using System.Windows;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class PortraitImportWindow : Window
{
    public PortraitImportWindow(PortraitImportViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Model = model;
    }

    public PortraitImportViewModel Model { get; }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!Model.CanCreate) return;

        DialogResult = true;
    }
}
