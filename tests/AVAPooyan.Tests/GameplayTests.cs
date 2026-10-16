using AVAPooyan.Engine;

namespace AVAPooyan.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapooyan-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A round under way with no wolves coming unless put there; calm (none throws, no meat) unless <paramref name="calm"/> is false.</summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.SetToCome(0);
        return g;
    }

    private static readonly Controls Fire = new(0, true), Up = new(-1), Down = new(1);

    /// <summary>Steps for an arrow from the bow to reach a point across.</summary>
    private static int Flight(double x) => (int)Math.Ceiling((Field.BowX - x) / Arrow.Speed * 60) + 3;

    // ───────────────────────────── the pig ─────────────────────────────

    [Fact]
    public void The_lift_rides_up_and_down_its_shaft_and_no_further()
    {
        var g = Game();
        g.PutWolf(100, 300);
        Assert.Equal(Field.LiftTop, g.Player.Y);
        g.Run(30, Down);
        Assert.Equal(Field.LiftTop + GameEngine.LiftSpeed / 2, g.Player.Y, 1);
        g.Run(60 * 5, Down);
        Assert.Equal(Field.LiftBottom, g.Player.Y);
        g.Run(60 * 5, Up);
        Assert.Equal(Field.LiftTop, g.Player.Y);
    }

    [Fact]
    public void Arrows_fly_left_from_the_bow_two_at_a_time()
    {
        var g = Game();
        g.PutWolf(100, 600, vy: 0);
        g.Run(1, Fire);
        var a = Assert.Single(g.Arrows);
        Assert.Equal((Field.BowX - Arrow.Speed / 60, g.Player.Y + Field.BowDy), (a.X, a.Y));
        g.Run(10, default);
        Assert.True(a.X < Field.BowX - 100);
        g.Run(60, Fire);
        Assert.Equal(GameEngine.MaxArrows, g.Arrows.Count);
    }

    [Fact]
    public void An_arrow_pops_a_balloon_and_the_wolf_falls_but_the_wolf_itself_turns_an_arrow_aside()
    {
        var g = Game();
        double y = g.Player.Y + Field.BowDy;
        var w = g.PutWolf(400, y - Wolf.BalloonDy);
        g.Run(1, Fire);
        g.Run(Flight(400), default);
        Assert.Equal(WolfState.Falling, w.State);
        Assert.Equal(GameEngine.PopPoints, g.Score);
        g.Run(60 * 4, default);
        Assert.DoesNotContain(w, g.Wolves);

        g = Game();
        y = g.Player.Y + Field.BowDy;
        w = g.PutWolf(400, y);
        g.Run(1, Fire);
        g.Run(Flight(400), default);
        Assert.Equal(WolfState.Floating, w.State);
        Assert.Empty(g.Arrows);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void Balloons_going_up_are_worth_double()
    {
        var g = Game(wave: 2);
        var w = g.PutWolf(400, g.Player.Y + Field.BowDy - Wolf.BalloonDy);
        g.Run(1, Fire);
        g.Run(Flight(400), default);
        Assert.Equal(GameEngine.PopPoints * 2, g.Score);
    }

    [Fact]
    public void The_meat_flies_on_through_every_balloon_in_its_way_doubling_as_it_goes()
    {
        var g = Game();
        double y = g.Player.Y + Field.BowDy;
        for (int i = 0; i < 3; i++) g.PutWolf(200 + i * 120, y - Wolf.BalloonDy);
        g.GiveMeat();
        g.Run(1, Fire);
        Assert.NotNull(g.Thrown);
        g.Run(60 * 3, default);
        Assert.All(g.Wolves, w => Assert.NotEqual(WolfState.Floating, w.State));
        int p = GameEngine.PopPoints * 2;
        Assert.Equal(p + 2 * p + 4 * p, g.Score);
        Assert.False(g.MeatReady);
    }

    [Fact]
    public void The_meat_comes_round_every_so_often()
    {
        var g = Game(calm: false);
        g.PutWolf(100, 300, vy: 0);
        g.Run((int)(GameEngine.MeatFirst * 60) + 2, default);
        Assert.True(g.MeatReady);
    }

    // ───────────────────────────── the wolves ─────────────────────────────

    [Fact]
    public void Down_rounds_bring_wolves_off_the_clifftop_and_up_rounds_bring_them_off_the_ground()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.Run(60 * 3, default);
        Assert.All(g.Wolves, w => Assert.True(w.Vy > 0 && w.Y < 400));
        g.Go(2);
        g.Run(60 * 3, default);
        Assert.All(g.Wolves, w => Assert.True(w.Vy < 0 && w.Y > 400));
        Assert.True(g.WolfCount > 0);
    }

    [Fact]
    public void A_wolf_that_lands_runs_to_the_cliff_climbs_to_a_ledge_and_bites_the_pig_level_with_it()
    {
        var g = Game();
        g.PutWolf(100, 300, vy: 0);
        var w = g.PutWolf(300, Field.Ground - Wolf.Radius - 2, vy: 60);
        g.Run(60 * 15, default);
        Assert.Equal(WolfState.Ledge, w.State);
        Assert.Equal(Field.Ledges.Length - 1, w.Ledge);
        Assert.Equal(1, g.GotThrough);
        int lives = g.Lives;
        for (int i = 0; i < 60 * 4 && g.Phase == Phase.Play; i++) g.Tick(Down);
        Assert.Equal((Phase.Dying, "bitten", lives - 1), (g.Phase, g.Cause, g.Lives));
    }

    [Fact]
    public void With_every_ledge_taken_a_wolf_waits_harmless_at_the_foot()
    {
        var g = Game();
        for (int i = 0; i < Field.Ledges.Length; i++) g.PutWolf(Field.FaceX + 55, Field.Ledges[i] - Wolf.Radius, WolfState.Ledge, ledge: i);
        var w = g.PutWolf(Field.FaceX - 30, Field.Ground - Wolf.Radius, WolfState.Running);
        g.Run(60, default);
        Assert.Equal((WolfState.Ledge, -1), (w.State, w.Ledge));
    }

    [Fact]
    public void Five_wolves_at_the_top_roll_the_boulder_down_the_shaft()
    {
        var g = Game(wave: 2);
        for (int i = 0; i < GameEngine.TopToRoll; i++) g.PutWolf(100 + i * 60, Field.ClifftopY + 4, vy: -80);
        g.Run(30, default);
        Assert.Equal(GameEngine.TopToRoll, g.AtTop);
        Assert.NotNull(g.Boulder);
        int lives = g.Lives;
        for (int i = 0; i < 60 * 6 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal((Phase.Dying, "the boulder", lives - 1), (g.Phase, g.Cause, g.Lives));
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Null(g.Boulder);
        Assert.Equal(0, g.AtTop);
    }

    [Fact]
    public void Wolves_that_reach_the_top_while_the_pig_is_down_still_roll_the_next_boulder()
    {
        var g = Game(wave: 2);
        g.PutWolf(100, 500, vy: 0);
        for (int i = 0; i < GameEngine.TopToRoll; i++) g.PutWolf(100 + i * 60, Field.ClifftopY, WolfState.Top);
        for (int i = 0; i < 60 * 8 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        for (int i = 0; i < 60 * 3 && g.Boulder != null; i++) g.Tick(default);
        Assert.Null(g.Boulder);
        // Five more get up there before the pig is back.
        for (int i = 0; i < GameEngine.TopToRoll; i++) g.PutWolf(130 + i * 60, Field.ClifftopY, WolfState.Top);
        for (int i = 0; i < 60 * 4 && g.Boulder == null; i++) g.Tick(default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.NotNull(g.Boulder);
    }

    [Fact]
    public void Wolves_throw_rocks_at_the_lift_that_can_be_dodged_or_shot()
    {
        var g = Game(calm: false);
        var w = g.PutWolf(500, 400, vy: 0);
        w.ThrowIn = 0;
        g.Tick(default);
        var r = Assert.Single(g.Rocks);
        Assert.True(r.Vx > 0);
        // Left where it is, the pig is hit.
        int lives = g.Lives;
        for (int i = 0; i < 60 * 4 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal((Phase.Dying, "hit by a rock"), (g.Phase, g.Cause));
        // Shot down: points.
        g = Game();
        g.PutWolf(100, 600, vy: 0);
        g.PutRock(500, g.Player.Y + Field.BowDy, 0, 0);
        g.Rocks[0].Vy = 0;
        g.Run(1, Fire);
        for (int i = 0; i < 40 && g.Rocks.Count > 0; i++) { g.Rocks.ForEach(x => x.Vy = 0); g.Tick(default); }
        Assert.Empty(g.Rocks);
        Assert.Equal(GameEngine.RockPoints, g.Score);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void With_every_wolf_dealt_with_the_round_ends_with_a_bonus_and_more_if_none_got_through()
    {
        var g = Game();
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(GameEngine.ClearBonus + GameEngine.PerfectBonus, g.Bonus);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal((2, GameEngine.ClearBonus + GameEngine.PerfectBonus), (g.Wave, g.Score));
        Assert.True(g.Rising);
    }

    [Fact]
    public void Later_rounds_bring_more_wolves_quicker_and_faster()
    {
        var a = Game(wave: 1);
        var b = Game(wave: 7);
        Assert.True(b.WolfCount > a.WolfCount);
        Assert.True(b.SpawnGap < a.SpawnGap);
        Assert.True(b.FloatSpeed > a.FloatSpeed);
        Assert.True(b.ThrowGap < a.ThrowGap);
    }

    [Fact]
    public void Every_thirty_thousand_is_an_extra_pig()
    {
        var g = Game();
        g.SetScore(GameEngine.ExtraLifeEvery - 20);
        int lives = g.Lives;
        var w = g.PutWolf(400, g.Player.Y + Field.BowDy - Wolf.BalloonDy);
        g.PutWolf(100, 600, vy: 0);
        g.Run(1, Fire);
        g.Run(Flight(400), default);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_pig_lost_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutWolf(100, 300, vy: 0);
        g.PutWolf(Field.FaceX + 55, Field.Ledges[0] - Wolf.Radius, WolfState.Ledge, ledge: 0);
        g.Run(60 * 2, Down);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "PIG") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("PIG", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_round()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([1, 2], waves);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0, Phase.Start), (g.Wave, g.Lives, g.Score, g.Phase));
        Assert.Equal(g.WolfCount, g.ToCome);
    }

    [Fact]
    public void The_up_key_raises_the_lift_the_down_key_lowers_it_and_space_shoots()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Avalonia.Input.Key.Down);
        input.OnKeyDown(Avalonia.Input.Key.Space);
        Assert.Equal(new Controls(1, true), input.Controls);
        input.OnKeyUp(Avalonia.Input.Key.Down);
        input.OnKeyUp(Avalonia.Input.Key.Space);
        input.OnKeyDown(Avalonia.Input.Key.Up);
        Assert.Equal(new Controls(-1, false), input.Controls);
    }

    [Fact]
    public void The_computer_player_gets_through_the_first_two_rounds()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            for (int i = 0; i < 60 * 120 && g.Wave < 3 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Wave >= 3, $"seed {seed}: round {g.Wave}");
        }
    }

    [Fact]
    public void Riding_up_and_down_firing_all_the_time_soon_loses()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int t = 0; t < 60 * 300 && g.State == GameState.Playing; t++) g.Tick(new Controls((t / 100) % 2 == 0 ? 1 : -1, true));
        Assert.NotEqual(GameState.Playing, g.State);
    }
}
