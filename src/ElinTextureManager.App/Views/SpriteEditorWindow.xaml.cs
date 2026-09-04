using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElinTextureManager.App.ViewModels;

namespace ElinTextureManager.App.Views;

public partial class SpriteEditorWindow : Window
{
    private readonly SpriteEditorViewModel _model;

    private bool _drawing;
    private bool _panning;
    private Point _panFrom;
    private double _panOffsetX;
    private double _panOffsetY;

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

        return ((int)Math.Floor(point.X / zoom), (int)Math.Floor(point.Y / zoom));
    }

    private static bool Held(Key key) => Keyboard.IsKeyDown(key);

    /// <summary>
    /// Space-drag anywhere in the canvas area, not only over the sprite itself.
    ///
    /// Hooked on the scroll viewer because that is the whole area the eye reads as the
    /// canvas: at a high zoom the drawing fills it, but at a low one it is a small square
    /// in the middle and grabbing the space around it did nothing.
    /// </summary>
    private void OnAreaDown(object sender, MouseButtonEventArgs e)
    {
        if (!Held(Key.Space)) return;

        StartPan(e);
        e.Handled = true;
    }

    private void OnAreaMove(object sender, MouseEventArgs e)
    {
        if (_panning) Pan(e);
    }

    private void OnAreaUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning) EndPan();
    }

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (Held(Key.Space)) { StartPan(e); return; }

        _drawing = true;
        CanvasHost.CaptureMouse();

        _model.LineFromLast = Held(Key.LeftShift) || Held(Key.RightShift);

        var (x, y) = CellPoint(e);
        _model.Apply(x, y, starting: true, picking: Held(Key.LeftAlt) || Held(Key.RightAlt));
    }

    /// <summary>Right button erases whatever tool is held, as every pixel editor does.</summary>
    private void OnCanvasRightDown(object sender, MouseButtonEventArgs e)
    {
        _drawing = true;
        CanvasHost.CaptureMouse();

        var (x, y) = CellPoint(e);
        _model.Apply(x, y, starting: true, erasing: true);
        e.Handled = true;
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (_panning) { Pan(e); return; }
        if (!_drawing) return;

        var left = e.LeftButton == MouseButtonState.Pressed;
        var right = e.RightButton == MouseButtonState.Pressed;
        if (!left && !right) return;

        var (x, y) = CellPoint(e);
        _model.Apply(x, y, starting: false, erasing: right,
            picking: left && (Held(Key.LeftAlt) || Held(Key.RightAlt)));
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning) { EndPan(); return; }

        _drawing = false;
        _model.LineFromLast = false;
        _model.FinishStroke();
        CanvasHost.ReleaseMouseCapture();
    }

    /// <summary>
    /// Wheel zooms, keeping the pixel under the cursor where it is.
    ///
    /// Zooming about the centre instead is the thing that makes a picker feel cheap:
    /// the detail being worked on slides away exactly when it is being looked at.
    /// </summary>
    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        var before = e.GetPosition(CanvasHost);
        var was = _model.Zoom;

        // A step that grows with the zoom, so 4x to 48x is a flick rather than twenty
        // notches, while staying fine-grained where it matters.
        var step = Math.Max(1, was / 6);
        _model.Zoom = was + (e.Delta > 0 ? step : -step);
        if (_model.Zoom == was) { e.Handled = true; return; }

        // Where that pixel has moved to once the canvas resized, and how far the view
        // has to shift to put it back under the cursor.
        var scale = _model.Zoom / (double)was;
        var view = e.GetPosition(CanvasScroll);

        CanvasScroll.UpdateLayout();
        CanvasScroll.ScrollToHorizontalOffset(before.X * scale - (view.X - CanvasScroll.HorizontalOffset));
        CanvasScroll.ScrollToVerticalOffset(before.Y * scale - (view.Y - CanvasScroll.VerticalOffset));

        e.Handled = true;
    }

    private void StartPan(MouseButtonEventArgs e)
    {
        _panning = true;
        _panFrom = e.GetPosition(CanvasScroll);
        _panOffsetX = CanvasScroll.HorizontalOffset;
        _panOffsetY = CanvasScroll.VerticalOffset;

        // Captured on the scroll viewer, so a drag that starts beside the sprite keeps
        // working once it passes over it.
        CanvasScroll.CaptureMouse();
        Cursor = Cursors.ScrollAll;
    }

    private void Pan(MouseEventArgs e)
    {
        var now = e.GetPosition(CanvasScroll);

        CanvasScroll.ScrollToHorizontalOffset(_panOffsetX - (now.X - _panFrom.X));
        CanvasScroll.ScrollToVerticalOffset(_panOffsetY - (now.Y - _panFrom.Y));
    }

    private void EndPan()
    {
        _panning = false;
        CanvasScroll.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    /// <summary>
    /// The shortcuts a pixel artist already has in their hands. Ignored while a text box
    /// has focus, so typing a name does not swap the tool.
    /// </summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (control)
        {
            switch (e.Key)
            {
                case Key.Z when shift:
                case Key.Y:
                    _model.RedoCommand.Execute(null); e.Handled = true; return;
                case Key.Z:
                    _model.UndoCommand.Execute(null); e.Handled = true; return;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.B: _model.Tool = SpriteTool.Pencil; break;
            case Key.E: _model.Tool = SpriteTool.Eraser; break;
            case Key.G: _model.Tool = SpriteTool.Fill; break;
            case Key.I: _model.Tool = SpriteTool.Dropper; break;
            case Key.L: _model.Tool = SpriteTool.Line; break;
            case Key.U: _model.Tool = SpriteTool.Rectangle; break;
            case Key.C: _model.Tool = SpriteTool.Ellipse; break;
            case Key.S: _model.Tool = SpriteTool.Star; break;
            case Key.R: _model.Tool = SpriteTool.ReplaceColour; break;

            case Key.OemOpenBrackets: _model.BrushSize--; break;
            case Key.OemCloseBrackets: _model.BrushSize++; break;

            case Key.Space when !_panning:
                // Swallowed so it does not press whatever button has focus.
                Cursor = Cursors.ScrollAll;
                break;

            default: return;
        }

        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_panning) Cursor = Cursors.Arrow;
    }

    private void OnSave(object sender, RoutedEventArgs e) => _model.Save();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
