using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.Storage;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Profiles, and the setup files that move one between machines.
///
/// A setup file records Workshop IDs, texture IDs and on/off states - what to do, not
/// copies of anyone's art. So importing one has to be able to say what it could not do,
/// which is most of what these pin down.
/// </summary>
public sealed class SetupTests
{
    private static SelectionStore NewStore(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "selections.json");
        var store = new SelectionStore(path);
        store.Load();
        return store;
    }

    private static OverrideSelection Selection(string textureId, string modKey,
        string? workshopId = null, string modName = "A Mod") => new()
    {
        TextureId = textureId,
        FileName = textureId + ".png",
        SourceModKey = modKey,
        SourceWorkshopId = workshopId,
        SourceModName = modName,
        SourcePath = @"C:\mods\" + modKey + @"\" + textureId + ".png",
    };

    private static ModPackage Mod(string key, string name, bool enabled = true,
        string? workshopId = null) => new()
    {
        Key = key,
        Name = name,
        Directory = @"C:\mods\" + key,
        SourceType = TextureSourceType.Workshop,
        WorkshopId = workshopId ?? key,
        Enabled = enabled,
    };

    [Fact]
    public void A_new_profile_starts_empty_and_leaves_the_current_one_alone()
    {
        var store = NewStore(out _);
        store.Set(Selection("objC_1", "100"));

        Assert.True(store.CreateProfile("Anime"));

        Assert.Equal(1, store.CountIn("Default"));
        Assert.Equal(0, store.CountIn("Anime"));
        Assert.Equal("Default", store.ActiveProfile);
    }

    [Fact]
    public void A_copied_profile_carries_the_choices_but_not_the_link()
    {
        var store = NewStore(out _);
        store.Set(Selection("objC_1", "100"));

        store.CreateProfile("Anime", copyFrom: "Default");
        store.ActiveProfile = "Anime";
        store.Remove("objC_1");

        // Changing the copy must not reach back into the original.
        Assert.Equal(0, store.CountIn("Anime"));
        Assert.Equal(1, store.CountIn("Default"));
    }

    [Fact]
    public void A_name_already_in_use_is_refused_rather_than_merged()
    {
        var store = NewStore(out _);
        store.Set(Selection("objC_1", "100"));

        // Merging two profiles under one name would lose a set of choices silently.
        Assert.False(store.CreateProfile("Default"));
        Assert.Equal(1, store.CountIn("Default"));
    }

    [Fact]
    public void The_last_profile_cannot_be_deleted()
    {
        var store = NewStore(out _);

        // With no profiles there is nowhere for the next selection to go.
        Assert.False(store.DeleteProfile("Default"));
        Assert.Single(store.Profiles);
    }

    [Fact]
    public void Deleting_the_profile_in_use_moves_to_one_that_is_left()
    {
        var store = NewStore(out _);
        store.CreateProfile("Anime");
        store.ActiveProfile = "Anime";

        Assert.True(store.DeleteProfile("Anime"));
        Assert.Equal("Default", store.ActiveProfile);
    }

    [Fact]
    public void Renaming_takes_the_selections_and_the_active_marker_with_it()
    {
        var store = NewStore(out _);
        store.Set(Selection("objC_1", "100"));

        Assert.True(store.RenameProfile("Default", "Vanilla+"));

        Assert.Equal("Vanilla+", store.ActiveProfile);
        Assert.Equal(1, store.CountIn("Vanilla+"));
        Assert.Equal("Vanilla+", store.Get("objC_1")!.Profile);
    }

    [Fact]
    public void Profiles_survive_a_save_and_reload()
    {
        var store = NewStore(out var path);
        store.Set(Selection("objC_1", "100"));
        store.CreateProfile("Anime");
        store.ActiveProfile = "Anime";
        store.Set(Selection("objC_2", "200"));
        store.Save();

        var reloaded = new SelectionStore(path);
        reloaded.Load();

        Assert.Equal("Anime", reloaded.ActiveProfile);
        Assert.Equal(1, reloaded.CountIn("Default"));
        Assert.Equal(1, reloaded.CountIn("Anime"));
    }

    [Fact]
    public void A_setup_file_survives_the_round_trip()
    {
        var mods = new[] { Mod("100", "Pack A"), Mod("200", "Pack B", enabled: false) };
        var selections = new[] { Selection("objC_1", "100", "100", "Pack A") };

        var written = SetupFile.Write(SetupFile.Build(mods, selections, "Anime", "for a friend"));
        var read = SetupFile.Read(written);

        Assert.NotNull(read);
        Assert.Equal("Anime", read!.Profile);
        Assert.Equal("for a friend", read.Note);
        Assert.Equal(2, read.Mods.Count);
        Assert.Single(read.Selections);
        Assert.False(read.Mods.Single(m => m.WorkshopId == "200").Enabled);
    }

    [Fact]
    public void A_file_that_is_not_a_setup_is_rejected()
    {
        Assert.Null(SetupFile.Read("not json at all"));
        Assert.Null(SetupFile.Read("{}"));

        // Right shape, nothing in it. Applying that would silently empty a profile.
        Assert.Null(SetupFile.Read("""{"Profile":"X","Mods":[],"Selections":[]}"""));
    }

    [Fact]
    public void Importing_names_the_mods_this_machine_does_not_have()
    {
        var setup = SetupFile.Build(
            new[] { Mod("100", "Pack A"), Mod("999", "Someone Elses Pack") },
            new[] { Selection("objC_1", "999", "999", "Someone Elses Pack") },
            "Theirs");

        var plan = SetupFile.Plan(setup, new[] { Mod("100", "Pack A") });

        Assert.False(plan.IsComplete);
        Assert.Contains("Someone Elses Pack (999)", plan.MissingMods);
        Assert.Empty(plan.Applicable);
        Assert.Single(plan.UnavailableSelections);
    }

    [Fact]
    public void Selections_whose_mod_is_installed_are_applicable()
    {
        var setup = SetupFile.Build(
            new[] { Mod("100", "Pack A") },
            new[] { Selection("objC_1", "100", "100", "Pack A") },
            "Theirs");

        var plan = SetupFile.Plan(setup, new[] { Mod("100", "Pack A") });

        Assert.True(plan.IsComplete);
        Assert.Single(plan.Applicable);
        Assert.Equal("objC_1", plan.Applicable[0].TextureId);
    }

    [Fact]
    public void A_setup_matches_mods_by_workshop_id_not_by_folder_path()
    {
        // The same mod lives at a different absolute path on every machine, which is the
        // whole reason a setup file records IDs.
        var setup = SetupFile.Build(
            new[] { Mod("100", "Pack A", workshopId: "3375278123") },
            new[] { Selection("objC_1", "somebody-elses-key", "3375278123", "Pack A") },
            "Theirs");

        var here = new ModPackage
        {
            Key = "my-own-key",
            Name = "Pack A",
            Directory = @"D:\Games\Elin\workshop\3375278123",
            SourceType = TextureSourceType.Workshop,
            WorkshopId = "3375278123",
        };

        var plan = SetupFile.Plan(setup, new[] { here });

        Assert.Empty(plan.MissingMods);
        Assert.Single(plan.Applicable);
    }

    [Fact]
    public void Only_mods_whose_state_differs_are_reported_as_changes()
    {
        var setup = SetupFile.Build(
            new[] { Mod("100", "Pack A", enabled: false), Mod("200", "Pack B") },
            Array.Empty<OverrideSelection>(), "Theirs");

        // Pack B already matches, so touching it would be a pointless rewrite of
        // loadorder.txt - and every rewrite of that file is a chance to get it wrong.
        var installed = new[] { Mod("100", "Pack A"), Mod("200", "Pack B") };

        var changes = SetupFile.EnabledChanges(setup, installed);

        var change = Assert.Single(changes);
        Assert.Equal("100", change.Mod.Key);
        Assert.False(change.Enabled);
    }

    [Fact]
    public void Replacing_a_profile_stamps_the_selections_with_its_name()
    {
        var store = NewStore(out _);
        store.CreateProfile("Imported");

        store.ReplaceProfile("Imported", new[] { Selection("objC_1", "100") });
        store.ActiveProfile = "Imported";

        Assert.Equal("Imported", store.Get("objC_1")!.Profile);
    }
}
