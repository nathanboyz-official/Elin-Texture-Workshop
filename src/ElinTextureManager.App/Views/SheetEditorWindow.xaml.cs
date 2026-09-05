using System.Windows;
using System.Windows.Controls;
using ElinTextureManager.App.ViewModels;
using ElinTextureManager.Core.Sheets;

namespace ElinTextureManager.App.Views;

public partial class SheetEditorWindow : Window
{
    public SheetEditorWindow(SheetEditorViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Model = model;
    }

    public SheetEditorViewModel Model { get; }

    /// <summary>
    /// Row headers show the row's real number in the spreadsheet, not its position in the
    /// grid. Everything about these sheets is described by row number - the first entry
    /// is on row 4 - so a grid counting from 1 would disagree with the file, the wiki and
    /// every finding this application reports.
    /// </summary>
    private void OnLoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + SourceSheetNames.FirstDataRow).ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // A grid still in edit mode has not written the cell back yet, so a save straight
        // after typing would miss the last thing typed.
        Sheet.CommitEdit(DataGridEditingUnit.Cell, true);
        Sheet.CommitEdit(DataGridEditingUnit.Row, true);

        if (!Model.Save()) return;

        var kept = Model.LastBackup is null
            ? string.Empty
            : $"\n\nThe file as it was is kept at:\n{Model.LastBackup}";

        MessageBox.Show(
            $"Saved {Model.FileName}.{kept}",
            "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Model.Dirty) return;

        var answer = MessageBox.Show(
            "This sheet has changes that have not been saved. Close it anyway?",
            "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
}
