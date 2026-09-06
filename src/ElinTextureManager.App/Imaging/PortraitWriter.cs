using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElinTextureManager.Core.Detection;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Portraits;

namespace ElinTextureManager.App.Imaging;

/// <summary>
/// Puts a portrait of the user's own into Elin\Custom\Portrait, where the game offers it
/// in the portrait picker alongside its own.
///
/// Always written as a PNG, whatever was picked: the folder is read as images by name
/// and a JPEG called .png is a file that fails to decode. Written under a temporary name
/// and moved into place, so a portrait half-copied when something goes wrong never
/// becomes a portrait the game tries to read.
/// </summary>
public static class PortraitWriter
{
    /// <summary>The file extensions worth offering in a picker.</summary>
    public const string Filter =
        "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*";

    public static string FolderFor(ElinPaths paths) => paths.CustomPortraitRoot;

    public static string PathFor(ElinPaths paths, string name) =>
        Path.Combine(FolderFor(paths), PortraitName.FileName(name));

    /// <summary>Portrait names already in the folder, so a new one cannot land on them.</summary>
    public static HashSet<string> Taken(ElinPaths paths)
    {
        var folder = FolderFor(paths);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(folder)) return taken;

        foreach (var file in Directory.EnumerateFiles(folder))
            taken.Add(Path.GetFileNameWithoutExtension(file));

        return taken;
    }

    /// <summary>Reads an image without holding the file open afterwards.</summary>
    public static BitmapSource Read(string path)
    {
        var image = new BitmapImage();

        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();

        return image;
    }

    /// <summary>
    /// Installs an image as a portrait under the id given, which is the file name the
    /// game reads the group and gender out of. The picture is fitted to the frame if it
    /// is not already the right size, and left exactly as it is if it is. Returns the
    /// path written.
    /// </summary>
    public static string Install(ElinPaths paths, BitmapSource image, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("A portrait needs a name.");

        var output = PortraitFit.ToFrame(image);
        var name = id;

        var folder = FolderFor(paths);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, PortraitName.FileName(name.Trim()));
        var temp = path + ".tmp";

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));

        using (var stream = File.Create(temp)) encoder.Save(stream);

        File.Move(temp, path, overwrite: true);

        AppLog.Info($"Added portrait {path} ({output.PixelWidth}x{output.PixelHeight})");
        return path;
    }
}
