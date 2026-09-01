using System.Diagnostics;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Services;

/// <summary>Windows Explorer integration for the "Open folder" buttons.</summary>
public static class ShellService
{
    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (!Directory.Exists(path))
            {
                AppLog.Warn($"Cannot open missing folder: {path}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not open folder {path}", ex);
        }
    }

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void RevealFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (!File.Exists(path))
            {
                OpenFolder(Path.GetDirectoryName(path));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not reveal file {path}", ex);
        }
    }
}

/// <summary>
/// Detects whether Elin is running, so the UI can tell the user that changes take
/// effect on restart. The application never touches the running game.
/// </summary>
public static class GameProcessDetector
{
    private const string ProcessName = "Elin";

    public static bool IsElinRunning()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName).Length > 0;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"Could not check for a running Elin process: {ex.Message}");
            return false;
        }
    }
}
