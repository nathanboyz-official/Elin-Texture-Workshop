using System.Windows;
using System.Windows.Input;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class SpriteEditorWindow : Window
{
    private readonly SpriteEditorViewModel _model;
    private bool _drawing;

    public SpriteEditorWindow(SpriteEditorViewModel model)
    {
        _model = model;

        InitializeComponent();
        DataContext = model;

        model.Finished += _ => DialogResult = true;
    }

    /// <summary>Turns a point on the zoomed canvas into a pixel in the cell.</summary>
    private (int X, int Y) CellPoint(MouseEventArgs e)
    {
        var point = e.GetPosition(CanvasHost);
        var zoom = Math.Max(1, _model.Zoom);

        return ((int)(point.X / zoom), (int)(point.Y / zoom));
    }

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        _drawing = true;
        CanvasHost.CaptureMouse();

        var (x, y) = CellPoint(e);
        _model.Apply(x, y, starting: true);
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || e.LeftButton != MouseButtonState.Pressed) return;

        var (x, y) = CellPoint(e);
        _model.Apply(x, y, starting: false);
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        _drawing = false;
        CanvasHost.ReleaseMouseCapture();
    }

    private void OnSave(object sender, RoutedEventArgs e) => _model.Save();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
