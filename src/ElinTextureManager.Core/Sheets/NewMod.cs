using System.Text;
using System.Xml.Linq;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Sheets;

/// <summary>What the user typed into the new-mod form.</summary>
public sealed class NewModRequest
{
    public required string Title { get; init; }
    public required string Id { get; init; }
    public required string Author { get; init; }
    public string Tags { get; init; } = "Other";
    public string Description { get; init; } = string.Empty;

    /// <summary>Which source sheet tabs to start the mod with. May be empty.</summary>
    public List<SheetTemplate> Sheets { get; } = new();

    /// <summary>Folders to create empty, ready to drop art into.</summary>
    public List<string> Folders { get; } = new();
}

public sealed record NewModCheck(bool Ok, string? Problem)
{
    public static readonly NewModCheck Fine = new(true, null);
    public static NewModCheck No(string problem) => new(false, problem);
}

/// <summary>
/// Creates a mod folder the game will load.
///
/// Every one of the traps this avoids is a real one: a mod with no package.xml is not a
/// mod, a source sheet without the official first three rows loses columns quietly, and
/// an id that changes after publishing makes Steam treat the mod as a different one so
/// the update reaches nobody.
///
/// Nothing here is written outside the folder being created, and an existing folder is
/// never written into.
/// </summary>
public static class NewMod
{
    /// <summary>Folders offered on the form, with what each is for.</summary>
    public static readonly IReadOnlyList<(string Path, string What)> OptionalFolders = new[]
    {
        ("Texture Replace", "Sprites addressed by their slot in the packed atlas"),
        ("Portrait", "Portraits replacing one of the game's by name"),
        (Path.Combine("Actor", "PCC", "female"), "Character parts - hair, clothes, body"),
        ("Texture", "Whole loose images mirroring _Elona\\Texture"),
        ("Sound", "Music and effects"),
    };

    /// <summary>An id the game and Steam can both live with.</summary>
    public static NewModCheck CheckId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return NewModCheck.No("Give it an id.");

        var trimmed = id.Trim();

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return NewModCheck.No("That id cannot be part of a folder name.");

        if (trimmed.Contains(' '))
            return NewModCheck.No("No spaces in the id - it is the folder name too.");

        if (trimmed.Length > 64) return NewModCheck.No("That id is too long.");

        return NewModCheck.Fine;
    }

    public static string FolderFor(ElinPaths paths, string id) =>
        Path.Combine(paths.PackageRoot, id.Trim());

    /// <summary>
    /// Writes the mod. Returns the folder created, or throws with something the user can
    /// act on. Refuses to touch a folder that already exists.
    /// </summary>
    public static string Create(ElinPaths paths, NewModRequest request)
    {
        var check = CheckId(request.Id);
        if (!check.Ok) throw new InvalidOperationException(check.Problem);

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Give it a title.");

        var folder = FolderFor(paths, request.Id);

        if (Directory.Exists(folder))
            throw new InvalidOperationException(
                $"There is already a folder called {request.Id.Trim()} in your Package folder. "
                + "Pick another id, or move that one out of the way.");

        Directory.CreateDirectory(folder);

        WritePackageXml(folder, request);

        foreach (var extra in request.Folders)
            Directory.CreateDirectory(Path.Combine(folder, extra));

        if (request.Sheets.Count > 0)
        {
            var sheets = request.Sheets
                .Select(t => new XlsxOutSheet(t.Tab, t.Rows))
                .ToList();

            var langFolder = Path.Combine(folder, "LangMod", "EN");
            XlsxWriter.Write(Path.Combine(langFolder, "Source.xlsx"), sheets);
        }

        AppLog.Info($"Created mod {folder} with {request.Sheets.Count} source sheets.");
        return folder;
    }

    /// <summary>
    /// package.xml, with the fields the game reads. Written with the XML writer rather
    /// than by hand so a quote in a title cannot break the file.
    /// </summary>
    private static void WritePackageXml(string folder, NewModRequest request)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Package",
                new XElement("title", request.Title.Trim()),
                new XElement("id", request.Id.Trim()),
                new XElement("author", request.Author.Trim()),
                new XElement("description", request.Description.Trim()),
                new XElement("tags", request.Tags.Trim()),
                new XElement("loadPriority", 0),
                new XElement("version", "1.0"),
                new XElement("builtin", "false")));

        using var writer = new StreamWriter(Path.Combine(folder, "package.xml"),
            append: false, new UTF8Encoding(false));

        doc.Save(writer);
    }
}
