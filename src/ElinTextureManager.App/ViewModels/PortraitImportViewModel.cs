using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ElinTextureManager.App.Imaging;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Portraits;
using Microsoft.Win32;

namespace ElinTextureManager.App.ViewModels;

/// <summary>
/// Making one portrait: the picture, the group and gender the game files it under, and
/// the name it is given.
///
/// The file name is the whole point of this window. Elin reads the group and the gender
/// straight out of it, and a portrait named anything else is loaded and then never shown
/// by anything - so the name being built is spelled out as it is typed rather than
/// worked out silently at the end.
/// </summary>
public sealed class PortraitImportViewModel : ObservableObject
{
    private readonly ElinPaths _paths;

    public PortraitImportViewModel(ElinPaths paths)
    {
        _paths = paths;

        _group = PortraitId.Groups[0];
        _gender = PortraitId.Genders[2];

        BrowseCommand = new RelayCommand(_ => Browse());
    }

    public IReadOnlyList<PortraitGroupOption> Groups => PortraitId.Groups;
    public IReadOnlyList<PortraitGenderOption> Genders => PortraitId.Genders;

    public RelayCommand BrowseCommand { get; }

    private PortraitGroupOption _group;

    public PortraitGroupOption Group
    {
        get => _group;
        set { if (SetProperty(ref _group, value)) NameChanged(); }
    }

    private PortraitGenderOption _gender;

    public PortraitGenderOption Gender
    {
        get => _gender;
        set { if (SetProperty(ref _gender, value)) NameChanged(); }
    }

    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) NameChanged(); }
    }

    private BitmapSource? _picture;

    /// <summary>The chosen picture, already fitted to the frame.</summary>
    public BitmapSource? Picture
    {
        get => _picture;
        private set
        {
            if (!SetProperty(ref _picture, value)) return;

            OnPropertyChanged(nameof(HasPicture));
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    public bool HasPicture => _picture is not null;

    private string? _pictureNote;

    /// <summary>What happened to the picture, when anything did.</summary>
    public string? PictureNote
    {
        get => _pictureNote;
        private set => SetProperty(ref _pictureNote, value);
    }

    /// <summary>The file that will be written. Shown as it is typed.</summary>
    public string OutputName => string.IsNullOrWhiteSpace(_name)
        ? PortraitId.FileName(Group.Code, Gender.Code, "name")
        : PortraitId.FileName(Group.Code, Gender.Code, _name.Trim());

    /// <summary>Why the name will not do, or null when it will.</summary>
    public string? NameProblem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_name)) return null;

            var check = PortraitName.Check(_name);
            return check.Ok ? null : check.Problem;
        }
    }

    public bool HasNameProblem => NameProblem is not null;

    public bool CanCreate =>
        HasPicture && !string.IsNullOrWhiteSpace(_name) && NameProblem is null;

    /// <summary>The id chosen once Create is pressed, free of anything already there.</summary>
    public string ChosenId => PortraitId.Available(
        Group.Code, Gender.Code, _name.Trim(), PortraitWriter.Taken(_paths).Contains);

    private void NameChanged()
    {
        OnPropertyChanged(nameof(OutputName));
        OnPropertyChanged(nameof(NameProblem));
        OnPropertyChanged(nameof(HasNameProblem));
        OnPropertyChanged(nameof(CanCreate));
    }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a picture to use as a portrait",
            Filter = PortraitWriter.Filter,
        };

        // Given an owner so it opens over this window rather than attaching itself to
        // whatever happens to be in front - which on a machine with a game running is
        // the game, on the wrong screen.
        var owner = Application.Current?.Windows
            .OfType<Views.PortraitImportWindow>()
            .FirstOrDefault();

        var picked = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (picked != true) return;

        try
        {
            var raw = PortraitWriter.Read(dialog.FileName);
            var was = $"{raw.PixelWidth}x{raw.PixelHeight}";

            // Fitted straight away, so what is shown in the frame is exactly what gets
            // written - borders and all.
            Picture = PortraitFit.ToFrame(raw);

            PictureNote = PortraitFit.Fits(raw)
                ? $"{Path.GetFileName(dialog.FileName)} · {was} · already the right size"
                : $"{Path.GetFileName(dialog.FileName)} · {was} · fitted to "
                  + $"{PortraitSize.Width}x{PortraitSize.Height} without stretching";

            if (string.IsNullOrWhiteSpace(_name))
                Name = PortraitName.FromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            Picture = null;
            PictureNote = null;

            MessageBox.Show(
                $"{Path.GetFileName(dialog.FileName)} could not be read as a picture.\n\n{ex.Message}",
                "Could not use that one", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
