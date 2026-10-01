using System.IO.Compression;
using System.Text;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.Core.Updates;

/// <summary>
/// Puts a downloaded release in place of the running one.
///
/// A running program cannot overwrite its own files, so this happens in two halves. While
/// the application is still open the zip is unpacked into a staging folder and checked.
/// Then a short PowerShell script is written that waits for this process to exit, copies
/// the staged files over the install folder, and starts the new version. Files are only
/// added or replaced, never deleted, so anything the user keeps beside the executable
/// survives. Settings, choices and the cache live in %APPDATA% and are not touched at all.
/// </summary>
public static class UpdateInstaller
{
    public const string ExecutableName = "ElinTextureWorkshop.exe";

    /// <summary>Where downloads and staging go; cleared before each update.</summary>
    public static string WorkRoot => Path.Combine(Path.GetTempPath(), "ElinTextureWorkshop-update");

    /// <summary>
    /// Whether the folder the application runs from can be written to. It cannot when the
    /// user put it under Program Files, and then the only honest offer is the download page.
    /// </summary>
    public static bool CanWriteTo(string installDir)
    {
        try
        {
            var probe = Path.Combine(installDir, $".update-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Install folder is not writable ({installDir}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unpacks the release into <paramref name="stagingDir"/> and returns the folder that
    /// holds the executable. Throws when the zip is not a build of this application.
    /// </summary>
    public static string Stage(string zipPath, string stagingDir)
    {
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        // ExtractToDirectory refuses entries that would land outside the folder.
        ZipFile.ExtractToDirectory(zipPath, stagingDir);

        // The release zip has the files at its root; allow one wrapping folder in case a
        // future zip is packed differently, but nothing deeper.
        var root = stagingDir;
        if (!File.Exists(Path.Combine(root, ExecutableName)))
        {
            var dirs = Directory.GetDirectories(stagingDir);
            if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], ExecutableName)))
                root = dirs[0];
            else
                throw new InvalidDataException($"The download does not contain {ExecutableName}.");
        }

        AppLog.Info($"Update staged in {root}.");
        return root;
    }

    /// <summary>
    /// Writes the script that finishes the update once process <paramref name="processId"/>
    /// has exited, and returns its path.
    /// </summary>
    public static string WriteFinishScript(string stagedDir, string installDir, int processId,
        string scriptDir)
    {
        Directory.CreateDirectory(scriptDir);

        var script = Path.Combine(scriptDir, "finish-update.ps1");
        var log = Path.Combine(scriptDir, "finish-update.log");
        var exe = Path.Combine(installDir, ExecutableName);

        var text = new StringBuilder()
            .AppendLine("# Written by Elin Texture Workshop to finish installing an update.")
            .AppendLine($"$staged  = {Quote(stagedDir)}")
            .AppendLine($"$install = {Quote(installDir)}")
            .AppendLine($"$exe     = {Quote(exe)}")
            .AppendLine($"$log     = {Quote(log)}")
            .AppendLine("\"Waiting for the application to close\" | Out-File $log -Encoding utf8")
            .AppendLine($"Wait-Process -Id {processId} -Timeout 120 -ErrorAction SilentlyContinue")
            .AppendLine("Start-Sleep -Milliseconds 500")
            // Robocopy retries files that are still briefly locked, and exit codes below 8
            // all mean success.
            .AppendLine("robocopy $staged $install /E /R:20 /W:1 /NP /NDL /NJH | Out-File $log -Append -Encoding utf8")
            .AppendLine("$code = $LASTEXITCODE")
            .AppendLine("\"robocopy exit code $code\" | Out-File $log -Append -Encoding utf8")
            // Started either way: on failure the old files are still there and still work.
            .AppendLine("Start-Process -FilePath $exe -WorkingDirectory $install")
            .AppendLine("if ($code -lt 8) { Remove-Item -LiteralPath $staged -Recurse -Force -ErrorAction SilentlyContinue }")
            .ToString();

        // With a BOM, so Windows PowerShell 5.1 reads non-ASCII folder names correctly.
        File.WriteAllText(script, text, new UTF8Encoding(true));
        return script;
    }

    /// <summary>
    /// A PowerShell single-quoted literal. Only quote marks need escaping, by doubling -
    /// and PowerShell counts the curly ones as quote marks too.
    /// </summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder("'");
        foreach (var c in value)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }
}
