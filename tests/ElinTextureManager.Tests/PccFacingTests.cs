using ElinTextureManager.Core.Pcc;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// Turning the character.
///
/// The sheet stores its rows front, left, right, back - the order they sit in the file,
/// not the order you meet them walking round someone. Stepping through the rows makes a
/// character flip from one profile straight to the other, half a turn in one click.
/// </summary>
public sealed class PccFacingTests
{
    [Fact]
    public void Turning_one_way_walks_round_the_character_rather_than_through_the_file()
    {
        var facing = PccFacing.Front;

        facing = PccFacing.Turn(facing, 1);
        Assert.Equal(PccFacing.Left, facing);

        // The one that matters: from a profile it must go to the back, not straight
        // across to the other profile.
        facing = PccFacing.Turn(facing, 1);
        Assert.Equal(PccFacing.Back, facing);

        facing = PccFacing.Turn(facing, 1);
        Assert.Equal(PccFacing.Right, facing);

        facing = PccFacing.Turn(facing, 1);
        Assert.Equal(PccFacing.Front, facing);
    }

    [Fact]
    public void Turning_the_other_way_is_the_same_circle_backwards()
    {
        var facing = PccFacing.Front;

        facing = PccFacing.Turn(facing, -1);
        Assert.Equal(PccFacing.Right, facing);

        facing = PccFacing.Turn(facing, -1);
        Assert.Equal(PccFacing.Back, facing);

        facing = PccFacing.Turn(facing, -1);
        Assert.Equal(PccFacing.Left, facing);

        facing = PccFacing.Turn(facing, -1);
        Assert.Equal(PccFacing.Front, facing);
    }

    [Theory]
    [InlineData(PccFacing.Front)]
    [InlineData(PccFacing.Left)]
    [InlineData(PccFacing.Right)]
    [InlineData(PccFacing.Back)]
    public void Four_turns_from_anywhere_comes_back_to_where_it_started(int start)
    {
        var facing = start;
        for (var i = 0; i < 4; i++) facing = PccFacing.Turn(facing, 1);

        Assert.Equal(start, facing);
    }

    [Fact]
    public void One_turn_each_way_cancels_out()
    {
        foreach (var start in PccFacing.Circle)
            Assert.Equal(start, PccFacing.Turn(PccFacing.Turn(start, 1), -1));
    }

    [Fact]
    public void Two_turns_is_always_the_opposite_side()
    {
        Assert.Equal(PccFacing.Back, PccFacing.Turn(PccFacing.Front, 2));
        Assert.Equal(PccFacing.Front, PccFacing.Turn(PccFacing.Back, 2));
        Assert.Equal(PccFacing.Right, PccFacing.Turn(PccFacing.Left, 2));
        Assert.Equal(PccFacing.Left, PccFacing.Turn(PccFacing.Right, 2));
    }

    [Fact]
    public void The_circle_holds_every_row_exactly_once()
    {
        // A row left out would be a facing the arrows could never reach.
        Assert.Equal(4, PccFacing.Circle.Count);
        Assert.Equal(4, PccFacing.Circle.Distinct().Count());
        Assert.Equal(new[] { 0, 1, 2, 3 }, PccFacing.Circle.OrderBy(f => f));
    }

    [Fact]
    public void A_facing_from_nowhere_is_treated_as_the_front_rather_than_throwing()
    {
        Assert.Equal(PccFacing.Left, PccFacing.Turn(99, 1));
    }
}
