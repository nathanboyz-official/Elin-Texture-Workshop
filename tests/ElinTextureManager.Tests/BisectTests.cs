using ElinTextureManager.Core.Bisect;
using Xunit;

namespace ElinTextureManager.Tests;

/// <summary>
/// The halving search that finds which mod broke the game.
///
/// Elin's patch notes hand the player this job after every stable update and leave them
/// to do it by hand. The value is entirely in getting the answer right: an app that
/// confidently names the wrong mod is worse than no app, because that is exactly the
/// mistake the game's own crash dialog already makes.
/// </summary>
public sealed class BisectTests
{
    /// <summary>Starts a search and answers the control round as "a mod is doing this".</summary>
    private static BisectSession Start(params string[] suspects)
    {
        var session = new BisectSession(suspects, Array.Empty<string>());
        session.Begin();
        if (session.IsControl) session.Answer(BisectAnswer.Fixed);
        return session;
    }

    /// <summary>
    /// Plays a whole search through, answering as the game would if
    /// <paramref name="guilty"/> were the one mod at fault.
    /// </summary>
    private static BisectSession RunToEnd(string guilty, params string[] suspects)
    {
        var session = Start(suspects);
        var rounds = 0;

        while (session.IsRunning && rounds++ < 50)
        {
            var broken = session.TrialGroup.Contains(guilty);
            session.Answer(broken ? BisectAnswer.StillBroken : BisectAnswer.Fixed);
        }

        return session;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(338)]
    public void It_finds_the_guilty_mod_wherever_it_sits(int count)
    {
        var mods = Enumerable.Range(0, count).Select(i => $"mod{i}").ToArray();

        // Every position, not a sampled few: an off-by-one in the halving would show up
        // only at an edge.
        foreach (var guilty in mods)
        {
            var session = RunToEnd(guilty, mods);

            Assert.Equal(BisectOutcome.Found, session.Outcome);
            Assert.Equal(guilty, session.Culprit);
        }
    }

    [Fact]
    public void It_gets_there_in_about_log2_launches()
    {
        var mods = Enumerable.Range(0, 338).Select(i => $"mod{i}").ToArray();
        var session = Start(mods);
        var rounds = 0;

        while (session.IsRunning && rounds++ < 100)
            session.Answer(session.TrialGroup.Contains("mod200")
                ? BisectAnswer.StillBroken
                : BisectAnswer.Fixed);

        // 338 mods by hand is 338 launches. This is the entire point of the feature.
        Assert.Equal(BisectOutcome.Found, session.Outcome);
        Assert.InRange(rounds, 1, 12);
    }

    [Fact]
    public void The_last_round_runs_the_accused_mod_on_its_own()
    {
        var session = RunToEnd("b", "a", "b");

        // Arriving at a mod by elimination is not proof it did anything. It has to be
        // seen doing it alone before this names it.
        Assert.Equal(BisectOutcome.Found, session.Outcome);
        Assert.Equal("b", session.Culprit);
    }

    [Fact]
    public void A_mod_that_behaves_when_left_alone_is_not_named()
    {
        var session = Start("a", "b");

        session.Answer(BisectAnswer.Fixed);       // not in the first half, so suspect b
        Assert.True(session.IsVerifying);
        Assert.Equal(new[] { "b" }, session.TrialGroup);

        session.Answer(BisectAnswer.Fixed);       // but b alone is fine

        Assert.Equal(BisectOutcome.NotReproducible, session.Outcome);
        Assert.Null(session.Culprit);
    }

    [Fact]
    public void Pinned_mods_are_never_suspects_and_never_turned_off()
    {
        var session = new BisectSession(new[] { "a", "b" }, new[] { "cwl", "framework" });
        session.Begin();

        Assert.DoesNotContain("cwl", session.Suspects);
        Assert.DoesNotContain("cwl", session.TrialGroup);
        Assert.DoesNotContain("cwl", session.RestingGroup);
        Assert.Contains("cwl", session.PinnedOn);
    }

    [Fact]
    public void Every_suspect_is_either_on_or_off_each_round_and_never_both()
    {
        var session = Start("a", "b", "c", "d", "e");

        var trial = session.TrialGroup;
        var resting = session.RestingGroup;

        // A mod in neither list would silently keep whatever state it had, which would
        // make the round's answer mean nothing.
        Assert.Empty(trial.Intersect(resting));
        Assert.Equal(session.Suspects.OrderBy(s => s),
            trial.Concat(resting).OrderBy(s => s));
    }

    [Fact]
    public void Clearing_a_half_never_loses_or_duplicates_a_mod()
    {
        var mods = Enumerable.Range(0, 40).Select(i => $"mod{i}").ToArray();
        var session = Start(mods);

        while (session.IsRunning)
        {
            session.Answer(session.TrialGroup.Contains("mod7")
                ? BisectAnswer.StillBroken
                : BisectAnswer.Fixed);

            var accounted = session.Suspects.Concat(session.Cleared).ToList();
            Assert.Equal(mods.Length, accounted.Count);
            Assert.Equal(mods.Length, accounted.Distinct().Count());
        }
    }

