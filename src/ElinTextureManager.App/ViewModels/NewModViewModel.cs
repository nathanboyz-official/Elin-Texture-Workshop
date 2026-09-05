using System.Collections.ObjectModel;
using System.IO;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Sheets;

namespace ElinTextureManager.App.ViewModels;

/// <summary>A sheet on offer, with how sure we are of its header.</summary>
public sealed class SheetChoiceViewModel : ObservableObject
{
    private bool _chosen;

    public SheetChoiceViewModel(SheetTemplate template, bool chosen)
    {
        Template = template;
        _chosen = chosen;
    }

    public SheetTemplate Template { get; }

    public string Tab => Template.Tab;

    public string Detail =>
        $"{Template.Header.Count} columns · matched in {Template.Agreed} of "
        + $"{Template.Seen} installed sheets";

    public bool Chosen
    {
        get => _chosen;
        set => SetProperty(ref _chosen, value);
    }
}

/// <summary>A folder on offer.</summary>
public sealed class FolderChoiceViewModel : ObservableObject
{
    private bool _chosen;

    public FolderChoiceViewModel(string path, string what)
    {
        Path = path;
        What = what;
    }

    public string Path { get; }
    public string What { get; }
    public string Display => Path.Replace(System.IO.Path.DirectorySeparatorChar, '\\');

    public bool Chosen
    {
        get => _chosen;
        set => SetProperty(ref _chosen, value);
    }
}

/// <summary>
/// Starting a mod.
///
/// The point of it is the parts nobody gets right first time: package.xml with the
/// fields the game reads, and source sheets that already carry the official first three
/// rows. Those rows are taken from the mods already installed - the official sheets live
/// in a Google Drive and are not on the machine, but every mod that uses one copied them.
/// </summary>
public sealed class NewModViewModel : ObservableObject
{
    private readonly ElinPaths _paths;

    public NewModViewModel(ElinPaths paths, IReadOnlyList<SheetTemplate> templates)
    {
        _paths = paths;

        // The two almost every content mod starts with are ticked; the rest are offered.
        foreach (var template in templates)
        {
            Sheets.Add(new SheetChoiceViewModel(template,
                template.Tab is "Chara" or "Thing"));
        }

        foreach (var (path, what) in NewMod.OptionalFolders)
            Folders.Add(new FolderChoiceViewModel(path, what));
    }

    public ObservableCollection<SheetChoiceViewModel> Sheets { get; } = new();
    public ObservableCollection<FolderChoiceViewModel> Folders { get; } = new();

    public bool HasSheets => Sheets.Count > 0;

    public string SheetNote => Sheets.Count == 0
        ? "No source sheets could be read from your installed mods, so none can be "
          + "started for you. The mod will still be created."
        : "The first three rows of each - the header, the types and the defaults - are "
          + "taken from the mods you already have. Your own entries start on row 4.";

    private string _title = string.Empty;

    public string Title
    {
        get => _title;
        set
        {
            if (!SetProperty(ref _title, value)) return;

            // The id follows the title until the id is typed into directly.
            if (!_idEdited) Id = Suggest(value);

            Changed();
        }
    }

    private bool _idEdited;
    private string _id = string.Empty;

    public string Id
    {
        get => _id;
        set { if (SetProperty(ref _id, value)) Changed(); }
    }

    /// <summary>Called by the view when the id box is typed into, not merely set.</summary>
    public void IdWasEdited() => _idEdited = true;

    private string _author = string.Empty;

    public string Author
    {
        get => _author;
        set { if (SetProperty(ref _author, value)) Changed(); }
    }

    private string _description = string.Empty;

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    private string _tags = "Other";

    public string Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    /// <summary>Where it will be written, shown as it is typed.</summary>
    public string FolderPath => string.IsNullOrWhiteSpace(_id)
        ? Path.Combine(_paths.PackageRoot, "<id>")
        : NewMod.FolderFor(_paths, _id);

    public string? Problem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_title)) return null;

            var check = NewMod.CheckId(_id);
            if (!check.Ok) return check.Problem;

            if (Directory.Exists(FolderPath))
                return "There is already a folder with that id in your Package folder.";

            return null;
        }
    }

    public bool HasProblem => Problem is not null;

    public bool CanCreate =>
        !string.IsNullOrWhiteSpace(_title)
        && !string.IsNullOrWhiteSpace(_id)
        && NewMod.CheckId(_id).Ok
        && !Directory.Exists(FolderPath);

    /// <summary>Builds the request the core writes from.</summary>
    public NewModRequest ToRequest()
    {
        var request = new NewModRequest
        {
            Title = _title,
            Id = _id,
            Author = string.IsNullOrWhiteSpace(_author) ? "Unknown" : _author,
            Description = _description,
            Tags = string.IsNullOrWhiteSpace(_tags) ? "Other" : _tags,
        };

        foreach (var sheet in Sheets.Where(s => s.Chosen)) request.Sheets.Add(sheet.Template);
        foreach (var folder in Folders.Where(f => f.Chosen)) request.Folders.Add(folder.Path);

        return request;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(FolderPath));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>An id from a title: lower case, no spaces, nothing a folder cannot hold.</summary>
    private static string Suggest(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();

        var cleaned = new string(title
            .Select(c => c == ' ' ? '_' : c)
            .Where(c => !invalid.Contains(c))
            .ToArray())
            .Trim('_')
            .ToLowerInvariant();

        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
