namespace ElinTextureManager.Core.Guide;

/// <summary>A link out to where the full detail lives.</summary>
public sealed record GuideLink(string Label, string Url);

/// <summary>One paragraph, list item or code line of a topic.</summary>
public sealed record GuideLine(string Text, GuideLineKind Kind = GuideLineKind.Body);

public enum GuideLineKind
{
    Body,
    Bullet,
    Code,
    /// <summary>A thing that fails silently. The reason this page exists.</summary>
    Trap,
}

public sealed class GuideTopic
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public List<GuideLine> Lines { get; } = new();
    public List<GuideLink> Links { get; } = new();
}

public sealed class GuideSection
{
    public required string Title { get; init; }
    public List<GuideTopic> Topics { get; } = new();
}

/// <summary>
/// A short modding reference, kept inside the application.
///
/// Written here rather than copied in. The community wiki and the decompiled game are
/// both excellent and both carry no licence at all, so their text cannot be shipped
/// inside an MIT application - doing so would relicense someone else's work, and in the
/// decompile's case redistribute the game's own code. What is written below are the
/// facts, in this project's own words, every one of them checked against the game while
/// building the feature it describes. Facts are not anyone's property; prose is, so this
/// is prose of our own.
///
/// The bias is deliberate: this covers the things that fail silently. Elin loads a
/// misnamed portrait and never shows it, reads a source sheet up to the first blank id
/// and drops the rest, and ignores a PCC part whose name has an underscore in the wrong
/// place. None of that is reported anywhere, in the game or out of it, which is what
/// makes it worth writing down.
/// </summary>
public static class ModdingGuide
{
    public const string WikiUrl = "https://elin-modding-resources.github.io/Elin.Docs/";
    public const string DecompileUrl = "https://elin-modding-resources.github.io/Elin-Decompiled/";
    public const string ModMakerUrl = "https://modmaker.elin-modding.net/";
    public const string DiscordUrl = "https://discord.gg/elona";

    public static IReadOnlyList<GuideSection> Sections { get; } = Build();

    private static List<GuideSection> Build()
    {
        return new List<GuideSection>
        {
            Anatomy(),
            Sheets(),
            Art(),
            Where(),
        };
    }

    private static GuideSection Anatomy()
    {
        var section = new GuideSection { Title = "How a mod is put together" };

        var folders = new GuideTopic
        {
            Title = "The folders",
            Summary = "A mod is a folder under Elin\\Package. Only two things in it are "
                      + "required; every other folder is there because you use it.",
        };

        folders.Lines.AddRange(new[]
        {
            new GuideLine("Package\\<YourMod>\\", GuideLineKind.Code),
            new GuideLine("  package.xml          required - what the mod is", GuideLineKind.Code),
            new GuideLine("  preview.jpg          required - the picture on the Workshop", GuideLineKind.Code),
            new GuideLine("  LangMod\\EN\\*.xlsx    source sheets: characters, items, the rest", GuideLineKind.Code),
            new GuideLine("  Texture Replace\\     sprites addressed by their slot in the atlas", GuideLineKind.Code),
            new GuideLine("  Portrait\\            portraits replacing one of the game's by name", GuideLineKind.Code),
            new GuideLine("  Actor\\PCC\\female\\    the layered parts characters are built from", GuideLineKind.Code),
            new GuideLine("  Texture\\             whole loose images mirroring _Elona\\Texture", GuideLineKind.Code),
            new GuideLine("  Sound\\               music and effects", GuideLineKind.Code),
            new GuideLine("A package mirrors the layout of the base game's own package, "
                          + "_Elona. That is the whole rule: put a file where _Elona keeps "
                          + "the one you are replacing.", GuideLineKind.Body),
        });

        var xml = new GuideTopic
        {
            Title = "package.xml",
            Summary = "The file that makes a folder a mod.",
        };

        xml.Lines.AddRange(new[]
        {
            new GuideLine("<title>       the name shown on the Workshop", GuideLineKind.Code),
            new GuideLine("<id>          unique, and never changed after publishing", GuideLineKind.Code),
            new GuideLine("<author>      you", GuideLineKind.Code),
            new GuideLine("<version>     the game version you tested against", GuideLineKind.Code),
            new GuideLine("<tags>        Workshop categories, comma separated", GuideLineKind.Code),
            new GuideLine("<loadPriority> -999 to 999; lower loads first", GuideLineKind.Code),
            new GuideLine("<builtin>     false", GuideLineKind.Code),
            new GuideLine("Changing <id> after you have published makes Steam treat it as a "
                          + "different mod. Everyone subscribed to the old one keeps the old "
                          + "one, and your update reaches nobody.", GuideLineKind.Trap),
            new GuideLine("loadorder.txt overrides loadPriority as soon as anyone reorders "
                          + "their mods, so priority is a starting suggestion rather than a "
                          + "guarantee.", GuideLineKind.Body),
        });

        section.Topics.Add(folders);
        section.Topics.Add(xml);
        return section;
    }

