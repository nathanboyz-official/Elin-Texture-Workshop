using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Identify;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Pcc;

namespace ElinTextureManager.App.Imaging;

/// <summary>
/// Writes a new PCC part into the application's own package, where the game loads it
/// from and the creator picks it up on the next scan.
///
/// The format is not a choice: the game's own parts are 8-bit RGBA PNGs, four cells
/// across by four down, and the modded ones people publish are the same. Anything else
/// is a file that either fails to load or draws wrong, so this writes exactly that.
/// </summary>
public static class PccPartWriter
{
    /// <summary>Where a part has to live to be loaded: the set folder under Actor/PCC.</summary>
    public static string FolderFor(ElinPaths paths, string set) =>
        Path.Combine(paths.OverridePackageRoot, "Actor", "PCC", set);

    public static string PathFor(ElinPaths paths, string set, string layer, string id) =>
        Path.Combine(FolderFor(paths, set), PccPartName.FileName(layer, id));

    /// <summary>
    /// Saves a sheet as a part. Returns the path written, or throws with a message the
    /// user can act on.
    /// </summary>
    public static string Save(ElinPaths paths, PccSheet sheet, string layer, string id,
        string set = PccSlots.DefaultSet)
    {
        var check = PccPartName.Check(layer, id);
        if (!check.Ok) throw new InvalidOperationException(check.Problem);

        var buffer = sheet.ToBuffer();

        // Bgra32 rather than Pbgra32: the game's parts carry straight alpha, and a
        // premultiplied write darkens every soft edge on the sprite.
        var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96,
            PixelFormats.Bgra32, null, buffer.Bgra, buffer.Width * 4);

        var folder = FolderFor(paths, set);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, PccPartName.FileName(layer, id.Trim()));

        // Written then moved, so an interrupted save cannot leave the game reading half
        // a PNG.
        var temp = path + ".tmp";

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using (var stream = File.Create(temp)) encoder.Save(stream);

        File.Move(temp, path, overwrite: true);

        AppLog.Info($"Wrote PCC part {path} ({buffer.Width}x{buffer.Height})");
        return path;
    }

    /// <summary>Whether a part of this name already exists anywhere the creator can see.</summary>
    public static bool Exists(ElinPaths paths, string set, string layer, string id) =>
        File.Exists(PathFor(paths, set, layer, id));
}
