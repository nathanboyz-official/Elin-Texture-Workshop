using System.IO.Compression;
using System.Security.Cryptography;
using ElinTextureManager.Core.Updates;
using Xunit;

namespace ElinTextureManager.Tests;

public sealed class UpdateTests
{
    private const string Download =
        "https://github.com/nathanboyz-official/Elin-Texture-Workshop/releases/download/";

    private static string Release(string tag, string assetName, string url, long size = 123,
        string? digest = "sha256:ABCDEF") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/nathanboyz-official/Elin-Texture-Workshop/releases/tag/{{tag}}",
          "body": "notes",
          "assets": [
            { "name": "source.txt", "browser_download_url": "{{Download}}{{tag}}/source.txt", "size": 1 },
            { "name": "{{assetName}}", "browser_download_url": "{{url}}", "size": {{size}}
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void Reads_the_windows_build_out_of_a_release()
    {
        var json = Release("v1.2.3", "ElinTextureWorkshop-v1.2.3-win-x64.zip",
            Download + "v1.2.3/ElinTextureWorkshop-v1.2.3-win-x64.zip");

        var release = UpdateClient.Parse(json)!;

        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal("ElinTextureWorkshop-v1.2.3-win-x64.zip", release.AssetName);
        Assert.Equal(123, release.Size);
        Assert.Equal("abcdef", release.Sha256);
    }

    [Fact]
    public void A_download_from_anywhere_else_is_not_offered()
    {
        var json = Release("v1.2.3", "ElinTextureWorkshop-v1.2.3-win-x64.zip",
            "https://example.com/ElinTextureWorkshop-v1.2.3-win-x64.zip");

        Assert.Null(UpdateClient.Parse(json));
    }

    [Fact]
    public void A_release_without_a_windows_zip_is_not_offered()
    {
        var json = Release("v1.2.3", "notes.pdf", Download + "v1.2.3/notes.pdf");

        Assert.Null(UpdateClient.Parse(json));
    }

    [Theory]
    [InlineData("v1.0.2", "1.0.2")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("V2.0.0", "2.0.0")]
    public void Tags_read_as_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateClient.ParseTag(tag));

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void Tags_that_are_not_versions_are_ignored(string? tag) =>
        Assert.Null(UpdateClient.ParseTag(tag));

    [Fact]
    public void A_four_part_assembly_version_equals_its_three_part_tag()
    {
        // The assembly says 1.0.2.0; the tag says v1.0.2. Treating those as different
        // would offer the version already running as an update, forever.
        Assert.Equal(UpdateClient.ParseTag("v1.0.2"), UpdateClient.Normalise(new Version(1, 0, 2, 0)));
    }

    [Fact]
    public void The_build_carries_a_real_version()
    {
        // Directory.Build.props stamps every project. Without it the version is 1.0.0.0
        // and every release would look newer than the copy that just installed it.
        var stamped = UpdateClient.Normalise(typeof(UpdateClient).Assembly.GetName().Version!);
        Assert.True(stamped > new Version(1, 0, 1), $"stamped {stamped}");
    }

    [Fact]
    public void A_download_that_does_not_match_its_checksum_is_refused()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "hello");
            var good = Convert.ToHexString(SHA256.HashData("hello"u8.ToArray())).ToLowerInvariant();

            var ok = new ReleaseInfo(new Version(1, 0, 0), "v1.0.0", "", "", "a.zip", Download, 5, good);
            UpdateClient.Verify(ok, file);

            Assert.Throws<InvalidDataException>(() => UpdateClient.Verify(ok with { Sha256 = new string('0', 64) }, file));
            Assert.Throws<InvalidDataException>(() => UpdateClient.Verify(ok with { Size = 6 }, file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Staging_finds_the_executable_and_rejects_a_zip_without_one()
    {
        using var ws = new TestWorkspace();
        var good = Path.Combine(ws.Root, "good.zip");
        using (var zip = ZipFile.Open(good, ZipArchiveMode.Create))
        {
            zip.CreateEntry(UpdateInstaller.ExecutableName);
            zip.CreateEntry("ElinTextureWorkshop.dll");
        }

        var staged = UpdateInstaller.Stage(good, Path.Combine(ws.Root, "stage"));
        Assert.True(File.Exists(Path.Combine(staged, UpdateInstaller.ExecutableName)));

        var bad = Path.Combine(ws.Root, "bad.zip");
        using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) zip.CreateEntry("readme.txt");

        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Stage(bad, Path.Combine(ws.Root, "stage2")));
    }

    [Fact]
    public void Folder_names_with_quotes_cannot_break_out_of_the_script()
    {
        Assert.Equal("'C:\\Bob''s Mods'", UpdateInstaller.Quote("C:\\Bob's Mods"));
        Assert.Equal("'a\u2019\u2019b'", UpdateInstaller.Quote("a\u2019b"));
    }

    [Fact]
    public void The_finish_script_waits_copies_and_relaunches()
    {
        using var ws = new TestWorkspace();
        var script = UpdateInstaller.WriteFinishScript(
            Path.Combine(ws.Root, "staged"), Path.Combine(ws.Root, "app"), 4242, ws.Root);

        var text = File.ReadAllText(script);
        Assert.Contains("Wait-Process -Id 4242", text);
        Assert.Contains("robocopy $staged $install /E", text);
        Assert.Contains("Start-Process -FilePath $exe", text);
        Assert.DoesNotContain("/MIR", text);
        Assert.True(UpdateInstaller.CanWriteTo(ws.Root));
    }
}
