using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The settings behind closing the window to the notification area.
///
/// The tray icon itself is Windows UI and is not testable here, but what the setting
/// defaults to and whether it survives a restart are exactly the things that would be
/// annoying to get wrong.
/// </summary>
public sealed class TraySettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "settings.json");

    [Fact]
    public void Closing_to_the_tray_is_on_by_default()
    {
        Assert.True(new AppSettings().MinimiseToTray);
    }

    [Fact]
    public void The_where_did_it_go_hint_has_not_been_shown_on_a_fresh_install()
    {
        // It fires once, the first time the window is closed. Starting it at "already
        // shown" would mean nobody ever sees it.
        Assert.False(new AppSettings().TrayHintShown);
    }

    [Fact]
    public void Both_settings_survive_a_restart()
    {
        var path = TempFile();

        var settings = new AppSettings { MinimiseToTray = false, TrayHintShown = true };
        settings.Save(path);

        var reloaded = AppSettings.Load(path);

        Assert.False(reloaded.MinimiseToTray);
        Assert.True(reloaded.TrayHintShown);
    }

    [Fact]
    public void Settings_written_before_this_feature_existed_get_the_default()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // An existing user's file has no such key. It must read as on, not as off,
        // or upgrading would quietly turn the feature off for everyone who already
        // had the application.
        File.WriteAllText(path, """{"ThumbnailSize":1,"EnableSteamNews":true}""");

        var loaded = AppSettings.Load(path);

        Assert.True(loaded.MinimiseToTray);
        Assert.False(loaded.TrayHintShown);
    }

    [Fact]
    public void Saved_colours_start_empty_and_survive_a_restart()
    {
        var path = TempFile();

        Assert.Empty(new AppSettings().SavedColours);

        var settings = new AppSettings();
        settings.SavedColours.Add("D32349");
        settings.SavedColours.Add("306369");
        settings.Save(path);

        var reloaded = AppSettings.Load(path);

        Assert.Equal(new[] { "D32349", "306369" }, reloaded.SavedColours);
    }

    [Fact]
    public void A_settings_file_from_before_saved_colours_existed_loads_with_none()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"MinimiseToTray":true}""");

        Assert.NotNull(AppSettings.Load(path).SavedColours);
        Assert.Empty(AppSettings.Load(path).SavedColours);
    }
}
