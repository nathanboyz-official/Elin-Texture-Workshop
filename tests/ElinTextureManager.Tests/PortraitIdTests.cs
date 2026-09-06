using ElinTextureManager.Core.Portraits;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The file name Elin reads a portrait's group and gender out of.
///
/// The rules are not a convention anyone here chose, so they are checked against the
/// game's own parser rather than against taste. Portrait.ListPortraits does:
///
///     string[] array = item.id.Split('-')[0].Split('_');
///     if (array[0] == cat) {
///         int num = array.Length > 1 ? (array[1] == "m" ? 2 : array[1] == "f" ? 1 : 0) : 0;
///         if (num == 0 || gender == 0 || gender == num) list.Add(item);
///     }
///
/// The test below runs that logic verbatim over the names this application produces. A
/// name the game would not list is a file that loads and is then never shown by
/// anything, with nothing anywhere reporting a problem.
/// </summary>
public sealed class PortraitIdTests
{
    /// <summary>Elin's own parser, transcribed. The thing being tested against.</summary>
    private static (string Group, int Gender) AsTheGameReadsIt(string id)
    {
        var array = id.Split('-')[0].Split('_');

        var gender = array.Length > 1
            ? (array[1] == "m" ? 2 : array[1] == "f" ? 1 : 0)
            : 0;

        return (array[0], gender);
    }

    /// <summary>The game's own filter: would this portrait be offered here?</summary>
    private static bool WouldBeListed(string id, string cat, int gender)
    {
        var (group, num) = AsTheGameReadsIt(id);
        if (group != cat) return false;

        return num == 0 || gender == 0 || gender == num;
    }

    [Theory]
    [InlineData("c", "m", "agnes", "c_m_agnes.png")]
    [InlineData("guard", "f", "vera", "guard_f_vera.png")]
    [InlineData("special", "n", "custom1", "special_n_custom1.png")]
    [InlineData("foxfolk", "n", "kitsune", "foxfolk_n_kitsune.png")]
    public void A_portrait_is_named_the_way_the_game_reads_one(
        string group, string gender, string name, string expected)
    {
        Assert.Equal(expected, PortraitId.FileName(group, gender, name));
    }

    [Fact]
    public void The_shape_matches_the_example_the_game_ships()
    {
        // Elin\Custom\Portrait\special_n_custom1.png, straight out of the install.
        Assert.Equal("special_n_custom1", PortraitId.Build("special", "n", "custom1"));
    }

    [Theory]
    [InlineData("c", "m", 2)]
    [InlineData("c", "f", 1)]
    [InlineData("guard", "m", 2)]
    [InlineData("special", "n", 0)]
    [InlineData("foxfolk", "n", 0)]
    public void The_game_reads_back_what_was_written(string group, string gender, int expected)
    {
        var id = PortraitId.Build(group, gender, "someone");
        var (readGroup, readGender) = AsTheGameReadsIt(id);

        Assert.Equal(group, readGroup);
        Assert.Equal(expected, readGender);
    }

    [Fact]
    public void Every_name_this_makes_is_one_the_game_would_actually_list()
    {
        foreach (var group in PortraitId.Groups)
        {
            foreach (var gender in PortraitId.Genders)
            {
                var id = PortraitId.Build(group.Code, gender.Code, "someone");

                var asked = gender.Code switch { "m" => 2, "f" => 1, _ => 0 };

                Assert.True(WouldBeListed(id, group.Code, asked),
                    $"{id} would not be listed under {group.Code} for gender {asked}");

                // A portrait with no gender goes to everyone, which is the point of it.
                if (gender.Code == PortraitId.AnyGender)
                {
                    Assert.True(WouldBeListed(id, group.Code, 1), $"{id} not offered to females");
                    Assert.True(WouldBeListed(id, group.Code, 2), $"{id} not offered to males");
                }
            }
        }
    }

    [Fact]
    public void A_bare_name_is_the_reason_a_portrait_never_shows_up()
    {
        // What the button used to produce, and what the user actually had sitting in the
        // folder doing nothing.
        Assert.False(WouldBeListed("afta", "c", 0));
        Assert.False(WouldBeListed("afta", "guard", 0));
        Assert.False(WouldBeListed("afta", "special", 0));
        Assert.False(WouldBeListed("afta", "foxfolk", 0));

        // And what it produces now.
        Assert.True(WouldBeListed(PortraitId.Build("c", "n", "afta"), "c", 0));
    }

    [Fact]
    public void Underscores_and_hyphens_in_the_chosen_name_do_not_change_the_group()
    {
        // Everything the game reads sits before the first hyphen, and only the first two
        // underscore-separated pieces are looked at, so the name itself is free.
        var id = PortraitId.Build("c", "f", "my_cat-in-a-hat");
        var (group, gender) = AsTheGameReadsIt(id);

        Assert.Equal("c", group);
        Assert.Equal(1, gender);
    }

    [Fact]
    public void An_id_already_in_the_folder_is_never_written_over()
    {
        var taken = new HashSet<string> { "c_f_agnes" };

        Assert.Equal("c_f_agnes2", PortraitId.Available("c", "f", "agnes", taken.Contains));
    }

    [Fact]
    public void A_free_id_is_left_alone()
    {
        Assert.Equal("c_f_agnes", PortraitId.Available("c", "f", "agnes", _ => false));
    }

    [Fact]
    public void The_groups_offered_are_the_ones_the_game_asks_for()
    {
        // Portrait.ListPlayerPortraits asks for exactly these, in this order.
        Assert.Equal(
            new[] { "c", "guard", "special", "foxfolk" },
            PortraitId.Groups.Select(g => g.Code).ToArray());
    }

    [Fact]
    public void Parsing_a_name_says_whether_the_game_will_ever_show_it()
    {
        Assert.True(PortraitId.Parse("c_f_agnes").Listed);
        Assert.False(PortraitId.Parse("afta").Listed);
    }

    [Fact]
    public void Parsing_gives_back_the_pieces_that_were_put_in()
    {
        var parts = PortraitId.Parse("guard_m_vera");

        Assert.Equal("guard", parts.Group);
        Assert.Equal("m", parts.Gender);
        Assert.Equal("vera", parts.Name);
    }

    [Fact]
    public void A_gender_letter_the_game_does_not_know_reads_as_anyone()
    {
        Assert.Equal(PortraitId.AnyGender, PortraitId.Parse("special_n_custom1").Gender);
        Assert.Equal(0, AsTheGameReadsIt("special_n_custom1").Gender);
    }

    [Fact]
    public void The_reserved_endings_are_the_ones_the_game_looks_up_as_extra_layers()
    {
        // SetPortrait fetches id + "-overlay" and id + "-full".
        Assert.Contains("-overlay", PortraitName.Reserved);
        Assert.Contains("-full", PortraitName.Reserved);

        Assert.False(PortraitName.Check("agnes-full").Ok);
        Assert.False(PortraitName.Check("agnes-overlay").Ok);
    }
}