    [Fact]
    public void A_problem_that_survives_every_candidate_being_off_is_not_a_mod()
    {
        var session = new BisectSession(new[] { "a", "b" }, new[] { "cwl" });
        session.Begin();

        // The control round: nothing but the pinned mods running, and it still happens.
        Assert.True(session.IsControl);
        Assert.Empty(session.TrialGroup);

        session.Answer(BisectAnswer.StillBroken);

        Assert.Equal(BisectOutcome.NotAMod, session.Outcome);
        Assert.Null(session.Culprit);
    }

    [Fact]
    public void The_control_round_is_what_stops_an_innocent_mod_being_named()
    {
        // A problem that happens no matter what - a broken save, the game itself, a
        // pinned framework. Without the control round every round answers "still
        // broken", the search converges on whatever was left, and the verify round
        // confirms it. That is the game's own crash dialog mistake, rebuilt.
        var session = new BisectSession(new[] { "a", "b", "c", "d" }, Array.Empty<string>());
        session.Begin();

        var rounds = 0;
        while (session.IsRunning && rounds++ < 20) session.Answer(BisectAnswer.StillBroken);

        Assert.Equal(BisectOutcome.NotAMod, session.Outcome);
        Assert.Null(session.Culprit);
        Assert.Equal(1, rounds);
    }

    [Fact]
    public void An_empty_candidate_list_ends_immediately_rather_than_looping()
    {
        var session = new BisectSession(Array.Empty<string>(), new[] { "cwl" });
        session.Begin();

        Assert.False(session.IsRunning);
        Assert.Equal(BisectOutcome.NotAMod, session.Outcome);
    }

    [Fact]
    public void A_single_candidate_still_has_to_prove_itself()
    {
        var session = Start("only");

        Assert.True(session.IsVerifying);
        Assert.Equal(new[] { "only" }, session.TrialGroup);

        session.Answer(BisectAnswer.StillBroken);
        Assert.Equal("only", session.Culprit);
    }

    [Fact]
    public void Cancelling_stops_the_search_and_names_nobody()
    {
        var session = Start("a", "b", "c");
        session.Cancel();

        Assert.False(session.IsRunning);
        Assert.Equal(BisectOutcome.Cancelled, session.Outcome);
        Assert.Null(session.Culprit);
    }

    [Fact]
    public void Answers_after_the_search_has_ended_change_nothing()
    {
        var session = RunToEnd("a", "a", "b");
        var culprit = session.Culprit;

        session.Answer(BisectAnswer.Fixed);

        Assert.Equal(culprit, session.Culprit);
        Assert.Equal(BisectOutcome.Found, session.Outcome);
    }

    [Fact]
    public void An_interrupted_search_can_be_put_back_from_what_was_written_to_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "bisect.json");

        var session = Start("a", "b", "c", "d");
        var original = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = true, ["b"] = true, ["c"] = false, ["d"] = true, ["untouched"] = true,
        };

        BisectState.Save(path, BisectState.Capture(session, original));

        // What the next launch sees after the window was closed mid-search.
        var recovered = BisectState.Load(path);

        Assert.NotNull(recovered);
        Assert.True(recovered!.NeedsRestore);
        Assert.Equal(original, recovered.OriginalStates);

        // Including a mod that was already off: restoring must not turn it back on.
        Assert.False(recovered.OriginalStates["c"]);
    }

    [Fact]
    public void A_finished_search_needs_no_restoring()
    {
        var session = RunToEnd("a", "a", "b");
        var state = BisectState.Capture(session, new Dictionary<string, bool> { ["a"] = true });

        Assert.Equal(BisectOutcome.Found, state.Outcome);
        Assert.False(state.NeedsRestore);
    }

    [Fact]
    public void A_saved_search_resumes_at_the_round_it_was_on()
    {
        var session = Start("a", "b", "c", "d");
        session.Answer(BisectAnswer.StillBroken);

        var state = BisectState.Capture(session, new Dictionary<string, bool> { ["a"] = true });
        var resumed = state.ToSession();

        Assert.Equal(session.Round, resumed.Round);
        Assert.Equal(session.Suspects, resumed.Suspects);
        Assert.Equal(session.Cleared, resumed.Cleared);
        Assert.Equal(session.IsVerifying, resumed.IsVerifying);
        Assert.True(resumed.IsRunning);
    }

    [Fact]
    public void A_session_file_with_no_original_states_is_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "bisect.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // It could not restore anything, and acting on it would be worse than ignoring it.
        File.WriteAllText(path, """{"FormatVersion":1,"Suspects":["a"],"OriginalStates":{}}""");

        Assert.Null(BisectState.Load(path));
    }

    [Fact]
    public void A_corrupt_session_file_is_ignored_rather_than_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), "etm_tests",
            Guid.NewGuid().ToString("N")[..10], "bisect.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(BisectState.Load(path));
    }
}
