using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ElinTextureManager.Core.Scanning;

/// <summary>
/// Cheap image inspection. Reading pixel dimensions from the header avoids decoding
/// thousands of PNGs during a scan, which is the difference between a snappy startup
/// and a multi-second freeze.
/// </summary>
public static class ImageInfo
{
    /// <summary>
    /// Formats the scanner accepts. PNG is what Elin texture packs ship today;
    /// adding a format here is all that is needed to support it.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Reads width/height from a PNG IHDR chunk. Returns false for anything that is
    /// not a valid PNG, so a truncated or misnamed file degrades to "unknown size"
    /// rather than an exception.
    /// </summary>
    public static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[24];
            if (fs.Read(header) != header.Length) return false;

            // 89 50 4E 47 0D 0A 1A 0A
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
                return false;

            // Bytes 12..15 must be the IHDR chunk type.
            if (header[12] != (byte)'I' || header[13] != (byte)'H' ||
                header[14] != (byte)'D' || header[15] != (byte)'R')
                return false;

            width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);

            if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
            {
                width = height = 0;
                return false;
            }

            return true;
        }
        catch
        {
            width = height = 0;
            return false;
        }
    }

    /// <summary>SHA-256 of a file, or null if it cannot be read (locked by a Steam update, say).</summary>
    public static string? TryComputeHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch
        {
            return null;
        }
    }
}
