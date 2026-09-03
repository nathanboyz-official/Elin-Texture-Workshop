using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The on-disk cache. It holds nothing that cannot be recomputed, which is exactly why
/// it must never be the reason the application misbehaves.
/// </summary>
public sealed class CacheTests
{
    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "etm_tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "cache.db");
    }

    [Fact]
    public void Entries_survive_a_close_and_reopen()
    {
        var file = TempFile();

        using (var cache = new CacheDatabase())
        {
            cache.Open(file);
            cache.SaveAll(new[] { new CachedTexture(@"C:\a\b.png", 12, 34, 16, 16, "hash") });
        }

        using var reopened = new CacheDatabase();
        reopened.Open(file);

        var row = Assert.Single(reopened.LoadAll()).Value;
        Assert.Equal(16, row.Width);
        Assert.Equal("hash", row.Hash);
    }

    [Fact]
    public void A_corrupt_database_is_rebuilt_rather_than_limped_along_with()
    {
        var file = TempFile();

        using (var cache = new CacheDatabase())
        {
            cache.Open(file);
            cache.SaveAll(new[] { new CachedTexture(@"C:\a\b.png", 12, 34, 16, 16, "hash") });
        }

        // What an interrupted write leaves behind. Before this was handled, every read
        // failed forever and every launch logged the same error without ever recovering.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(file);
        for (var i = 1024; i < Math.Min(bytes.Length, 4096); i++) bytes[i] = 0xFF;
        File.WriteAllBytes(file, bytes);

        using var cache2 = new CacheDatabase();
        cache2.Open(file);

        Assert.True(cache2.IsOpen);
        Assert.Empty(cache2.LoadAll());

        // And it is usable again, not merely not-crashing.
        cache2.SaveAll(new[] { new CachedTexture(@"C:\a\c.png", 1, 2, 8, 8, null) });
        Assert.Single(cache2.LoadAll());
    }

    [Fact]
    public void Signatures_round_trip_and_are_pruned_with_their_files()
    {
        var file = TempFile();

        using var cache = new CacheDatabase();
        cache.Open(file);
        cache.SaveSignatures(new[]
        {
            new CachedSignature(@"C:\a\here.png", 10, 20, new byte[] { 1, 2, 3 }),
            new CachedSignature(@"C:\a\gone.png", 10, 20, new byte[] { 4, 5, 6 }),
        });

        Assert.Equal(2, cache.LoadSignatures().Count);

        cache.PruneMissing(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\a\here.png" });

        var left = Assert.Single(cache.LoadSignatures());
        Assert.Equal(@"C:\a\here.png", left.Key);
        Assert.Equal(new byte[] { 1, 2, 3 }, left.Value.Blob);
    }
}