    private static GuideSection Sheets()
    {
        var section = new GuideSection { Title = "Adding characters and items" };

        var basics = new GuideTopic
        {
            Title = "Source sheets",
            Summary = "Most content needs no code. It is spreadsheets: .xlsx files under "
                      + "LangMod\\EN, read by the name of the tab rather than the name of "
                      + "the file.",
        };

        basics.Lines.AddRange(new[]
        {
            new GuideLine("Row 1 is the header - the column names.", GuideLineKind.Bullet),
            new GuideLine("Row 2 is the type of each column.", GuideLineKind.Bullet),
            new GuideLine("Row 3 is the default for each column.", GuideLineKind.Bullet),
            new GuideLine("Row 4 is where your own entries start.", GuideLineKind.Bullet),
            new GuideLine("Copy the first three rows out of the official sheet whole. A "
                          + "missing column is filled with nothing and logged as "
                          + "\"#source ill-format\" in Player.log, which you will not be "
                          + "reading at the time.", GuideLineKind.Body),
            new GuideLine("A row with an empty id stops the sheet being read. Every row "
                          + "below it is dropped, and nothing anywhere says so - not the "
                          + "game, not the log. Do not leave blank rows to group your "
                          + "entries. This application checks for it on the Mod Health "
                          + "page.", GuideLineKind.Trap),
            new GuideLine("An empty cell is not an empty value. It falls back to row 3, so "
                          + "a blank category quietly becomes \"other\", a blank race "
                          + "becomes \"norland\", a blank defMat becomes \"oak\".",
                GuideLineKind.Trap),
            new GuideLine("An id that matches a vanilla entry replaces it rather than "
                          + "adding to it.", GuideLineKind.Trap),
        });

        var names = new GuideTopic
        {
            Title = "Tab names the game reads",
            Summary = "The tab name is what decides whether a sheet is read at all. A tab "
                      + "named anything else is never looked at.",
        };

        names.Lines.AddRange(new[]
        {
            new GuideLine("Chara, CharaText, Tactics, Race, Job, Hobby", GuideLineKind.Code),
            new GuideLine("Thing, ThingV, Food, Recipe, SpawnList, Category, Collectible, KeyItem", GuideLineKind.Code),
            new GuideLine("Element, Calc, Stat, Check, Faction, Religion", GuideLineKind.Code),
            new GuideLine("Zone, ZoneAffix, Quest, Area, HomeResource, Research, Person", GuideLineKind.Code),
            new GuideLine("GlobalTile, Block, Floor, Obj, Deco, CellEffect, Material", GuideLineKind.Code),
            new GuideLine("For text: General, Game, List, Word, Note", GuideLineKind.Code),
            new GuideLine("One .xlsx can hold as many of these tabs as you like, or you can "
                          + "split them across files. The file name is yours to choose.",
                GuideLineKind.Body),
        });

        section.Topics.Add(basics);
        section.Topics.Add(names);
        return section;
    }

