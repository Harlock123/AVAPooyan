using AVAPooyan.Engine;

namespace AVAPooyan.Tests;

/// <summary>How the computer player gets on, round by round (set POOYAN_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that rides the lift up and down firing all the time).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("POOYAN_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "pooyan-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 10 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? Wander(t) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) levels.Add($"R{wave}:{(t - start) / 60:0}s {g.GotThrough}");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Wave} after {t / 60:0}s score {g.Score} (now {g.ToCome} to come, {g.GotThrough} through) | {string.Join(" ", levels)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }

    /// <summary>Up and down the shaft over and over, firing all the time.</summary>
    private static Controls Wander(int t) => new((t / 100) % 2 == 0 ? 1 : -1, true);
}
