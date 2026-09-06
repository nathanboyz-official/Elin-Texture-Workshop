using System.Text;

namespace ElinTextureManager.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Small append-only file logger. Deliberately dependency-free and never throws:
/// a logging failure must not be able to take the application down.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logFile;
    private const long MaxBytes = 5 * 1024 * 1024;

    public static string? LogFile => _logFile;

    /// <summary>Raised for every entry so the UI can mirror the log live.</summary>
    public static event Action<LogLevel, string>? Entry;

    public static void Initialize(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            var file = Path.Combine(logDirectory, "ElinTextureManager.log");
            RollIfTooLarge(file);
            lock (Gate) _logFile = file;
            Info($"=== Elin Texture Manager started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch
        {
            // Logging is best-effort and must never block startup.
        }
    }

    private static void RollIfTooLarge(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                var archived = Path.Combine(fi.DirectoryName!,
                    $"ElinTextureManager_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.Move(file, archived, overwrite: true);
            }
        }
        catch { }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string message)
    {
        try { Entry?.Invoke(level, message); } catch { }

        var file = _logFile;
        if (file is null) return;

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant().PadRight(5)).Append("] ")
            .Append(message)
            .AppendLine()
            .ToString();

        lock (Gate)
        {
            try { File.AppendAllText(file, line, Encoding.UTF8); } catch { }
        }
    }
}
