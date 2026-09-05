using System.Windows;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class NewModWindow : Window
{
    public NewModWindow(NewModViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Model = model;
    }

    public NewModViewModel Model { get; }

    /// <summary>
    /// The id follows the title only until it is typed into. Checked here rather than in
    /// the view model because a binding cannot tell a keystroke from an assignment, and
    /// without the difference the suggestion overwrites what the user just typed.
    /// </summary>
    private void OnIdTyped(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IdBox.IsKeyboardFocusWithin) Model.IdWasEdited();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!Model.CanCreate) return;

        DialogResult = true;
    }
}
