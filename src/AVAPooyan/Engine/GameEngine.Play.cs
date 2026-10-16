namespace AVAPooyan.Engine;

public sealed partial class GameEngine
{
    public const double LiftSpeed = 290, ReloadSeconds = 0.32, Gravity = 420, RockSpeed = 300, RunSpeed = 120, ClimbSpeed = 90;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, ClearSeconds = 3.2, MeatEvery = 15, MeatFirst = 9, BiteReach = 34;
    public const int MaxArrows = 2, MaxRocks = 3, TopToRoll = 5;
    public const int PopPoints = 50, RockPoints = 50, ClearBonus = 1000, PerfectBonus = 2000, ExtraLifeEvery = 30000;

    /// <summary>The round: 1 for the first. Odd rounds the wolves float down; even rounds they float up.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Pig Player { get; private set; } = new();
    public List<Wolf> Wolves { get; } = [];
    public List<Arrow> Arrows { get; } = [];
    public List<Rock> Rocks { get; } = [];
    public Meat? Thrown { get; private set; }
    public Boulder? Boulder { get; private set; }
    /// <summary>Meat to hand: the next shot throws it instead of an arrow.</summary>
    public bool MeatReady { get; private set; }
    /// <summary>Wolves still to come this round.</summary>
    public int ToCome { get; private set; }
    /// <summary>How many made it this round: to the ground (down rounds), or to the top (up rounds, waiting to roll the boulder).</summary>
    public int GotThrough { get; private set; }
    public int AtTop => Wolves.Count(w => w.State == WolfState.Top);
    public bool Rising => Wave % 2 == 0;
    public PigPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>For tests: no wolf comes unless put there, none throws, and the meat never comes.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _spawnIn, _meatIn;

    public int WolfCount => Math.Min(48, 20 + 3 * Wave);
    public double SpawnGap => Math.Max(0.45, 1.15 - 0.07 * (Wave - 1));
    public double FloatSpeed => Math.Min(120, 55 + 5 * (Wave - 1));
    public double ThrowGap => Math.Max(3.5, 7.5 - 0.4 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Wolves.Clear();
        Ready(1);
        SetPhase(Phase.Start);
    }

    /// <summary>A round set up: the wolves all to come, the ledges empty, the meat a while off.</summary>
    private void Ready(int wave)
    {
        Wolves.Clear();
        ToCome = WolfCount;
        GotThrough = 0;
        MeatReady = false;
        _meatIn = MeatFirst;
        Restart();
    }

