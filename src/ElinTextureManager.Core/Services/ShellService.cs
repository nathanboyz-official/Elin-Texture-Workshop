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

    /// <summary>
    /// Opens a Workshop item's page, preferring the Steam desktop client.
    ///
    /// This needs no account, no sign-in and no API key. "steam://" is a protocol
    /// handler Steam registers on this machine when it is installed; handing it a
    /// link is a purely local hand-off, exactly like double-clicking a .txt file
    /// opens Notepad. The application never authenticates with Valve, never reads
    /// anything about the Steam account, and sends nothing anywhere.
    ///
    /// Falls back to the browser when Steam is not installed, so the button still works.
    /// </summary>
    public static void OpenWorkshopPage(string? workshopId, bool preferSteamClient = true)
    {
        var target = WorkshopLink(workshopId, preferSteamClient && IsSteamClientInstalled());
        if (target is null) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not open {target}: {ex.Message}");

            // Steam may be registered but broken. The web page is always reachable.
            var web = WorkshopLink(workshopId, useSteamClient: false);
            if (web is not null && !string.Equals(web, target, StringComparison.Ordinal))
                OpenUrl(web);
        }
    }

    /// <summary>
    /// Builds the link for a Workshop item, or null when the ID is not one.
    ///
    /// The ID comes from a Workshop folder name, which is untrusted input, so it has to
    /// be all digits before it is put in a URL - that is what stops anything arbitrary
    /// being handed to the shell.
    /// </summary>
    public static string? WorkshopLink(string? workshopId, bool useSteamClient)
    {
        if (string.IsNullOrWhiteSpace(workshopId)) return null;

        foreach (var c in workshopId)
        {
            if (c is >= '0' and <= '9') continue;
            AppLog.Warn($"Refusing to open a Workshop page for a non-numeric ID: {workshopId}");
            return null;
        }

        return useSteamClient
            ? $"steam://url/CommunityFilePage/{workshopId}"
            : $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";
    }

    /// <summary>
    /// Whether the Steam desktop client is installed, read from the same registry entry
    /// the application already uses to find the game. Nothing is launched to find out.
    /// </summary>
    public static bool IsSteamClientInstalled() =>
        Detection.SteamLocator.FindSteamRoot() is not null;

    /// <summary>Elin's Steam application id, used only to build a steam:// launch link.</summary>
    public const string ElinAppId = "2135150";

    /// <summary>
    /// Starts the game through Steam.
    ///
    /// Through Steam rather than the executable directly: Workshop content is mounted by
    /// the client, so a mod search that launched the exe on its own would be testing a
    /// game with no Workshop mods in it at all.
    /// </summary>
    public static bool LaunchElin()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"steam://rungameid/{ElinAppId}")
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not start Elin through Steam", ex);
            return false;
        }
    }

    /// <summary>Opens an https URL in the default browser. Anything else is refused.</summary>
    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            AppLog.Warn($"Refusing to open a non-https URL: {url}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not open {uri.AbsoluteUri}", ex);
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
