using ElinTextureManager.Core.Logging;
using Microsoft.Data.Sqlite;

namespace ElinTextureManager.Core.Storage;

/// <summary>One cached colour signature, keyed by absolute path.</summary>
public sealed record CachedSignature(
    string Path,
    long Size,
    long ModifiedTicks,
    byte[] Blob);

/// <summary>One cached texture record, keyed by absolute path.</summary>
public sealed record CachedTexture(
    string Path,
    long Size,
    long ModifiedTicks,
    int Width,
    int Height,
    string? Hash);

/// <summary>
/// A local SQLite index of texture metadata so repeat launches do not have to re-hash
/// and re-read every PNG. Entries are validated against file size and modified time,
/// so a Steam update invalidates them automatically.
///
/// The cache is an optimisation only: if anything goes wrong it is disabled and the
/// scanner falls back to reading from disk.
/// </summary>
public sealed class CacheDatabase : IDisposable
{
    private SqliteConnection? _connection;
    private readonly object _gate = new();

    public bool IsOpen => _connection is not null;

    public void Open(string databaseFile)
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(databaseFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = databaseFile,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                }.ToString());

                connection.Open();

                // A half-written database stays broken forever otherwise: every read
                // fails, every launch logs the same error, and nothing ever rebuilds it.
                // The cache holds nothing that cannot be recomputed, so the answer to a
                // corrupt one is to throw it away.
                if (!IsIntact(connection))
                {
                    AppLog.Error($"The texture cache at {databaseFile} is corrupt; rebuilding it.");
                    connection.Close();
                    SqliteConnection.ClearAllPools();
                    DeleteDatabaseFiles(databaseFile);

                    connection.Open();
                }

                Execute(connection, "PRAGMA journal_mode=WAL;");
                Execute(connection, "PRAGMA synchronous=NORMAL;");
                Execute(connection, """
                    CREATE TABLE IF NOT EXISTS textures (
                        path           TEXT PRIMARY KEY,
                        size           INTEGER NOT NULL,
                        modified_ticks INTEGER NOT NULL,
                        width          INTEGER NOT NULL,
                        height         INTEGER NOT NULL,
                        hash           TEXT
                    );
                    """);
                Execute(connection, "CREATE INDEX IF NOT EXISTS ix_textures_hash ON textures(hash);");

                // Colour signatures for the reverse lookup. A separate table because it
                // is filled by a different pass: building one means decoding the image,
                // which the scanner deliberately does not do.
                Execute(connection, """
                    CREATE TABLE IF NOT EXISTS signatures (
                        path           TEXT PRIMARY KEY,
                        size           INTEGER NOT NULL,
                        modified_ticks INTEGER NOT NULL,
                        blob           BLOB NOT NULL
                    );
                    """);

                _connection = connection;
                AppLog.Info($"Texture cache opened: {databaseFile}");
            }
            catch (Exception ex)
            {
                AppLog.Error($"Could not open the texture cache at {databaseFile}; "
                             + "continuing without it", ex);
                _connection = null;
            }
        }
    }

    /// <summary>
    /// Asks SQLite whether the file is sound. quick_check rather than integrity_check:
    /// it catches a malformed image, which is what an interrupted write leaves behind,
    /// without walking every index on a database of thousands of rows at every launch.
    /// </summary>
    private static bool IsIntact(SqliteConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check(1);";
            return cmd.ExecuteScalar() as string == "ok";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes the database and its write-ahead companions, which travel together.</summary>
    private static void DeleteDatabaseFiles(string databaseFile)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(databaseFile + suffix); }
            catch (Exception ex) { AppLog.Error($"Could not remove {databaseFile}{suffix}", ex); }
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Loads the whole cache into memory - far faster than a query per file.</summary>
    public Dictionary<string, CachedTexture> LoadAll()
    {
        var map = new Dictionary<string, CachedTexture>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            if (_connection is null) return map;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT path, size, modified_ticks, width, height, hash FROM textures;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    map[path] = new CachedTexture(
                        path,
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.GetInt32(3),
                        reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5));
                }

                AppLog.Info($"Texture cache: {map.Count} entries loaded.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not read the texture cache", ex);
            }
        }

        return map;
    }

    /// <summary>Upserts a batch inside one transaction.</summary>
    public void SaveAll(IEnumerable<CachedTexture> textures)
    {
        lock (_gate)
        {
            if (_connection is null) return;

            try
            {
                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO textures (path, size, modified_ticks, width, height, hash)
                    VALUES ($path, $size, $ticks, $w, $h, $hash)
                    ON CONFLICT(path) DO UPDATE SET
                        size = excluded.size,
                        modified_ticks = excluded.modified_ticks,
                        width = excluded.width,
                        height = excluded.height,
                        hash = excluded.hash;
                    """;

                var pPath = cmd.CreateParameter(); pPath.ParameterName = "$path"; cmd.Parameters.Add(pPath);
                var pSize = cmd.CreateParameter(); pSize.ParameterName = "$size"; cmd.Parameters.Add(pSize);
                var pTicks = cmd.CreateParameter(); pTicks.ParameterName = "$ticks"; cmd.Parameters.Add(pTicks);
                var pW = cmd.CreateParameter(); pW.ParameterName = "$w"; cmd.Parameters.Add(pW);
                var pH = cmd.CreateParameter(); pH.ParameterName = "$h"; cmd.Parameters.Add(pH);
                var pHash = cmd.CreateParameter(); pHash.ParameterName = "$hash"; cmd.Parameters.Add(pHash);

                var count = 0;
                foreach (var t in textures)
                {
                    pPath.Value = t.Path;
                    pSize.Value = t.Size;
                    pTicks.Value = t.ModifiedTicks;
                    pW.Value = t.Width;
                    pH.Value = t.Height;
                    pHash.Value = (object?)t.Hash ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                    count++;
                }

                tx.Commit();
                AppLog.Debug($"Texture cache: {count} entries written.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not write the texture cache", ex);
            }
        }
    }

    /// <summary>
    /// Loads every stored colour signature. The caller checks size and modified time
    /// against the file on disk: a repainted texture keeps its path.
    /// </summary>
    public Dictionary<string, CachedSignature> LoadSignatures()
    {
        var map = new Dictionary<string, CachedSignature>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            if (_connection is null) return map;

            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT path, size, modified_ticks, blob FROM signatures;";
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    var blob = (byte[])reader.GetValue(3);
                    map[path] = new CachedSignature(path, reader.GetInt64(1), reader.GetInt64(2), blob);
                }

                AppLog.Info($"Signature cache: {map.Count} entries loaded.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not read the signature cache", ex);
            }
        }

        return map;
    }

    /// <summary>Upserts a batch of signatures inside one transaction.</summary>
    public void SaveSignatures(IEnumerable<CachedSignature> signatures)
    {
        lock (_gate)
        {
            if (_connection is null) return;

            try
            {
                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO signatures (path, size, modified_ticks, blob)
                    VALUES ($path, $size, $ticks, $blob)
                    ON CONFLICT(path) DO UPDATE SET
                        size = excluded.size,
                        modified_ticks = excluded.modified_ticks,
                        blob = excluded.blob;
                    """;

                var pPath = cmd.CreateParameter(); pPath.ParameterName = "$path"; cmd.Parameters.Add(pPath);
                var pSize = cmd.CreateParameter(); pSize.ParameterName = "$size"; cmd.Parameters.Add(pSize);
                var pTicks = cmd.CreateParameter(); pTicks.ParameterName = "$ticks"; cmd.Parameters.Add(pTicks);
                var pBlob = cmd.CreateParameter(); pBlob.ParameterName = "$blob"; cmd.Parameters.Add(pBlob);

                var count = 0;
                foreach (var s in signatures)
                {
                    pPath.Value = s.Path;
                    pSize.Value = s.Size;
                    pTicks.Value = s.ModifiedTicks;
                    pBlob.Value = s.Blob;
                    cmd.ExecuteNonQuery();
                    count++;
                }

                tx.Commit();
                AppLog.Debug($"Signature cache: {count} entries written.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not write the signature cache", ex);
            }
        }
    }

    /// <summary>Drops rows for files that no longer exist, keeping the cache from growing forever.</summary>
    public void PruneMissing(IReadOnlySet<string> livePaths)
    {
        lock (_gate)
        {
            if (_connection is null) return;

            try
            {
                var stale = LoadAll().Keys
                    .Concat(LoadSignatures().Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(p => !livePaths.Contains(p))
                    .ToList();

                if (stale.Count == 0) return;

                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                // Both tables are keyed by path, and an unsubscribed mod takes its
                // signatures with it just as it takes its metadata.
                cmd.CommandText = "DELETE FROM textures WHERE path = $path;"
                                  + "DELETE FROM signatures WHERE path = $path;";
                var p = cmd.CreateParameter(); p.ParameterName = "$path"; cmd.Parameters.Add(p);

                foreach (var path in stale) { p.Value = path; cmd.ExecuteNonQuery(); }

                tx.Commit();
                AppLog.Info($"Texture cache: pruned {stale.Count} stale entries.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not prune the texture cache", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _connection?.Close(); _connection?.Dispose(); }
            catch { }
            _connection = null;
        }
    }
}
