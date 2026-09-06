using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The colour maths behind the dye wheel.
///
/// Worth pinning down because the wheel reads a colour out and writes one back on every
/// interaction: a conversion that drifts would slowly change a character while the user
/// only looks at it.
/// </summary>
public sealed class PccColourTests
{
    [Theory]
    [InlineData("92847B")]
    [InlineData("7C8FAE")]
    [InlineData("6F6E64")]
    [InlineData("9E824D")]
    [InlineData("FFFFFF")]
    [InlineData("000000")]
    public void A_colour_survives_the_trip_to_the_wheel_and_back(string hex)
    {
        var (r, g, b) = PccColour.FromHex(hex)!.Value;

        var (hue, saturation, value) = PccColour.ToHsv(r, g, b);
        var (r2, g2, b2) = PccColour.FromHsv(hue, saturation, value);

        // Off-by-one from rounding is fine; drift is not.
        Assert.InRange(r2, r - 1, r + 1);
        Assert.InRange(g2, g - 1, g + 1);
        Assert.InRange(b2, b - 1, b + 1);
    }

    [Theory]
    [InlineData(0, "FF0000")]
    [InlineData(120, "00FF00")]
    [InlineData(240, "0000FF")]
    [InlineData(60, "FFFF00")]
    public void The_wheel_puts_the_primaries_where_they_belong(double hue, string expected)
    {
        var (r, g, b) = PccColour.FromHsv(hue, 1, 1);

        Assert.Equal(expected, PccColour.ToHex(r, g, b));
    }

    [Fact]
    public void Hue_wraps_rather_than_clamping()
    {
        // Dragging round the wheel passes 360 constantly, and stopping dead at red
        // would feel broken.
        Assert.Equal(PccColour.FromHsv(10, 1, 1), PccColour.FromHsv(370, 1, 1));
        Assert.Equal(PccColour.FromHsv(350, 1, 1), PccColour.FromHsv(-10, 1, 1));
    }

    [Fact]
    public void Saturation_and_value_are_clamped_rather_than_wrapped()
    {
        // Dragging past the rim must stay at full saturation, not flip to none.
        Assert.Equal(PccColour.FromHsv(0, 1, 1), PccColour.FromHsv(0, 5, 5));
        Assert.Equal(PccColour.FromHsv(0, 0, 0), PccColour.FromHsv(0, -3, -3));
    }

    [Fact]
    public void Grey_reports_no_hue_so_the_marker_stays_still()
    {
        // Otherwise the marker jumps around the wheel while only brightness is moving.
        Assert.Equal(0, PccColour.ToHsv(128, 128, 128).Hue);
        Assert.Equal(0, PccColour.ToHsv(128, 128, 128).Saturation);
    }

    [Fact]
    public void A_hex_string_is_read_with_or_without_its_hash_and_refused_when_it_is_not_one()
    {
        Assert.Equal((byte)0x92, PccColour.FromHex("92847B")!.Value.R);
        Assert.Equal((byte)0x92, PccColour.FromHex("#92847B")!.Value.R);
        Assert.Equal((byte)0x92, PccColour.FromHex(" 92847B ")!.Value.R);

        Assert.Null(PccColour.FromHex(null));
        Assert.Null(PccColour.FromHex("92847"));
        Assert.Null(PccColour.FromHex("nothex"));
    }

    [Fact]
    public void A_random_colour_looks_like_one_of_the_games_own()
    {
        // Random RGB comes out neon and looks nothing like Elin. These ranges are
        // measured from the palette the game itself uses.
        var random = new Random(1);

        for (var i = 0; i < 500; i++)
        {
            var (r, g, b) = PccColour.FromHex(PccColour.RandomHex(random))!.Value;
            var (_, saturation, value) = PccColour.ToHsv(r, g, b);

            Assert.InRange(saturation, 0, 0.58);
            Assert.InRange(value, 0.34, 0.81);
        }
    }

    [Fact]
    public void Random_colours_are_not_all_the_same_colour()
    {
        var random = new Random(7);
        var seen = Enumerable.Range(0, 50).Select(_ => PccColour.RandomHex(random)).Distinct();

        Assert.True(seen.Count() > 40);
    }
}
