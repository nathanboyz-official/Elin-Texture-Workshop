using System.Text;
using ElinTextureManager.Core.Detection;

namespace ElinTextureManager.Tests;

/// <summary>
/// Builds a throwaway Elin-shaped folder tree on disk so scanning, override and
/// load-order logic can be exercised against real files rather than mocks.
/// </summary>
public sealed class TestWorkspace : IDisposable
{
    public string Root { get; }
    public string ElinRoot { get; }
    public string WorkshopRoot { get; }

    public ElinPaths Paths { get; }

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "etm_tests", Guid.NewGuid().ToString("N")[..10]);
        ElinRoot = Path.Combine(Root, "Elin");
        WorkshopRoot = Path.Combine(Root, "workshop", "content", "2135150");

        Directory.CreateDirectory(Path.Combine(ElinRoot, "Package"));
        Directory.CreateDirectory(Path.Combine(ElinRoot, "Elin_Data"));
        Directory.CreateDirectory(Path.Combine(ElinRoot, "User", "Texture Replace"));
        Directory.CreateDirectory(WorkshopRoot);

        File.WriteAllText(Path.Combine(ElinRoot, "Elin.exe"), "stub");

        Paths = new ElinPaths { ElinRoot = ElinRoot, WorkshopRoot = WorkshopRoot };
    }

    /// <summary>Creates a Workshop mod with a package.xml and the given texture files.</summary>
    public string AddWorkshopMod(
        string workshopId,
        string title,
        IEnumerable<(string fileName, string content)> textures,
        string? author = "tester",
        int? loadPriority = null)
    {
        var modDir = Path.Combine(WorkshopRoot, workshopId);
        var textureDir = Path.Combine(modDir, "Texture Replace");
        Directory.CreateDirectory(textureDir);

        var priority = loadPriority is null ? "" : $"  <loadPriority>{loadPriority}</loadPriority>\n";
        File.WriteAllText(Path.Combine(modDir, "package.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<Meta>\n"
            + $"  <title>{title}</title>\n"
            + $"  <id>test.{workshopId}</id>\n"
            + $"  <author>{author}</author>\n"
            + "  <builtin>false</builtin>\n"
            + priority
            + "  <version>1.0.0</version>\n"
            + "  <description>test mod</description>\n"
            + "</Meta>\n",
            new UTF8Encoding(false));

        foreach (var (fileName, content) in textures)
            WritePng(Path.Combine(textureDir, fileName), content);

        return modDir;
    }

    /// <summary>Adds a file inside a sub-folder of Texture Replace (a variant set).</summary>
    public void AddVariant(string workshopId, string variantFolder, string fileName, string content)
    {
        var dir = Path.Combine(WorkshopRoot, workshopId, "Texture Replace", variantFolder);
        Directory.CreateDirectory(dir);
        WritePng(Path.Combine(dir, fileName), content);
    }

    /// <summary>
    /// Writes a minimally valid 1x1-header PNG whose pixel payload varies with
    /// <paramref name="content"/>, so files can be made identical or different on demand.
    /// </summary>
    public static void WritePng(string path, string content, int width = 16, int height = 16)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // signature
        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x0D });                          // IHDR length
        bw.Write(Encoding.ASCII.GetBytes("IHDR"));

        Span<byte> dim = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(dim, width);
        bw.Write(dim.ToArray());
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(dim, height);
        bw.Write(dim.ToArray());

        bw.Write(new byte[] { 8, 6, 0, 0, 0 }); // bit depth, colour type, etc.
        bw.Write(Encoding.UTF8.GetBytes(content)); // payload that differentiates files
    }

    public void WriteLoadOrder(params (string modDir, bool enabled)[] entries)
    {
        var sb = new StringBuilder();
        foreach (var (dir, enabled) in entries)
            sb.Append(dir).Append(',').Append(enabled ? '1' : '0').Append("\r\n");

        File.WriteAllText(Paths.LoadOrderFile, sb.ToString(), new UTF8Encoding(false));
    }

    public string BackupDirectory => Path.Combine(Root, "Backups");

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* temp folder cleanup is best effort */ }
    }
}
