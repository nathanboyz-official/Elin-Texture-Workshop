namespace ElinTextureManager.Core.Model;

/// <summary>
/// One line of Elin's loadorder.txt. The observed format is:
///   &lt;absolute mod directory&gt;,&lt;1 = enabled | 0 = disabled&gt;
/// </summary>
public sealed class LoadOrderEntry
{
    public required string Path { get; set; }
    public bool Enabled { get; set; }

    /// <summary>Preserved verbatim when a line cannot be parsed, so saving never loses data.</summary>
    public string? RawLine { get; init; }

    public bool IsParsed => RawLine is null;

    public string FolderName => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));

    public string Serialize() => RawLine ?? $"{Path},{(Enabled ? 1 : 0)}";
}
