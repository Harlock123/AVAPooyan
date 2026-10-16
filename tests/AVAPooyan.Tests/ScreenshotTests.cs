using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPooyan.Engine;
using AVAPooyan.Views;
using Controls = AVAPooyan.Engine.Controls;

namespace AVAPooyan.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set POOYAN_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapooyan-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("POOYAN_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPooyan-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("PIG", 72450, 9);
        g.HighScores.Insert("AVA", 41800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A round, and the computer at work in it until something worth showing.</summary>
    private static void Play(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Shooting(GameEngine g) => g.Phase == Phase.Play && g.Player.Alive;

    private static int Floating(GameEngine g) => g.Wolves.Count(w => w.State == WolfState.Floating);

    [AvaloniaFact]
    public void Coming_down() => Play(1, "down", g => Shooting(g) && !g.Rising && Floating(g) >= 4 && g.Arrows.Count >= 1 && g.Effects.Any(e => e.Kind == EffectKind.Pop));

    [AvaloniaFact]
    public void Going_up() => Play(2, "up", g => Shooting(g) && g.Rising && Floating(g) >= 4 && g.AtTop >= 1 && g.Arrows.Count >= 1, seconds: 300);

    [AvaloniaFact]
    public void A_rock() => Play(3, "rock", g => Shooting(g) && g.Rocks.Any(r => r.X > 500) && Floating(g) >= 4, seconds: 300);

    [AvaloniaFact]
    public void The_meat() => Play(5, "meat", g => Shooting(g) && g.Thrown is { } m && m.X is > 300 and < 700 && Floating(g) >= 3, seconds: 300);

    [AvaloniaFact]
    public void Wolves_on_the_ledges() => Play(7, "ledges", g => Shooting(g) && g.Wolves.Count(w => w.State == WolfState.Ledge && w.Ledge >= 0) >= 2 && Floating(g) >= 4, seconds: 400);

    [AvaloniaFact]
    public void Bitten()
    {
        var (w, c, g) = Open(3);
        g.Go(1);
        g.PutWolf(Field.FaceX + 55, Field.Ledges[1] - Wolf.Radius, WolfState.Ledge, ledge: 1);
        for (int i = 0; i < 60 * 5 && g.Phase == Phase.Play; i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 20; i++) g.Tick(default);
        c.Advance(0.3);
        Shot(w, c, "bitten");
    }
}
