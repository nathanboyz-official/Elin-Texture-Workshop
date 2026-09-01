using System.Xml.Linq;
using ElinTextureManager.Core.Logging;
using ElinTextureManager.Core.Model;

namespace ElinTextureManager.Core.Scanning;

/// <summary>
/// Reads Elin's package.xml. The schema was taken from the packages shipped with the
/// game (Package\_Elona, Package\_ModdingKit):
///
///   &lt;Meta&gt;
///     &lt;title&gt;Elin Core&lt;/title&gt;
///     &lt;id&gt;elin_core1&lt;/id&gt;
///     &lt;author&gt;Lafrontier&lt;/author&gt;
///     &lt;builtin&gt;true&lt;/builtin&gt;
///     &lt;loadPriority&gt;-100&lt;/loadPriority&gt;
///     &lt;version&gt;0.22.1&lt;/version&gt;
///     &lt;description&gt;...&lt;/description&gt;
///   &lt;/Meta&gt;
/// </summary>
public static class PackageMetadataParser
{
    /// <summary>
    /// Applies package.xml values onto a mod. A missing or corrupt file is recorded on
    /// the mod and logged, never thrown - one bad mod must not stop a scan.
    /// </summary>
    public static void Apply(ModPackage mod, string packageXmlPath)
    {
        if (!File.Exists(packageXmlPath))
        {
            mod.MetadataMissing = true;
            return;
        }

        try
        {
            var doc = XDocument.Load(packageXmlPath, LoadOptions.None);
            var meta = doc.Root;
            if (meta is null)
            {
                mod.MetadataMissing = true;
                return;
            }

            var title = Value(meta, "title");
            if (!string.IsNullOrWhiteSpace(title)) mod.Name = title.Trim();

            mod.ModId = Value(meta, "id")?.Trim();
            mod.Author = Value(meta, "author")?.Trim();
            mod.Description = Value(meta, "description")?.Trim();
            mod.Version = Value(meta, "version")?.Trim();

            if (bool.TryParse(Value(meta, "builtin")?.Trim(), out var builtin))
                mod.Builtin = builtin;

            if (int.TryParse(Value(meta, "loadPriority")?.Trim(), out var priority))
                mod.LoadPriority = priority;
        }
        catch (Exception ex)
        {
            mod.MetadataMissing = true;
            AppLog.Warn($"Malformed package.xml in {mod.Directory}: {ex.Message}");
        }
    }

    /// <summary>Case-insensitive single-element lookup, tolerant of unexpected casing.</summary>
    private static string? Value(XElement root, string name)
    {
        foreach (var el in root.Elements())
            if (string.Equals(el.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                return el.Value;
        return null;
    }

    /// <summary>Builds the package.xml written for our own override package.</summary>
    public static string BuildOverridePackageXml(string title, string modId, int loadPriority)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Meta",
                new XElement("title", title),
                new XElement("id", modId),
                new XElement("author", "Elin Texture Manager"),
                new XElement("builtin", "false"),
                new XElement("loadPriority", loadPriority),
                new XElement("version", "1.0.0"),
                new XElement("description",
                    "Per-texture overrides selected in Elin Texture Manager. "
                    + "This package is generated - textures here are copies of files from "
                    + "Workshop mods, which are never modified.")));

        using var sw = new Utf8StringWriter();
        doc.Save(sw);
        return sw.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }
}