    private static GuideSection Art()
    {
        var section = new GuideSection { Title = "Art" };

        var portraits = new GuideTopic
        {
            Title = "Portraits",
            Summary = "240x320 PNG. The game reads the group and who it is offered to out "
                      + "of the file name, so the name is not decoration.",
        };

        portraits.Lines.AddRange(new[]
        {
            new GuideLine("group_gender_name.png", GuideLineKind.Code),
            new GuideLine("c_f_agnes.png        a female character portrait", GuideLineKind.Code),
            new GuideLine("guard_m_vera.png     offered for male guards", GuideLineKind.Code),
            new GuideLine("special_n_mine.png   offered to anyone", GuideLineKind.Code),
            new GuideLine("The group must be one of c, guard, special or foxfolk - those are "
                          + "the four the character screen asks for. The gender letter is m, "
                          + "f, or anything else for \"anyone\".", GuideLineKind.Body),
            new GuideLine("A portrait whose name does not start with one of those groups is "
                          + "loaded and then never shown, because nothing ever asks for its "
                          + "group. A file called agnes.png does nothing at all.",
                GuideLineKind.Trap),
            new GuideLine("Drop your own in Elin\\Custom\\Portrait to add one alongside the "
                          + "game's. Put one in your mod's Portrait folder, named after a "
                          + "vanilla file, to replace that one instead.", GuideLineKind.Body),
            new GuideLine("<id>-overlay.png is drawn over the portrait and tinted with hair "
                          + "colour. <id>-full.png is the large art. Neither is a portrait "
                          + "in its own right.", GuideLineKind.Body),
        });

        var pcc = new GuideTopic
        {
            Title = "PCC parts",
            Summary = "The layered pieces a character is built from. 128x192 PNG: four "
                      + "frames across, four facings down, each cell 32x48.",
        };

        pcc.Lines.AddRange(new[]
        {
            new GuideLine("pcc_<layer>_<id>.png", GuideLineKind.Code),
            new GuideLine("pcc_hair_mine.png", GuideLineKind.Code),
            new GuideLine("The game splits the file name on underscores to find the layer, "
                          + "so an id containing one is read as a different layer and the "
                          + "file never appears.", GuideLineKind.Trap),
            new GuideLine("Rows top to bottom are the facings: front, left, right, back.",
                GuideLineKind.Body),
            new GuideLine("Draw in greyscale around mid grey. The game multiplies the whole "
                          + "part by one colour to dye it, so anything already coloured "
                          + "fights the dye.", GuideLineKind.Body),
            new GuideLine("Layers draw back to front: hairbk, mantle, body, undie, boots, "
                          + "pants, cloth, chest, belt, glove, eye, hair, subhair, face, "
                          + "head, etc, mantlebk.", GuideLineKind.Body),
        });

        var textures = new GuideTopic
        {
            Title = "Which folder a replacement goes in",
            Summary = "Three different folders, three different ways of addressing an image.",
        };

        textures.Lines.AddRange(new[]
        {
            new GuideLine("Texture Replace  - a sprite by its slot in the packed atlas, "
                          + "named like objC_2115.png.", GuideLineKind.Bullet),
            new GuideLine("Portrait         - a portrait by the vanilla file's own name.",
                GuideLineKind.Bullet),
            new GuideLine("Actor\\PCC        - a character part, under female or male.",
                GuideLineKind.Bullet),
            new GuideLine("Texture          - a whole loose image mirroring _Elona\\Texture.",
                GuideLineKind.Bullet),
            new GuideLine("The originals for Texture Replace sprites live inside a Unity "
                          + "atlas rather than as loose files, which is why this application "
                          + "can show you the original of a portrait but not of one of those.",
                GuideLineKind.Body),
        });

        section.Topics.Add(portraits);
        section.Topics.Add(pcc);
        section.Topics.Add(textures);
        return section;
    }

    private static GuideSection Where()
    {
        var section = new GuideSection { Title = "Where the rest of it is" };

        var links = new GuideTopic
        {
            Title = "The community's own documentation",
            Summary = "This page covers what tends to fail quietly. Everything else - "
                      + "drama, zones, elements, scripting, every column of every sheet - "
                      + "is written up properly by the people who worked it out.",
        };

        links.Lines.AddRange(new[]
        {
            new GuideLine("The wiki is the place to start, and it is actively maintained.",
                GuideLineKind.Bullet),
            new GuideLine("The decompiled source answers the questions no documentation "
                          + "can - it is the game itself, and it is how the portrait and "
                          + "PCC rules above were established.", GuideLineKind.Bullet),
            new GuideLine("ModMaker is a browser editor for character, race and job sheets.",
                GuideLineKind.Bullet),
            new GuideLine("None of it is bundled into this application. Both repositories "
                          + "carry no licence, so their text is not ours to redistribute, "
                          + "and the decompile is the game's own code. Links rather than "
                          + "copies.", GuideLineKind.Body),
        });

        links.Links.AddRange(new[]
        {
            new GuideLink("Elin Modding Wiki", WikiUrl),
            new GuideLink("Decompiled source", DecompileUrl),
            new GuideLink("ModMaker", ModMakerUrl),
            new GuideLink("Elona Discord", DiscordUrl),
        });

        section.Topics.Add(links);
        return section;
    }
}