    /// <summary>The pig back at the top of the lift, the air clear; the wolves on the ledges and the clifftop stay.</summary>
    private void Restart()
    {
        Player = new Pig();
        Arrows.Clear();
        Rocks.Clear();
        Thrown = null;
        Boulder = null;
        Wolves.RemoveAll(w => w.State is WolfState.Floating or WolfState.Falling);
        _spawnIn = 1.2;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Ready(wave);
        SetPhase(Phase.Start);
        Say($"ROUND {wave}", Rising ? "THEY'RE COMING UP - DON'T LET FIVE REACH THE TOP" : "THEY'RE COMING DOWN - POP THEIR BALLOONS", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                Lift(dt, c);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Lift(dt, c);
                if (!Calm) Spawn(dt);
                MoveWolves(dt);
                if (Phase != Phase.Play) break;
                MoveShots(dt);
                if (Phase != Phase.Play) break;
                Dangers();
                if (Phase == Phase.Play && ToCome == 0 && !Wolves.Any(w => w.State is WolfState.Floating or WolfState.Falling or WolfState.Running or WolfState.Climbing) && Boulder == null)
                    Finish();
                break;
            case Phase.Dying:
                MoveWolves(dt);
                if (Boulder != null) RollBoulder(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Restart();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0) { Score += Bonus; CheckExtra(); }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the pig ─────────────────────────────

    /// <summary>The lift up and down its shaft.</summary>
    public static void Ride(Pig p, Controls c, double dt) =>
        p.Y = Math.Clamp(p.Y + c.Dy * LiftSpeed * dt, Field.LiftTop, Field.LiftBottom);

    private void Lift(double dt, Controls c)
    {
        var p = Player;
        Ride(p, c, dt);
        p.ReloadIn -= dt;
        p.Draw = Math.Max(0, p.Draw - dt);
        if (Phase != Phase.Play) return;
        if (!Calm && !MeatReady && (_meatIn -= dt) <= 0) { MeatReady = true; Play(Sfx.MeatReady); }
        if (!c.Fire || p.ReloadIn > 0) return;
        if (MeatReady && Thrown == null)
        {
            MeatReady = false;
            _meatIn = MeatEvery;
            Thrown = new Meat { X = Field.BowX, Y = p.Y + Field.BowDy };
            p.ReloadIn = ReloadSeconds;
            Play(Sfx.Meat);
        }
        else if (Arrows.Count < MaxArrows)
        {
            Arrows.Add(new Arrow { X = Field.BowX, Y = p.Y + Field.BowDy });
            p.ReloadIn = ReloadSeconds;
            p.Draw = 0.15;
            Play(Sfx.Shoot);
        }
    }

    // ───────────────────────────── the wolves ─────────────────────────────

    /// <summary>The next wolf: off the clifftop with a balloon (down rounds), or up off the ground (up rounds).</summary>
    private void Spawn(double dt)
    {
        if (ToCome <= 0 || (_spawnIn -= dt) > 0) return;
        _spawnIn = SpawnGap * (0.75 + _rng.NextDouble() * 0.5);
        // Now and then a few together, more often later on.
        int pack = _rng.NextDouble() < Math.Min(0.45, 0.08 * Wave) ? 2 + _rng.Next(2) : 1;
        for (int i = 0; i < pack && ToCome > 0; i++) Launch();
    }

    private void Launch()
    {
        ToCome--;
        double x = 70 + _rng.NextDouble() * 460;
        Wolves.Add(new Wolf
        {
            X = x, Y = Rising ? Field.Ground - Wolf.Radius : Field.ClifftopY + 20,
            Vy = (Rising ? -1 : 1) * FloatSpeed * (0.8 + _rng.NextDouble() * 0.4),
            Vx = 4 + _rng.NextDouble() * 12, T = _rng.NextDouble() * 6, Balloon = _rng.Next(4),
            ThrowIn = ThrowGap * (0.5 + _rng.NextDouble()),
        });
    }

    private void MoveWolves(double dt)
    {
        var p = Player;
        foreach (var w in Wolves.Where(w => !w.Gone))
        {
            w.T += dt;
            switch (w.State)
            {
                case WolfState.Floating:
                    // A gentle sway, drifting a little towards the lift.
                    w.X = Math.Clamp(w.X + (w.Vx + Math.Sin(w.T * 1.3) * 22) * dt, 40, Field.FaceX - 90);
                    w.Y += w.Vy * dt;
                    if (!Calm && Phase == Phase.Play && (w.ThrowIn -= dt) <= 0)
                    {
                        w.ThrowIn = ThrowGap * (0.7 + _rng.NextDouble() * 0.6);
                        if (w.X > 200 && w.Y is > 180 and < 640 && Rocks.Count < MaxRocks && p.Alive) Throw(w);
                    }
                    if (!Rising && w.Y >= Field.Ground - Wolf.Radius)
                    {
                        w.Y = Field.Ground - Wolf.Radius;
                        w.State = WolfState.Running;
                        GotThrough++;
                        Play(Sfx.Land);
                    }
                    else if (Rising && w.Y <= Field.ClifftopY - Wolf.Radius)
                    {
                        w.Y = Field.ClifftopY - Wolf.Radius;
                        w.State = WolfState.Top;
                        w.X = Math.Min(w.X, Field.ClifftopRight - 30);
                        GotThrough++;
                        Play(Sfx.Land);
                    }
                    break;
                case WolfState.Falling:
                    w.Vy += Gravity * dt;
                    w.Y += w.Vy * dt;
                    if (w.Y > Field.Ground + 40) w.Gone = true;
                    break;
                case WolfState.Running:
                    // Across to the foot of the cliff, then up to a free ledge (or to wait at the foot).
                    w.X += RunSpeed * dt;
                    if (w.X >= Field.FaceX - 20)
                    {
                        w.X = Field.FaceX - 20;
                        int free = Enumerable.Range(0, Field.Ledges.Length).Where(i => !Wolves.Any(o => o.Ledge == i)).DefaultIfEmpty(-1).Last();
                        if (free >= 0) { w.Ledge = free; w.State = WolfState.Climbing; Play(Sfx.Climb); }
                        // Every ledge taken: it waits at the foot of the cliff, harmless.
                        else w.State = WolfState.Ledge;
                    }
                    break;
                case WolfState.Climbing:
                    w.Y -= ClimbSpeed * dt;
                    if (w.Y <= Field.Ledges[w.Ledge] - Wolf.Radius)
                    {
                        w.Y = Field.Ledges[w.Ledge] - Wolf.Radius;
                        w.State = WolfState.Ledge;
                        w.X = Field.FaceX + 55;
                    }
                    break;
            }
        }
        Wolves.RemoveAll(w => w.Gone);
    }

    /// <summary>A rock lobbed at where the lift is.</summary>
    private void Throw(Wolf w)
    {
        var p = Player;
        double tx = Field.ShaftX, ty = p.Y, dx = tx - w.X, dy = ty - w.Y;
        double t = Math.Max(0.6, Math.Sqrt(dx * dx + dy * dy) / RockSpeed);
        Rocks.Add(new Rock { X = w.X + 10, Y = w.Y, Vx = dx / t, Vy = dy / t - 0.5 * Gravity * t });
        Play(Sfx.Throw);
    }

    /// <summary>Five at the top: they roll the boulder along and down the shaft.</summary>
    private void RollOut()
    {
        Boulder = new Boulder { X = 200, Y = Field.ClifftopY - Boulder.Radius, Vx = 260 };
        Say("LOOK OUT!", null, 1.5);
        Play(Sfx.Boulder);
    }

    private void RollBoulder(double dt)
    {
        var b = Boulder!;
        b.Spin += b.Vx * dt / Boulder.Radius + b.Vy * dt / Boulder.Radius;
        if (b.X < Field.ShaftX) b.X = Math.Min(Field.ShaftX, b.X + b.Vx * dt);
        else
        {
            b.Vy += Gravity * 2 * dt;
            b.Y += b.Vy * dt;
        }
        if (b.Y > Field.Ground + 60)
        {
            Boulder = null;
            // The wolves that rolled it go back to wait for the next lot.
            Wolves.RemoveAll(w => w.State == WolfState.Top);
        }
    }

    // ───────────────────────────── arrows, meat and rocks ─────────────────────────────

    private void MoveShots(double dt)
    {
        foreach (var a in Arrows)
        {
            a.X -= Arrow.Speed * dt;
            if (a.X < -20) { a.Gone = true; continue; }
            // A balloon pops; the wolf itself turns the arrow aside.
            if (Wolves.FirstOrDefault(w => w.State == WolfState.Floating && Hit(a.X, a.Y, w.X, w.BalloonY, Wolf.BalloonRadius + 2)) is { } w)
            {
                a.Gone = true;
                Pop(w, PopPoints * (Rising ? 2 : 1));
            }
            else if (Wolves.Any(o => o.State == WolfState.Floating && Hit(a.X, a.Y, o.X, o.Y, Wolf.Radius)))
            {
                a.Gone = true;
                Effects.Add(new Effect { Kind = EffectKind.Dust, X = a.X, Y = a.Y, Life = 0.3 });
            }
            else if (Rocks.FirstOrDefault(r => Hit(a.X, a.Y, r.X, r.Y, Rock.Radius + 4)) is { } r)
            {
                a.Gone = r.Gone = true;
                Award(RockPoints, r.X, r.Y);
                Play(Sfx.RockHit);
            }
        }
        Arrows.RemoveAll(a => a.Gone);
        if (Thrown is { } m)
        {
            m.X -= Meat.Speed * dt;
            foreach (var w in Wolves.Where(w => w.State == WolfState.Floating && (Hit(m.X, m.Y, w.X, w.BalloonY, Wolf.BalloonRadius + Meat.Radius) || Hit(m.X, m.Y, w.X, w.Y, Wolf.Radius + Meat.Radius))).ToList())
                Pop(w, PopPoints * 2 << m.Chain++);
            if (m.X < -30) Thrown = null;
        }
        foreach (var r in Rocks)
        {
            r.Vy += Gravity * dt;
            r.X += r.Vx * dt;
            r.Y += r.Vy * dt;
            if (r.Y > Field.Ground || r.X > Field.Width) r.Gone = true;
        }
        Rocks.RemoveAll(r => r.Gone);
    }

    private static bool Hit(double ax, double ay, double bx, double by, double r) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by) < r * r;

    private void Pop(Wolf w, int points)
    {
        w.State = WolfState.Falling;
        w.Vy = 40;
        Effects.Add(new Effect { Kind = EffectKind.Pop, X = w.X, Y = w.BalloonY, Life = 0.35, Colour = (uint)w.Balloon });
        Award(points, w.X, w.BalloonY);
        Play(Sfx.Pop);
        Play(Sfx.Fall);
    }

    /// <summary>What can get the pig: a wolf on a ledge level with the lift, a rock, the boulder.</summary>
    private void Dangers()
    {
        var p = Player;
        // Checked every step, not just as each wolf arrives: one may arrive while the last boulder's still falling.
        if (Boulder == null && AtTop >= TopToRoll) RollOut();
        if (Boulder != null)
        {
            RollBoulder(Field.Step);
            if (Boulder != null && Boulder.X >= Field.ShaftX && Math.Abs(Boulder.Y - p.Y) < Boulder.Radius + Pig.Radius) { Lose("the boulder"); return; }
        }
        if (Wolves.FirstOrDefault(w => w.State == WolfState.Ledge && w.Ledge >= 0 && Math.Abs(w.Y + Wolf.Radius - (p.Y + Pig.Radius)) < BiteReach) is { } biter)
        {
            Lose("bitten");
            Play(Sfx.Bite);
            return;
        }
        if (Rocks.FirstOrDefault(r => Hit(r.X, r.Y, Field.ShaftX, p.Y, Pig.Radius + Rock.Radius)) is { } rock)
        {
            rock.Gone = true;
            Lose("hit by a rock");
            Play(Sfx.Hit);
        }
    }

    private void Finish()
    {
        bool perfect = GotThrough == 0;
        Bonus = ClearBonus + (perfect ? PerfectBonus : 0);
        Play(Sfx.LevelClear);
        Say(perfect ? "PERFECT!" : "ROUND CLEAR!", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Lose(string cause)
    {
        Cause = cause;
        Player.Alive = false;
        Lives--;
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 14, Text = points.ToString(), Life = 0.8 });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a round, past its name, with nobody to come unless a test says so.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void PutPig(double y) => Player.Y = y;

    internal Wolf PutWolf(double x, double y, WolfState state = WolfState.Floating, double vy = 0, int ledge = -1)
    {
        var w = new Wolf { X = x, Y = y, State = state, Vy = vy, Ledge = ledge, ThrowIn = 99 };
        Wolves.Add(w);
        return w;
    }

    internal Rock PutRock(double x, double y, double vx, double vy)
    {
        var r = new Rock { X = x, Y = y, Vx = vx, Vy = vy };
        Rocks.Add(r);
        return r;
    }

    internal void SetToCome(int n) => ToCome = n;
    internal void GiveMeat() => MeatReady = true;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
