using ElinTextureManager.Core.LoadOrder;
using ElinTextureManager.Core.Model;
using ElinTextureManager.Core.News;
using ElinTextureManager.Core.Scanning;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The mod-management half of the application: Workshop tags as sections, whole-mod
/// enable/disable through loadorder.txt, portrait replacements, and originals.
/// </summary>
public sealed class ModManagementTests
{
    private static ScanResult Scan(TestWorkspace ws) =>
        new ModScanner(new ScanOptions { ComputeHashes = true }).ScanSynchronously(ws.Paths);

    // ---- Workshop tags become sections ----

    [Fact]
    public void Tags_are_read_from_package_xml()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Tagged", new[] { ("objC_1.png", "a") });
        ws.SetTags("100", "NPC,Sprite");

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");

        Assert.Equal(new[] { "NPC", "Sprite" }, mod.Tags);
    }

    [Fact]
    public void Tag_casing_and_spacing_do_not_split_a_section()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Lowercase", new[] { ("objC_1.png", "a") });
        ws.SetTags("100", " sprite , qol ");

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");
        var sections = ModSection.SectionsFor(mod);

        Assert.Contains(ModSection.Sprite, sections);
        Assert.Contains(ModSection.QoL, sections);
    }

    [Fact]
    public void A_compound_tag_lands_in_every_section_it_names()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Compound", new[] { ("objC_1.png", "a") });
        ws.SetTags("100", "NPC Sprite");

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");
        var sections = ModSection.SectionsFor(mod);

        Assert.Contains(ModSection.Npc, sections);
        Assert.Contains(ModSection.Sprite, sections);
    }

    [Fact]
    public void An_unrecognised_tag_is_kept_rather_than_dropped()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Odd", new[] { ("objC_1.png", "a") });
        ws.SetTags("100", "Slime Witch");

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");

        // It lands in Other, and the author's own wording stays available.
        Assert.Equal(new[] { ModSection.Other }, ModSection.SectionsFor(mod));
        Assert.Contains("Slime Witch", ModSection.FreeFormTags(mod));
    }

    [Fact]
    public void A_mod_with_no_tags_lands_in_Other()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Untagged", new[] { ("objC_1.png", "a") });

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");

        Assert.Equal(new[] { ModSection.Other }, ModSection.SectionsFor(mod));
    }

    // ---- which mods change characters ----

    [Fact]
    public void Character_sprites_are_counted_separately_from_everything_else()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Mixed", new[]
        {
            ("objC_1.png", "a"),
            ("objCL_2.png", "b"),
            ("obj_3.png", "c"),
        });

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");

        Assert.Equal(2, mod.CharacterTextureCount);
        Assert.True(mod.ReplacesCharacters);
    }

    [Fact]
    public void A_mod_that_only_ships_items_does_not_count_as_a_character_mod()
    {
        using var ws = new TestWorkspace();
        ws.AddWorkshopMod("100", "Items", new[] { ("obj_3.png", "c") });

        var mod = Assert.Single(Scan(ws).Mods, m => m.Key == "100");

        Assert.Equal(0, mod.CharacterTextureCount);
        Assert.False(mod.ReplacesCharacters);
    }

    // ---- turning a whole mod off ----

    [Fact]
    public void Disabling_a_listed_mod_flips_its_flag()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        ws.WriteLoadOrder((dir, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.True(LoadOrderFile.SetEnabled(doc, dir, false));
        Assert.False(doc.Entries.Single().Enabled);
    }

    [Fact]
    public void Setting_a_mod_to_the_state_it_already_has_changes_nothing()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        ws.WriteLoadOrder((dir, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.False(LoadOrderFile.SetEnabled(doc, dir, true));
    }

    [Fact]
    public void Disabling_a_mod_the_game_has_not_seen_yet_appends_an_entry()
    {
        using var ws = new TestWorkspace();
        var listed = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        var unlisted = ws.AddWorkshopMod("200", "B", new[] { ("objC_2.png", "b") });
        ws.WriteLoadOrder((listed, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.True(LoadOrderFile.SetEnabled(doc, unlisted, false));
        Assert.Equal(2, doc.Entries.Count);

        var added = doc.Entries[1];
        Assert.False(added.Enabled);
        Assert.Equal(Path.GetFullPath(unlisted), added.Path);
    }

    [Fact]
    public void Enabling_a_mod_the_game_has_not_seen_yet_needs_no_entry()
    {
        using var ws = new TestWorkspace();
        var listed = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        var unlisted = ws.AddWorkshopMod("200", "B", new[] { ("objC_2.png", "b") });
        ws.WriteLoadOrder((listed, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        // A mod absent from the file is already loaded, so there is nothing to write.
        Assert.False(LoadOrderFile.SetEnabled(doc, unlisted, true));
        Assert.Single(doc.Entries);
    }

    [Fact]
    public void A_disabled_mod_survives_a_round_trip_through_the_file()
    {
        using var ws = new TestWorkspace();
        var dir = ws.AddWorkshopMod("100", "A", new[] { ("objC_1.png", "a") });
        ws.WriteLoadOrder((dir, true));

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.SetEnabled(doc, dir, false);

        Assert.True(LoadOrderFile.Save(doc, ws.BackupDirectory, out _));

        var scan = Scan(ws);
        var reread = LoadOrderFile.Read(ws.Paths.LoadOrderFile);
        LoadOrderFile.ApplyTo(reread, scan.Mods);

        Assert.False(Assert.Single(scan.Mods, m => m.Key == "100").Enabled);
    }

    [Fact]
    public void An_unparsed_line_is_never_rewritten()
    {
        using var ws = new TestWorkspace();
        File.WriteAllText(ws.Paths.LoadOrderFile, "something we do not understand\r\n");

        var doc = LoadOrderFile.Read(ws.Paths.LoadOrderFile);

        Assert.False(LoadOrderFile.SetEnabled(doc, "something we do not understand", false));
        Assert.Equal("something we do not understand", doc.Entries.Single().Serialize());
    }
}
